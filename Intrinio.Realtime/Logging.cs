using System;

namespace Intrinio.Realtime;

public static class Logging
{
    [Serilog.Core.MessageTemplateFormatMethod("messageTemplate")]
    public static void Log(LogLevel logLevel, string messageTemplate, params object[] propertyValues)
    {
        switch (logLevel)
        {
            case LogLevel.VERBOSE:
                Serilog.Log.Verbose(messageTemplate, propertyValues);
                break;
            case LogLevel.DEBUG:
                Serilog.Log.Debug(messageTemplate, propertyValues);
                break;
            case LogLevel.INFORMATION:
                Serilog.Log.Information(messageTemplate, propertyValues);
                break;
            case LogLevel.WARNING:
                Serilog.Log.Warning(messageTemplate, propertyValues);
                break;
            case LogLevel.ERROR:
                Serilog.Log.Error(messageTemplate, propertyValues);
                break;
            default:
                throw new ArgumentException("LogLevel not specified!");
                break;
        }
    }
}