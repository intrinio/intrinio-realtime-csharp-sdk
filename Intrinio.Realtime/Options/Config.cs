using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

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
        Delayed = false;
    }

    /// <summary>
    /// Attempts to convert a contract of the form AAPL250130P00010000 (no underscore padding of symbol) to AAPL__250130P00010000 (6 char symbol with right-pad underscore).
    /// </summary>
    /// <param name="nonstandardContract"></param>
    /// <returns>A standard formatted contract.</returns>
    public static string ConvertNonstandardContractToStandardContract(string nonstandardContract)
    {
        string nonTickerPart = nonstandardContract.Substring(nonstandardContract.Length - 15, 15);
        int    tickerLength  = nonstandardContract.Length - 15; 
        string tickerPart    = nonstandardContract.Substring(0, tickerLength);
        StringBuilder sb = new StringBuilder(21);
        sb.Append(tickerPart);
        for (int i = 0; i < (6 - tickerLength); i++)
            sb.Append('_');
        sb.Append(nonTickerPart);
        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string TranslateContract(string contract)
    {
        if ((contract.Length <= 9) || (contract.IndexOf(".")>=9))
        {
            return contract;
        }

        if (contract.Length < 21)
        {
            //This is the nonstandard format with no underscores at all.  
            return TranslateContract(ConvertNonstandardContractToStandardContract(contract));
        }
        
        //this is of the standard format and we need to translate it to what the server understands. input: AAPL__220101C00140000, TSLA__221111P00195000
        string symbol     = contract.Substring(0, 6).TrimEnd('_');
        string date       = contract.Substring(6, 6);
        char   callPut    = contract[12];
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