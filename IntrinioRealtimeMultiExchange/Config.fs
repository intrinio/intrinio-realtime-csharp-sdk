namespace Intrinio

module Config =

    open System
    open Serilog
    open System.IO
    open Microsoft.Extensions.Configuration

    type Config () =
        let mutable apiKey : string = String.Empty
        let mutable provider : Provider = Provider.NONE        
        let mutable ipAddress : string = String.Empty
        let mutable numThreads : int = 2
        
        member this.ApiKey with get () : string = apiKey and set (value : string) = apiKey <- value
        member this.Provider with get () : Provider = provider and set (value : Provider) = provider <- value
        member this.IPAddress with get () : string = ipAddress and set (value : string) = ipAddress <- value
        member val Symbols: string[] = [||] with get, set
        member val TradesOnly: bool = false with get, set
        member this.NumThreads with get () : int = numThreads and set (value : int) = numThreads <- value
        
        member _.Validate() : unit =
            if String.IsNullOrWhiteSpace(apiKey)
            then failwith "You must provide a valid API key"
            if (provider = Provider.NONE)
            then failwith "You must specify a valid 'provider'"
            if ((provider = Provider.MANUAL) && (String.IsNullOrWhiteSpace(ipAddress)))
            then failwith "You must specify an IP address for manual configuration"
            if (numThreads <= 0)
            then failwith "You must specify a valid 'NumThreads'"

    let LoadConfig() =
        Log.Information("Loading application configuration")
        let _config = ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("config.json").Build()
        Log.Logger <- LoggerConfiguration().ReadFrom.Configuration(_config).CreateLogger()
        let mutable config = new Config()
        for (KeyValue(key,value)) in _config.AsEnumerable() do Log.Debug("Key: {0}, Value:{1}", key, value)
        _config.Bind("Config", config)
        config.Validate()
        config
