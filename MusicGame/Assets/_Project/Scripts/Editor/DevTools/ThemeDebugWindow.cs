#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Runtime Theme forcing/preview tool (Tools > MusicGame > Theme Debugger) — lets a developer force
/// a Music Style, a Frontend Visual preset or an Event override without needing a real analyzed
/// song or a real Event activation system (neither exists yet), and re-resolve CurrentTheme after
/// editing a Theme asset's fields live in the Inspector. Every button is a thin call into
/// ThemeManager's own Debug* API (see that class) — this window has zero Theme logic of its own,
/// it's purely a UI over already-real entry points, so there is no separate "debug" code path that
/// could drift out of sync with what real gameplay actually does.
///
/// Play-Mode only: ThemeManager.Instance is null outside Play Mode (it's created by AppBootstrap's
/// RuntimeInitializeOnLoadMethod hook, which only runs once the game actually starts running).
/// </summary>
public class ThemeDebugWindow : EditorWindow
{
    private MusicStyleId _selectedStyle;
    private int _selectedFrontendIndex;
    private int _selectedEventIndex;

    [MenuItem("Tools/MusicGame/Theme Debugger")]
    private static void Open() => GetWindow<ThemeDebugWindow>("Theme Debugger");

    private void OnGUI()
    {
        var manager = ThemeManager.Instance;
        if (manager == null)
        {
            EditorGUILayout.HelpBox("ThemeManager.Instance is null — enter Play Mode first.", MessageType.Info);
            return;
        }

        DrawCurrentThemeSummary(manager);
        EditorGUILayout.Space();
        DrawMusicStyleSection(manager);
        EditorGUILayout.Space();
        DrawFrontendVisualSection(manager);
        EditorGUILayout.Space();
        DrawEventSection(manager);
        EditorGUILayout.Space();

        if (GUILayout.Button("Reload Theme (re-resolve current layers)"))
            manager.DebugReloadTheme();
    }

    private void DrawCurrentThemeSummary(ThemeManager manager)
    {
        EditorGUILayout.LabelField("Current Theme", EditorStyles.boldLabel);
        var theme = manager.CurrentTheme;

        EditorGUILayout.LabelField("UI", theme?.UI != null ? theme.UI.name : "(null)");
        // The actual point of this window: an undeniable visual confirmation that forcing a
        // different Music Style really did swap something, not just a different asset NAME in a
        // label — these four swatches redraw the instant Force is pressed (OnInspectorUpdate keeps
        // this window repainting).
        if (theme?.UI != null) DrawUIColorSwatches(theme.UI);

        EditorGUILayout.LabelField("World",        theme?.World        != null ? theme.World.name        : "(null)");
        EditorGUILayout.LabelField("Track",        theme?.Track        != null ? theme.Track.name        : "(null)");
        EditorGUILayout.LabelField("Player",       theme?.Player       != null ? theme.Player.name       : "(null)");
        EditorGUILayout.LabelField("Collectibles", theme?.Collectibles != null ? theme.Collectibles.name : "(null)");
        EditorGUILayout.LabelField("VFX",          theme?.VFX          != null ? theme.VFX.name          : "(null)");
    }

    private void DrawUIColorSwatches(UIStyleSO ui)
    {
        EditorGUILayout.BeginHorizontal();
        DrawColorSwatch("Primary",    ui.primaryColor);
        DrawColorSwatch("Secondary",  ui.secondaryColor);
        DrawColorSwatch("Accent",     ui.accentColor);
        DrawColorSwatch("Background", ui.backgroundColor);
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawColorSwatch(string label, Color color)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(70));
        Rect rect = GUILayoutUtility.GetRect(64, 28);
        EditorGUI.DrawRect(rect, color);

        // A thin border so a pale/white swatch (e.g. BaseTheme's default primaryColor) doesn't
        // just blend into the window background and look "empty".
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.black);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Color.black);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), Color.black);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Color.black);

        EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawMusicStyleSection(ThemeManager manager)
    {
        EditorGUILayout.LabelField("Force Music Style", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _selectedStyle = (MusicStyleId)EditorGUILayout.EnumPopup(_selectedStyle);
        if (GUILayout.Button("Force", GUILayout.Width(80)))
            manager.DebugForceMusicStyle(_selectedStyle);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawFrontendVisualSection(ThemeManager manager)
    {
        EditorGUILayout.LabelField("Force Frontend Visual", EditorStyles.boldLabel);

        var presets = manager.Config != null && manager.Config.frontendVisualRegistry != null
            ? manager.Config.frontendVisualRegistry.presets
            : null;

        if (presets == null || presets.Length == 0)
        {
            EditorGUILayout.HelpBox("No entries in the Frontend Visual Registry.", MessageType.None);
            return;
        }

        string[] labels = new string[presets.Length];
        for (int i = 0; i < presets.Length; i++)
            labels[i] = LabelForGuid(presets[i] != null ? presets[i].AssetGUID : null, $"Preset {i}");

        _selectedFrontendIndex = Mathf.Clamp(_selectedFrontendIndex, 0, presets.Length - 1);

        EditorGUILayout.BeginHorizontal();
        _selectedFrontendIndex = EditorGUILayout.Popup(_selectedFrontendIndex, labels);
        if (GUILayout.Button("Force", GUILayout.Width(80)))
            manager.DebugForceFrontendVisual(presets[_selectedFrontendIndex]);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawEventSection(ThemeManager manager)
    {
        EditorGUILayout.LabelField("Force Event", EditorStyles.boldLabel);

        var entries = manager.Config != null && manager.Config.eventThemeRegistry != null
            ? manager.Config.eventThemeRegistry.entries
            : null;

        if (entries == null || entries.Length == 0)
        {
            EditorGUILayout.HelpBox("No entries in the Event Theme Registry.", MessageType.None);
            return;
        }

        string[] labels = new string[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            labels[i] = string.IsNullOrEmpty(entries[i].displayName) ? entries[i].eventId : entries[i].displayName;

        _selectedEventIndex = Mathf.Clamp(_selectedEventIndex, 0, entries.Length - 1);

        EditorGUILayout.BeginHorizontal();
        _selectedEventIndex = EditorGUILayout.Popup(_selectedEventIndex, labels);
        if (GUILayout.Button("Force", GUILayout.Width(80)))
            manager.DebugForceEvent(entries[_selectedEventIndex].theme);
        if (GUILayout.Button("Clear", GUILayout.Width(60)))
            manager.DebugClearEvent();
        EditorGUILayout.EndHorizontal();
    }

    private static string LabelForGuid(string guid, string fallback)
    {
        string path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? fallback : Path.GetFileNameWithoutExtension(path);
    }

    // Keeps the "Current Theme" summary live while Force buttons are pressed during Play Mode.
    private void OnInspectorUpdate() => Repaint();
}
#endif
