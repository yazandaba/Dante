using Microsoft.Extensions.Logging;

namespace Dante;

internal static class LoggerFactory
{
    public static ILogger<T> Create<T>()
    {
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<T>();
        return logger;
    }

    public static ILogger Create(Type t)
    {
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger(t);
        return logger;
    }
}