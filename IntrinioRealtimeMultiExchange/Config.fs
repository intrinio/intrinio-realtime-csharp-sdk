namespace Intrinio

module Config =

    open System
    open Serilog
    open System.IO
    open Microsoft.Extensions.Configuration

    type Config () =
        member val ApiKey : string = null with get, set
        member val Provider : Provider = Provider.NONE with get, set
        member val IPAddress : string = null with get, set
        member val Symbols: string[] = [||] with get, set
        member val TradesOnly: bool = false with get, set
        member val NumThreads: int = 4 with get, set

    let LoadConfig() =
        Log.Information("Loading application configuration")
        let _config = ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("config.json").Build()
        Log.Logger <- LoggerConfiguration().ReadFrom.Configuration(_config).CreateLogger()
        let mutable config = new Config()
        for (KeyValue(key,value)) in _config.AsEnumerable() do Log.Debug("Key: {0}, Value:{1}", key, value)
        _config.Bind("Config", config)
        if String.IsNullOrWhiteSpace(config.ApiKey)
        then failwith "You must provide a valid API key"
        if (config.Provider = Provider.NONE)
        then failwith "You must specify a valid 'provider'"
        if ((config.Provider = Provider.MANUAL) || (config.Provider = Provider.MANUAL_FIREHOSE)) && (String.IsNullOrWhiteSpace(config.IPAddress))
        then failwith "You must specify an IP address for manual configuration"
        config
