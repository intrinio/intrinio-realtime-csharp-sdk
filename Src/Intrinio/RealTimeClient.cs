using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using WebSocketSharp;
using Newtonsoft.Json;
using log4net;

[assembly: log4net.Config.XmlConfigurator(ConfigFile = "log4net.config", Watch = true)]

namespace Intrinio
{
    class PhoenixMessage
    {
        public String Event { get;  }
        public IexQuote Payload { get; }

        public PhoenixMessage(String Event, IexQuote Payload) {
            this.Event = Event;
            this.Payload = Payload;
        }
    }

    public enum QuoteProvider { IEX };

    public class RealTimeClient : IDisposable
    {
        public ILog Logger { get; set; }

        private string username;
        private string password;
        private QuoteProvider provider;
        private string token;
        private WebSocket ws;
        private bool ready = false;
        private ConcurrentQueue<IQuote> queue = new ConcurrentQueue<IQuote>();
        private List<Thread> runningThreads = new List<Thread>();
        private HashSet<string> channels = new HashSet<string>();
        private HashSet<string> joinedChannels = new HashSet<string>();

        private readonly int SELF_HEAL_TIME = 1000;
        private readonly int HEARTBEAT_INTERVAL = 1000;
        private readonly string IEX_HEARTBEAT_MSG = "{\"topic\":\"phoenix\",\"event\":\"heartbeat\",\"payload\":{},\"ref\":null}";

        #region Public Methods

        public RealTimeClient(string username, string password, QuoteProvider provider)
        {
            this.username = username;
            this.password = password;
            this.provider = provider;

            if (this.username.Length == 0)
            {
                throw new ArgumentException("Must provide a valid username");
            }

            if (this.password.Length == 0)
            {
                throw new ArgumentException("Must provide a valid password");
            }

            this.Logger = LogManager.GetLogger(this.GetType().FullName);

            if (provider == QuoteProvider.IEX)
            {
                Thread heartbeat = new Thread(new ThreadStart(this.SendHeartbeat));
                heartbeat.Start();
                this.runningThreads.Add(heartbeat);
            }
        }

        public void Connect()
        {
            this.Logger.Info("Connecting...");

            this.ready = false;
            this.joinedChannels = new HashSet<String>();

            if (this.ws != null && this.ws.IsAlive)
            {
                this.ws.Close();
            }

            try
            {
                this.RefreshToken();
                this.RefreshWebSocket();
            }
            catch (Exception e)
            {
                this.Logger.Error("Cannot connect", e);
                this.SelfHeal();
            }
        }

        public void Disconnect()
        {
            this.ready = false;
            this.joinedChannels = new HashSet<String>();

            if (this.ws != null && this.ws.IsAlive)
            {
                this.ws.Close();
            }

            this.Logger.Info("Disconnected!");
        }

        public void RegisterQuoteHandler(QuoteHandler handler)
        {
            handler.Client = this;
            ThreadStart threadStart = new ThreadStart(handler.Listen);
            Thread thread = new Thread(threadStart);
            thread.Start();
            this.runningThreads.Add(thread);
            this.Logger.Debug("Registered quote handler");
        }

        public IQuote GetNextQuote()
        {
            IQuote quote = null;
            while (this.queue.TryDequeue(out quote) != true) { }
            return quote;
        }

        public void Join(string channel)
        {
            this.Join(new string[] { channel });
        }

        public void Join(string[] channels)
        {
            foreach (string channel in channels)
            {
                this.channels.Add(channel);
            }
            this.RefreshChannels();
        }

        public void Leave(string channel)
        {
            this.Leave(new string[] { channel });
        }

        public void Leave(string[] channels)
        {
            foreach (string channel in channels)
            {
                this.channels.Remove(channel);
            }
            this.RefreshChannels();
        }

        public void LeaveAll()
        {
            this.channels.Clear();
            this.RefreshChannels();
        }
        
        public void SetChannels(string[] channels)
        {
            this.channels.Clear();
            foreach (string channel in channels)
            {
                this.channels.Add(channel);
            }
            this.RefreshChannels();
        }

        #endregion

        #region Private Methods

        public void Dispose()
        {
            this.Logger.Debug("Client disposing...");

            this.Disconnect();

            this.runningThreads.ForEach(delegate (Thread thread)
            {
                thread.Abort();
            });
        }

        private void SelfHeal()
        {
            this.Logger.Info("Retrying connection...");
            Thread.Sleep(SELF_HEAL_TIME);
            this.Connect();
        }

        private string MakeAuthUrl()
        {
            if (this.provider == QuoteProvider.IEX)
            {
                return "https://realtime.intrinio.com/auth";
            }
            return null;
        }

