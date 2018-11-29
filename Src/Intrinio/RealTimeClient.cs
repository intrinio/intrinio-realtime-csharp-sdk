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
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

[assembly: log4net.Config.XmlConfigurator(Watch = true)]

namespace Intrinio
{
    class IexMessage
    {
        public String Event { get;  }
        public IexQuote Payload { get; }

        public IexMessage(String Event, IexQuote Payload) {
            this.Event = Event;
            this.Payload = Payload;
        }
    }

    class QuoddMessage
    {
        public String Event { get; }
        public JObject Data { get; }

        public QuoddMessage(String Event, JObject Data)
        {
            this.Event = Event;
            this.Data = Data;
        }
    }

    class QuoddInfoData
    {
        public String Action { get; }
        public String Message { get; }

        public QuoddInfoData(String Action, String Message)
        {
            this.Action = Action;
            this.Message = Message;
        }
    }

    /// <summary>
    /// The available providers/vendors of real-time price quotes. Each provider requires a subscription from https://intrinio.com
    /// </summary>
    public enum QuoteProvider {
        /// <summary>
        /// The Investor's Exchange https://iextrading.com/
        /// </summary>
        IEX,
        /// <summary>
        /// QUODD http://home.quodd.com/
        /// </summary>
        QUODD,
    };

    /// <summary>
    /// Intrinio's client for receiving real-time stock prices via a WebSocket connection.
    /// </summary>
    public class RealTimeClient : IDisposable
    {
        /// <summary>
        /// A log4net logger instance. By default a console logger that logs INFO-level messages, but you can set your own configured logger.
        /// </summary>
        public ILog Logger { get; set; }

        private string api_key;
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

        /// <summary>
        /// Initializes a new real-time client instance.
        /// </summary>
        /// <param name="api_key">Your Intrinio API Key</param>
        /// <param name="username">Your Intrinio API Username</param>
        /// <param name="password">Your Intrinio API Password</param>
        /// <param name="provider">A QuoteProvider</param>
        public RealTimeClient(QuoteProvider provider, string username = null, string password = null, string api_key = null)
        {
            this.api_key = api_key;
            this.username = username;
            this.password = password;
            this.provider = provider;

            if (String.IsNullOrEmpty(this.api_key))
            {
                if (String.IsNullOrEmpty(this.username) && String.IsNullOrEmpty(this.password))
                {
                    throw new ArgumentException("Must provide an API key or username and password");
                }

                if (String.IsNullOrEmpty(this.username))
                {
                    throw new ArgumentException("Must provide a valid username");
                }

                if (String.IsNullOrEmpty(this.password))
                {
                    throw new ArgumentException("Must provide a valid password");
                }
            }

            this.Logger = LogManager.GetLogger(this.GetType().FullName);

            Thread heartbeat = new Thread(new ThreadStart(this.SendHeartbeat));
            heartbeat.Start();
            this.runningThreads.Add(heartbeat);
        }

        /// <summary>
        /// Establishes a WebSocket connection and starts listening for price quotes. Attempts to self-heal if the connection is interrupted or dropped. This method will return after a connection is established. You may want to block the thread afterwards in order to allow the client to keep listening for prices.
        /// </summary>
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

        /// <summary>
        /// Severs the WebSocket connection and stops listening for quotes.
        /// </summary>
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

        /// <summary>
        /// Registers a QuoteHandler instance to handle quotes in the client's queue. Multiple QuoteHandler instances can be registered. Quotes will be taken off the queue and given to the next available QuoteHander.
        /// </summary>
        /// <param name="handler">An instance of QuoteHandler</param>
        public void RegisterQuoteHandler(QuoteHandler handler)
        {
            handler.Client = this;
            ThreadStart threadStart = new ThreadStart(handler.Listen);
            Thread thread = new Thread(threadStart);
            thread.Start();
            this.runningThreads.Add(thread);
            this.Logger.Debug("Registered quote handler");
        }

        /// <summary>
        /// Blocks until a quote can be dequeued from the queue/
        /// </summary>
        /// <returns>An IQuote</returns>
        public IQuote GetNextQuote()
        {
            IQuote quote = null;
            while (this.queue.TryDequeue(out quote) != true) { }
            return quote;
        }

        /// <summary>
        /// Returns the size of the quote queue. Monitor this to make sure your QuoteHandler instances are not falling behind.
        /// </summary>
        /// <returns>An integer representing the size of the quote queue</returns>
        public int QueueSize()
        {
            return this.queue.Count;
        }

        /// <summary>
        /// Listen for price quotes on the given channel.
        /// </summary>
        /// <param name="channel">A channel to join, which may be a security ticker such as "AAPL"</param>
        public void Join(string channel)
        {
            this.Join(new string[] { channel });
        }

        /// <summary>
        /// Listen for price quotes on the given channels.
        /// </summary>
        /// <param name="channels">The channels to join, which may be a list of security tickers such as "AAPL"</param>
        public void Join(string[] channels)
        {
            foreach (string channel in channels)
            {
                this.channels.Add(channel);
            }
            this.RefreshChannels();
        }

