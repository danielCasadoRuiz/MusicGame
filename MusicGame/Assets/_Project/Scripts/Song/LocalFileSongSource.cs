using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// ISongSource for "Play Your Song" (Section 5.B of the app-flow/Theme refactor plan) — asks an
/// ILocalSongPicker for a path, then decodes it via LocalAudioClipLoader. Neither step is
/// hardcoded here: swap the picker for a real native-dialog implementation later (see
/// LocalSongPickerFactory) without touching this class at all.
///
/// Optionally trims the decoded clip to AudioAnalysisConfig.manualPlayRangeStartSeconds/
/// EndSeconds (see that field's own doc — manual-range is a LOCAL-file-only concept; catalog/
/// automatic songs will get a real "interesting chunk" algorithm later instead). Trimming produces
/// a genuinely shorter, independent AudioClip via AudioClip.Create rather than special-casing an
/// offset anywhere downstream — AudioPreAnalyzer, MusicClock, GameplayManager and every progress
/// bar all just see "the whole song" and need no awareness that it was ever cut from something
/// longer.
/// </summary>
public class LocalFileSongSource : ISongSource
{
    private readonly ILocalSongPicker    _picker;
    private readonly AudioAnalysisConfig _config;
    private string _pickedPath;

    public LocalFileSongSource(ILocalSongPicker picker, AudioAnalysisConfig config = null)
    {
        _picker = picker;
        _config = config;
    }

    public string DisplayName => _pickedPath != null ? Path.GetFileNameWithoutExtension(_pickedPath) : Loc.Get("SongSelection.LocalFileDefaultName");

    public IEnumerator Load(Action<SelectedSongInfo?> onComplete)
    {
        _pickedPath = _picker?.PickFile();
        if (string.IsNullOrEmpty(_pickedPath))
        {
            onComplete?.Invoke(null); // cancelled — not an error
            yield break;
        }

        AudioClip loadedClip = null;
        yield return LocalAudioClipLoader.Load(_pickedPath, clip => loadedClip = clip);

        if (loadedClip == null)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        if (_config != null && _config.useManualPlayRange)
            loadedClip = TrimClip(loadedClip, _config.manualPlayRangeStartSeconds, _config.manualPlayRangeEndSeconds);

        onComplete?.Invoke(new SelectedSongInfo
        {
            DisplayName   = Path.GetFileNameWithoutExtension(_pickedPath),
            Clip          = loadedClip,
            LocalFilePath = _pickedPath,
        });
    }

    // A value of 0 (or beyond the clip's own length) for `endSeconds` means "to the end" — see
    // AudioAnalysisConfig.manualPlayRangeEndSeconds's own doc. Falls back to the ORIGINAL clip
    // (never crashes/throws) if the configured range doesn't leave anything sane to play.
    private static AudioClip TrimClip(AudioClip source, float startSeconds, float endSeconds)
    {
        int sampleRate    = source.frequency;
        int channels      = source.channels;
        int totalSamples  = source.samples;

        int startSample = Mathf.Clamp(Mathf.RoundToInt(startSeconds * sampleRate), 0, totalSamples - 1);
        int endSample   = endSeconds > 0f
            ? Mathf.Clamp(Mathf.RoundToInt(endSeconds * sampleRate), startSample + 1, totalSamples)
            : totalSamples;

        int length = endSample - startSample;
        if (length <= 0) return source;

        var data = new float[length * channels];
        source.GetData(data, startSample);

        var trimmed = AudioClip.Create(source.name + "_trimmed", length, channels, sampleRate, false);
        trimmed.SetData(data, 0);
        return trimmed;
    }
}
