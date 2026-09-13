#if UNITY_EDITOR

using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class OdinCompatibilityLogFilter
{
    private const string BlockedMessage =
        "Unity's internal custom editor management classes have changed in this version of Unity";

    private static readonly ILogHandler OriginalLogHandler;

    static OdinCompatibilityLogFilter()
    {
        OriginalLogHandler = Debug.unityLogger.logHandler;

        Debug.unityLogger.logHandler =
            new FilteredLogHandler(OriginalLogHandler);

        AssemblyReloadEvents.beforeAssemblyReload += RestoreOriginalLogHandler;
    }

    private static void RestoreOriginalLogHandler()
    {
        if (Debug.unityLogger.logHandler is FilteredLogHandler)
        {
            Debug.unityLogger.logHandler = OriginalLogHandler;
        }
    }

    private sealed class FilteredLogHandler : ILogHandler
    {
        private readonly ILogHandler _originalLogHandler;

        public FilteredLogHandler(ILogHandler originalLogHandler)
        {
            _originalLogHandler = originalLogHandler;
        }

        public void LogFormat(
            LogType logType,
            UnityEngine.Object context,
            string format,
            params object[] args)
        {
            if (ShouldSuppress(logType, format, args)) return;

            _originalLogHandler.LogFormat(
                logType,
                context,
                format,
                args);
        }

        public void LogException(
            Exception exception,
            UnityEngine.Object context)
        {
            _originalLogHandler.LogException(
                exception,
                context);
        }

        private static bool ShouldSuppress(
            LogType logType,
            string format,
            object[] args)
        {
            if (logType != LogType.Error) return false;

            if (!string.IsNullOrEmpty(format) &&
                format.Contains(
                    BlockedMessage,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (args == null) return false;

            foreach (object argument in args)
            {
                if (argument == null) continue;

                string message = argument.ToString();

                if (message.Contains(
                    BlockedMessage,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

#endif