using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One place every segmented-garment message goes through ("[GarmentLayering]" prefix): the same unknown
/// region or unresolved slot would otherwise be logged on every rebuild, so each distinct message is logged
/// once per session. Tests and the importer subscribe to Reported to collect messages.
/// </summary>
public static class GarmentDiagnostics
{
    public const string Prefix = "[GarmentLayering]";

    /// <summary>Every report, including repeats that were not logged again.</summary>
    public static event Action<LogType, string> Reported;

    private static readonly HashSet<string> s_logged = new(StringComparer.Ordinal);

    public static void Info(string message) => Report(LogType.Log, message);
    public static void Warn(string message) => Report(LogType.Warning, message);
    public static void Error(string message) => Report(LogType.Error, message);

    /// <summary>Forgets which messages were already logged (tests, or a new import).</summary>
    public static void ResetLogged() => s_logged.Clear();

    private static void Report(LogType type, string message)
    {
        Reported?.Invoke(type, message);
        if (!s_logged.Add(message)) return;
        string line = $"{Prefix} {message}";
        if (type == LogType.Error) Debug.LogError(line);
        else if (type == LogType.Warning) Debug.LogWarning(line);
        else Debug.Log(line);
    }
}