        /// <summary>
        /// Stop listening for price quotes on the given channel.
        /// </summary>
        /// <param name="channel">A channel to leave, which may be a security ticker such as "AAPL"</param>
        public void Leave(string channel)
        {
            this.Leave(new string[] { channel });
        }

        /// <summary>
        /// Stop listening for price quotes on the given channels.
        /// </summary>
        /// <param name="channels">The channels to leave, which may be a list of security tickers such as "AAPL"</param>
        public void Leave(string[] channels)
        {
            foreach (string channel in channels)
            {
                this.channels.Remove(channel);
            }
            this.RefreshChannels();
        }

        /// <summary>
        /// Stop listening for price quotes on all channels.
        /// </summary>
        public void LeaveAll()
        {
            this.channels.Clear();
            this.RefreshChannels();
        }

        /// <summary>
        /// Stop listening for price quotes on all channels, then start listening for price quotes on the given channels
        /// </summary>
        /// <param name="channels">The channels to join, which may be a list of security tickers such as "AAPL"</param>
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

        /// <summary>
        /// Disposes of the client. 
        /// </summary>
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
            string url = null;
            if (this.provider == QuoteProvider.IEX)
            {
                url = "https://realtime.intrinio.com/auth";
            }
            else if (this.provider == QuoteProvider.QUODD)
            {
                url = "https://api.intrinio.com/token?type=QUODD";
            }

            if (!String.IsNullOrEmpty(url) && !String.IsNullOrEmpty(this.api_key))
            {
                url = this.MakeUrlAuthUrl(url);
            }

            return url;
        }

        private string MakeUrlAuthUrl(string auth_url)
        {
            if (auth_url.Contains("?"))
            {
                auth_url = auth_url + "&";
            }
            else
            {
                auth_url = auth_url + "?";
            }

            return auth_url + "api_key=" + this.api_key;
        }

        private void RefreshToken()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(this.MakeAuthUrl());

            if (String.IsNullOrEmpty(this.api_key))
            {
                String encoded = System.Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(this.username + ":" + this.password));
                request.Headers.Add("Authorization", "Basic " + encoded);
            }

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
            else if (this.provider == QuoteProvider.QUODD)
            {
                return "wss://www5.quodd.com/websocket/webStreamer/intrinio/" + this.token;
            }
            return null;
        }

        private void RefreshWebSocket()
        {
            this.ws = new WebSocket(this.MakeWebSocketUrl());

            this.ws.OnOpen += (sender, e) => {
                this.Logger.Info("Websocket connected!");
                if (this.provider == QuoteProvider.IEX)
                {
                    this.ready = true;
                    this.RefreshChannels();
                }
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
                this.Logger.Info(e.Data);

                if (this.provider == QuoteProvider.IEX)
                {
                    IexMessage message = JsonConvert.DeserializeObject<IexMessage>(e.Data);
                    if (message.Event == "quote")
                    {
                        quote = message.Payload;
                    }
                }
                else if (this.provider == QuoteProvider.QUODD)
                {
                    QuoddMessage message = JsonConvert.DeserializeObject<QuoddMessage>(e.Data);
                    if (message.Event == "info")
                    {
                        QuoddInfoData info = message.Data.ToObject<QuoddInfoData>();
                        if (info.Message == "Connected")
                        {
                            this.ready = true;
                            this.RefreshChannels();
                        }
                        else
                        {
                            this.Logger.Info(info.Message);
                        }
                    }
                    else if (message.Event == "quote")
                    {
                        quote = message.Data.ToObject<QuoddBookQuote>();
                    }
                    else if (message.Event == "trade")
                    {
                        quote = message.Data.ToObject<QuoddTradeQuote>();
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
            else if (this.provider == QuoteProvider.QUODD)
            {
                message = "{\"event\": \"unsubscribe\", \"data\": { \"ticker\": " + channel + ", \"action\": \"unsubscribe\"}}";
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
            else if (this.provider == QuoteProvider.QUODD)
            {
                message = "{\"event\": \"subscribe\", \"data\": { \"ticker\": " + channel + ", \"action\": \"subscribe\"}}";
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
                        if (this.provider == QuoteProvider.IEX)
                        {
                            this.ws.Send("{\"topic\":\"phoenix\",\"event\":\"heartbeat\",\"payload\":{},\"ref\":null}");
                        }
                        else if (this.provider == QuoteProvider.QUODD)
                        {
                            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                            var timestamp = (DateTime.UtcNow - epoch).TotalMilliseconds;
                            this.ws.Send("{\"event\": \"heartbeat\", \"data\": {\"action\": \"heartbeat\", \"ticker\": " + timestamp + "}}");
                        }

                    }
                }
            }
            catch (ThreadAbortException e) { }
        }

        #endregion
    }
}
