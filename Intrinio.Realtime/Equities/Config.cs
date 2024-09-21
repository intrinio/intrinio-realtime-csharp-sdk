using System.Collections.Generic;

namespace Intrinio.Realtime.Equities;

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

    /// <summary>
    /// The configuration for The Equities Websocket Client.
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
    }

    public static Config LoadConfig()
    {
        Log.Information("Loading application configuration");
        var rawConfig = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("config.json").Build();
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(rawConfig).CreateLogger();
        Config config = new Config();

        foreach (KeyValuePair<string, string> kvp in rawConfig.AsEnumerable())
        {
            Log.Debug("Key: {0}, Value:{1}", kvp.Key, kvp.Value);
        }
        
        rawConfig.Bind("Config", config);
        config.Validate();
        return config;
    }
}