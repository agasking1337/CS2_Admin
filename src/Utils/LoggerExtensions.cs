using System;
using Microsoft.Extensions.Logging;

namespace CS2_Admin.Utils;

public static class LoggerExtensions
{
    public static void LogInformationIfEnabled(this ILogger logger, string message, params object?[] args)
    {
        if (!DebugSettings.LoggingEnabled)
            return;

        logger.LogInformation(message, args);
    }

    public static void LogWarningIfEnabled(this ILogger logger, string message, params object?[] args)
    {
        if (!DebugSettings.LoggingEnabled)
            return;

        logger.LogWarning(message, args);
    }

    public static void LogErrorIfEnabled(this ILogger logger, string message, params object?[] args)
    {
        if (!DebugSettings.LoggingEnabled)
            return;

        logger.LogError(message, args);
    }

    public static void LogErrorIfEnabled(this ILogger logger, Exception exception, string message, params object?[] args)
    {
        if (!DebugSettings.LoggingEnabled)
            return;

        logger.LogError(exception, message, args);
    }
}
