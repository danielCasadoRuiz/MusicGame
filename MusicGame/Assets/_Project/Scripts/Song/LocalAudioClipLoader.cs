using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Loads a WAV/MP3 file from an absolute local path into a real AudioClip, at runtime, on every
/// platform Unity supports — UnityWebRequestMultimedia is the correct cross-platform way to do
/// this (works in actual builds, unlike any Editor-only asset-import API). Used by
/// LocalFileSongSource once ILocalSongPicker has returned a path; this class knows nothing about
/// how that path was chosen.
/// </summary>
public static class LocalAudioClipLoader
{
    public static IEnumerator Load(string filePath, Action<AudioClip> onComplete)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogWarning($"[LocalAudioClipLoader] File not found: '{filePath}'.");
            onComplete?.Invoke(null);
            yield break;
        }

        AudioType audioType = GuessAudioType(filePath);
        if (audioType == AudioType.UNKNOWN)
        {
            Debug.LogWarning($"[LocalAudioClipLoader] Unsupported extension for '{filePath}' — only WAV/MP3 are supported.");
            onComplete?.Invoke(null);
            yield break;
        }

        // Uri.AbsoluteUri (not naive string concatenation) correctly escapes spaces/special
        // characters in the path on every platform.
        string uri = new Uri(filePath).AbsoluteUri;

        using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[LocalAudioClipLoader] Failed to load '{filePath}': {request.error}");
            onComplete?.Invoke(null);
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
        onComplete?.Invoke(clip);
    }

    private static AudioType GuessAudioType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => AudioType.WAV,
            ".mp3" => AudioType.MPEG,
            _      => AudioType.UNKNOWN,
        };
    }
}
