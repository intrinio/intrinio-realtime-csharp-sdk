using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Intrinio.Realtime.Options;

using System;
using Serilog;
using System.IO;
using Microsoft.Extensions.Configuration;

public class Config
{
    public string ApiKey { get; set; }

    public Provider Provider { get; set; }
    
    public string IPAddress { get; set; }
    
    public string[] Symbols { get; set; }
    
    public bool TradesOnly { get; set; }
    
    public int NumThreads { get; set; }
    
    public int BufferSize { get; set; }
    
    public int OverflowBufferSize { get; set; }
    
    public bool Delayed { get; set; }

    /// <summary>
    /// The configuration for The Options Websocket Client.
    /// </summary>
    public Config()
    {
        ApiKey = String.Empty;
        Provider = Provider.NONE;
        IPAddress = String.Empty;
        Symbols = Array.Empty<string>();
        TradesOnly = false;
        NumThreads = 2;
        BufferSize = 2048;
        OverflowBufferSize = 2048;
        Delayed = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string TranslateContract(string contract)
    {
        if ((contract.Length <= 9) || (contract.IndexOf(".")>=9))
        {
            return contract;
        }
        
        //this is of the old format and we need to translate it to what the server understands. input: AAPL__220101C00140000, TSLA__221111P00195000
        string symbol = contract.Substring(0, 6).TrimEnd('_');
        string date = contract.Substring(6, 6);
        char callPut = contract[12];
        string wholePrice = contract.Substring(13, 5).TrimStart('0');
        if (wholePrice == String.Empty)
        {
            wholePrice = "0";
        }

        string decimalPrice = contract.Substring(18);

        if (decimalPrice[2] == '0')
            decimalPrice = decimalPrice.Substring(0, 2);

        return String.Format($"{symbol}_{date}{callPut}{wholePrice}.{decimalPrice}");
    }

    public void Validate()
    {
        if (String.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ArgumentException("You must provide a valid API key");
        }

        if (Provider == Provider.NONE)
        {
            throw new ArgumentException("You must specify a valid 'provider'");
        }

        if ((Provider == Provider.MANUAL) && (String.IsNullOrWhiteSpace(IPAddress)))
        {
            throw new ArgumentException("You must specify an IP address for manual configuration");
        }

        if (NumThreads <= 0)
        {
            throw new ArgumentException("You must specify a valid 'NumThreads'");
        }

        if (BufferSize < 2048)
        {
            throw new ArgumentException("'BufferSize' must be greater than or equal to 2048.");
        }
        
        if (OverflowBufferSize < 2048)
        {
            throw new ArgumentException("'OverflowBufferSize' must be greater than or equal to 2048.");
        }

        for (int i = 0; i < Symbols.Length; i++)
        {
            Symbols[i] = TranslateContract(Symbols[i]);
        }
    }

    public static Config LoadConfig()
    {
        var rawConfig = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("config.json").Build();
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(rawConfig).CreateLogger();
        Logging.Log(LogLevel.VERBOSE, "Loading application configuration");
        Config config = new Config();

        foreach (KeyValuePair<string, string> kvp in rawConfig.AsEnumerable())
        {
            Logging.Log(LogLevel.DEBUG, "Key: {0}, Value:{1}", kvp.Key, kvp.Value);
        }
        
        rawConfig.Bind("OptionsConfig", config);
        config.Validate();
        return config;
    }
}