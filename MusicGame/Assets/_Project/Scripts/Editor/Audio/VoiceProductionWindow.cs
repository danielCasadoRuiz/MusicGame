#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Audio > Voice Production. Generate the pending voices (all, or one artist / language),
/// listen to a line or a voice's reference, regenerate one selected line, cancel at any time (finished lines are
/// kept and imported). Everything it does is described in VoiceProduction; setup in Tools/VoiceGen/README.md.
/// </summary>
public class VoiceProductionWindow : EditorWindow
{
    private string _voice = "(all)", _language = "(all)";
    private bool _onlyPending = true;
    private string _filter = "";
    private Vector2 _scroll, _logScroll;
    private List<VoiceProduction.VoiceLine> _lines = new();
    private VoiceProduction.Manifest _manifest;
    private VoiceProduction.Run _run;
    private string _status = "", _check = "";
    private string _selected;

    [MenuItem("Tools/MusicGame/Audio/Voice Production")]
    public static void Open() => GetWindow<VoiceProductionWindow>("Voice Production").Refresh();

    private void OnEnable() { Refresh(); EditorApplication.update += Tick; }
    private void OnDisable() { EditorApplication.update -= Tick; }

    private void Refresh()
    {
        _manifest = VoiceProduction.LoadManifest();
        _lines = VoiceProduction.Inventory(_manifest);
        Repaint();
    }

    private void Tick()
    {
        if (_run == null) return;
        _run.Pump();
        if (_run.Exited)
        {
            _run.Process?.WaitForExit();
            _run.Pump();
            _status = VoiceProduction.Import(_manifest, _run.Touched) + (_run.Fatal != null ? "\nFATAL: " + _run.Fatal : "") + (_run.Cancelled ? "\n(cancelled — finished lines kept)" : "");
            _run = null;
            EditorUtility.ClearProgressBar();
            Refresh();
        }
        Repaint();
    }

    private IEnumerable<VoiceProduction.VoiceLine> Filtered() => _lines.Where(l =>
        (_voice == "(all)" || l.Voice == _voice) && (_language == "(all)" || l.Language == _language) &&
        (!_onlyPending || VoiceProduction.IsPending(l)) &&
        (string.IsNullOrEmpty(_filter) || l.Key.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 || l.Text.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0));

    private void OnGUI()
    {
        // Environment
        using (new EditorGUILayout.HorizontalScope())
        {
            bool installed = File.Exists(VoiceProduction.PythonExe);
            EditorGUILayout.LabelField(installed ? "Generator: installed (Tools/VoiceGen)" : "Generator: NOT installed — run Tools/VoiceGen/setup.ps1", EditorStyles.boldLabel);
            if (GUILayout.Button("Setup & docs", GUILayout.Width(110))) EditorUtility.OpenWithDefaultApp(Path.Combine(VoiceProduction.ToolRoot, "README.md"));
            if (GUILayout.Button("Check environment", GUILayout.Width(130))) _check = CheckEnvironment();
            if (GUILayout.Button("Refresh", GUILayout.Width(70))) Refresh();
        }
        if (!string.IsNullOrEmpty(_check)) EditorGUILayout.HelpBox(_check, MessageType.None);

        // Filters
        var voices = new[] { "(all)" }.Concat(_lines.Select(l => l.Voice).Distinct().OrderBy(v => v)).ToArray();
        var langs = new[] { "(all)" }.Concat(_lines.Select(l => l.Language).Distinct().OrderBy(v => v)).ToArray();
        using (new EditorGUILayout.HorizontalScope())
        {
            _voice = voices[Mathf.Max(0, EditorGUILayout.Popup("Artist", Array.IndexOf(voices, _voice), voices))];
            _language = langs[Mathf.Max(0, EditorGUILayout.Popup("Language", Array.IndexOf(langs, _language), langs))];
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _onlyPending = EditorGUILayout.ToggleLeft("Only pending", _onlyPending, GUILayout.Width(110));
            _filter = EditorGUILayout.TextField("Search", _filter);
        }

        // Summary
        var scope = _lines.Where(l => (_voice == "(all)" || l.Voice == _voice) && (_language == "(all)" || l.Language == _language)).ToList();
        EditorGUILayout.HelpBox(string.Join("   ", scope.GroupBy(l => l.State).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}")) +
                                (scope.Any(l => l.State == VoiceProduction.LineState.NoEngine) ? "\nNoEngine = a language without a generation engine yet (e.g. Catalan): those lines play as subtitles only." : ""),
                                MessageType.Info);

        // Actions
        using (new EditorGUI.DisabledScope(_run != null))
        using (new EditorGUILayout.HorizontalScope())
        {
            var pending = scope.Where(VoiceProduction.IsPending).ToList();
            if (GUILayout.Button($"Generate pending ({pending.Count})", GUILayout.Height(28)) && pending.Count > 0) StartRun(pending);
            var sel = _lines.FirstOrDefault(l => l.Id == _selected);
            using (new EditorGUI.DisabledScope(sel == null || sel.State == VoiceProduction.LineState.NoEngine))
                if (GUILayout.Button("Regenerate selected", GUILayout.Height(28)) && sel != null) StartRun(new List<VoiceProduction.VoiceLine> { sel });
            var def = VoiceProduction.Definitions().FirstOrDefault(d => d.voiceId == _voice);
            using (new EditorGUI.DisabledScope(def == null))
                if (GUILayout.Button("▶ Voice reference", GUILayout.Height(28)) && def != null) Play(VoiceProduction.ReferencePreview(def));
        }
        if (_run != null)
        {
            float p = _run.Total > 0 ? (float)_run.Done / _run.Total : 0f;
            var r = EditorGUILayout.GetControlRect(false, 20);
            EditorGUI.ProgressBar(r, p, $"{_run.Done}/{_run.Total}  {_run.Current}");
            if (GUILayout.Button("Cancel (keeps finished lines)")) _run.Cancel();
        }
        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);