        private void RefreshToken()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(this.MakeAuthUrl());
            String encoded = System.Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(this.username + ":" + this.password));
            request.Headers.Add("Authorization", "Basic " + encoded);

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using (Stream stream = response.GetResponseStream())
                {
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    this.token = reader.ReadToEnd();
                    this.Logger.Info("Authorization successful!");
                }
            }
            else
            {
                throw new Exception("Authorization status code: " + response.StatusCode);
            }
        }

        private string MakeWebSocketUrl()
        {
            if (this.provider == QuoteProvider.IEX)
            {
                return "wss://realtime.intrinio.com/socket/websocket?vsn=1.0.0&token=" + this.token;
            }
            return null;
        }

        private void RefreshWebSocket()
        {
            this.ws = new WebSocket(this.MakeWebSocketUrl());

            this.ws.OnOpen += (sender, e) => {
                this.Logger.Info("Websocket connected!");
                this.ready = true;
                this.RefreshChannels();
            };

            this.ws.OnClose += (sender, e) => {
                this.Logger.Info("Websocket closed!");
                if (!(e.Code == 1001 || e.Code == 1005))
                {
                    this.SelfHeal();
                }
            };

            this.ws.OnError += (sender, e) => {
                this.Logger.Error("Websocket error!", e.Exception);
            };

            this.ws.OnMessage += (sender, e) => {
                IQuote quote = null;

                if (this.provider == QuoteProvider.IEX)
                {
                    PhoenixMessage message = JsonConvert.DeserializeObject<PhoenixMessage>(e.Data);
                    if (message.Event == "quote")
                    {
                        quote = message.Payload;
                    }
                }

                if (quote != null)
                {
                    this.Logger.Debug("Websocket quote received: " + quote);
                    this.queue.Enqueue(quote);
                }
                else
                {
                    this.Logger.Debug("Websocket non-quote message: " + e.Data);
                }
            };

            this.ws.Connect();
        }

        private void RefreshChannels()
        {
            if (!this.ready)
            {
                return;
            }

            if (this.channels.Count == 00 && this.joinedChannels.Count == 0)
            {
                return;
            }

            // Join new channels
            HashSet<string> newChannels = new HashSet<string>(this.channels);
            this.Logger.Debug("New channels: " + String.Join(", ", newChannels));
            foreach (string channel in this.joinedChannels)
            {
                newChannels.Remove(channel);
            }
            foreach (string channel in newChannels)
            {
                String msg = this.MakeJoinMessage(channel);
                this.ws.Send(msg);
                this.Logger.Info("Joined channel " + channel);
            }

            // Leave old channels
            HashSet<string> oldChannels = new HashSet<string>(this.joinedChannels);
            this.Logger.Debug("Old channels: " + String.Join(", ", oldChannels));
            foreach (string channel in this.channels)
            {
                oldChannels.Remove(channel);
            }
            foreach (string channel in oldChannels)
            {
                String msg = this.MakeLeaveMessage(channel);
                this.ws.Send(msg);
                this.Logger.Info("Left channel " + channel);
            }

            this.joinedChannels = new HashSet<string>(this.channels);
            this.Logger.Debug("Current channels: " + String.Join(", ", this.joinedChannels));
        }

        private string MakeLeaveMessage(string channel)
        {
            String message = "";

            if (this.provider == QuoteProvider.IEX)
            {
                message = "{\"topic\":\"" + this.ParseIexTopic(channel) + "\",\"event\":\"phx_leave\",\"payload\":{},\"ref\":null}";
            }

            return message;
        }

        private string MakeJoinMessage(string channel)
        {
            String message = "";

            if (this.provider == QuoteProvider.IEX)
            {
                message = "{\"topic\":\"" + this.ParseIexTopic(channel) + "\",\"event\":\"phx_join\",\"payload\":{},\"ref\":null}";
            }

            return message;
        }

        private string ParseIexTopic(string channel)
        {
            switch (channel)
            {
                case "$lobby":
                    return "iex:lobby";
                case "$lobby_last_price":
                    return "iex:lobby:last_price";
                default:
                    return "iex:securities:" + channel;
            }
        }

        private void SendHeartbeat()
        {
            try
            {
                while (true)
                {
                    Thread.Sleep(HEARTBEAT_INTERVAL);
                    if (this.ws != null && this.ws.IsAlive)
                    {
                        this.Logger.Debug("Sending heartbeat");
                        this.ws.Send(IEX_HEARTBEAT_MSG);
                    }
                }
            }
            catch (ThreadAbortException e) { }
        }

        #endregion
    }
}
