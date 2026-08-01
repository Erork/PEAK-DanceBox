using System;
using BepInEx.Logging;

namespace PEAKEmoteLib;

internal enum DanceLogLevel
{
    Off = 0,
    ErrorsOnly = 1,
    Warnings = 2,
    Info = 3,
    Debug = 4
}

/// <summary>
/// Centralized logging gate. Normal informational spam is disabled by default;
/// errors remain available unless the user explicitly selects Off.
/// </summary>
internal static class DanceLog
{
    private static ManualLogSource? source;
    private static DanceLogLevel level = DanceLogLevel.ErrorsOnly;

    public static DanceLogLevel Level => level;

    public static void Initialize(ManualLogSource logger, string configuredLevel)
    {
        source = logger;
        SetLevel(configuredLevel);
    }

    public static void SetLevel(string configuredLevel)
    {
        if (!Enum.TryParse(configuredLevel, true, out DanceLogLevel parsed))
        {
            parsed = DanceLogLevel.ErrorsOnly;
        }
        level = parsed;
    }

    public static bool IsEnabled(DanceLogLevel required) => source != null && level >= required;

    public static void Debug(object data)
    {
        if (IsEnabled(DanceLogLevel.Debug)) source!.LogDebug(data);
    }

    public static void Info(object data)
    {
        if (IsEnabled(DanceLogLevel.Info)) source!.LogInfo(data);
    }

    public static void Warning(object data)
    {
        if (IsEnabled(DanceLogLevel.Warnings)) source!.LogWarning(data);
    }

    public static void Error(object data)
    {
        if (IsEnabled(DanceLogLevel.ErrorsOnly)) source!.LogError(data);
    }
}