        // Lines
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var l in Filtered().Take(400))
        {
            using (new EditorGUILayout.HorizontalScope(l.Id == _selected ? EditorStyles.helpBox : GUIStyle.none))
            {
                if (GUILayout.Button(l.Id == _selected ? "●" : "○", GUILayout.Width(22))) _selected = l.Id;
                using (new EditorGUI.DisabledScope(!File.Exists(Path.Combine(VoiceProduction.ProjectRoot, l.GamePath))))
                    if (GUILayout.Button("▶", GUILayout.Width(24))) Play(l.GamePath);
                GUILayout.Label($"{l.Voice}/{l.Language}", GUILayout.Width(150));
                GUILayout.Label(l.State.ToString(), GUILayout.Width(100));
                GUILayout.Label(l.Key, GUILayout.Width(260));
                GUILayout.Label(l.Text + (l.Done != null && !string.IsNullOrEmpty(l.Done.warnings) ? "   ⚠ " + l.Done.warnings : "") +
                                (l.Done != null && !l.Done.ok && !string.IsNullOrEmpty(l.Done.error) ? "   ✖ " + l.Done.error : ""));
            }
        }
        EditorGUILayout.EndScrollView();

        if (_run != null && _run.Log.Count > 0)
        {
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(110));
            foreach (var s in _run.Log.Skip(Math.Max(0, _run.Log.Count - 40))) EditorGUILayout.LabelField(s);
            EditorGUILayout.EndScrollView();
        }
    }

    private void StartRun(List<VoiceProduction.VoiceLine> lines)
    {
        _status = "";
        _run = VoiceProduction.Start(_manifest, lines);
    }

    private static string CheckEnvironment()
    {
        if (!File.Exists(VoiceProduction.PythonExe)) return "Not installed: open PowerShell in Tools/VoiceGen and run ./setup.ps1 (see README).";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(VoiceProduction.PythonExe, "voicegen.py --check")
            { WorkingDirectory = VoiceProduction.ToolRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using var p = System.Diagnostics.Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(60000);
            return o.Split('\n').LastOrDefault(s => s.Contains("\"check\"")) ?? o;
        }
        catch (Exception e) { return e.Message; }
    }

    // Editor preview through the internal AudioUtil (no scene needed).
    private static void Play(string assetPath)
    {
        AssetDatabase.Refresh();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        if (clip == null) { Debug.LogWarning("[VoiceProduction] nothing to play at " + assetPath); return; }
        var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        util?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
        var play = util?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        play?.Invoke(null, new object[] { clip, 0, false });
    }
}
#endif
