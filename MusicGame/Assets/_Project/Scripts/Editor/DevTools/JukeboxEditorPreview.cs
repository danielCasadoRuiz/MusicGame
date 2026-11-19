#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only, session-local store simulator. No player entitlements or real money.</summary>
[InitializeOnLoad]
public class JukeboxEditorPreview : EditorWindow
{
    private const string EnabledKey = "Jukebox.StoreSimulation.Enabled";
    private const string OwnedKey = "Jukebox.StoreSimulation.Owned.V2";
    private static readonly MusicProduct[] Products = { MusicProduct.Jukebox, MusicProduct.CustomAudio };
    private static int OwnedMask()
    {
        int current = SessionState.GetInt(OwnedKey, -1);
        if (current >= 0) return current;
        int old = SessionState.GetInt("Jukebox.StoreSimulation.Owned", 0);
        int migrated = ((old & ((1 << 0) | (1 << 2) | (1 << 4))) != 0 ? 1 << (int)MusicProduct.Jukebox : 0)
                     | ((old & ((1 << 3) | (1 << 4))) != 0 ? 1 << (int)MusicProduct.CustomAudio : 0);
        SessionState.SetInt(OwnedKey, migrated); return migrated;
    }
    private static readonly IMusicStore Simulator = new PreviewStore();
    private static readonly string[] Names = { "Jukebox Premium · inclou segon pla", "Custom Audio · importacions il·limitades" };
    private sealed class PreviewStore : IMusicStore
    {
        public bool Owns(MusicProduct p) => (OwnedMask() & (1 << (int)p)) != 0;
        public void Purchase(MusicProduct p)
        {
            SessionState.SetInt(OwnedKey, OwnedMask() | (1 << (int)p));
            MusicAccess.NotifyChanged();
        }
        public void Restore() => MusicAccess.NotifyChanged();
    }
    static JukeboxEditorPreview()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Apply();
            if (state == PlayModeStateChange.ExitingPlayMode && ReferenceEquals(MusicAccess.Store, Simulator)) MusicAccess.Store = null;
        };
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying) Apply();
            else LocalizationSetup.AddMissingEntries(Array.FindAll(LocalizationSetup.Entries, e => e.key.StartsWith("Jukebox.Purchase") || e.key.StartsWith("Jukebox.Buy") || e.key == "Jukebox.FullPremium").Select(e => e.key));
        };
    }
    [MenuItem("Tools/MusicGame/Jukebox/Test Purchases")]
    public static void Open()
    {
        SessionState.SetBool(EnabledKey, true); Apply();
        GetWindow<JukeboxEditorPreview>("Music Purchases").Show();
    }
    [MenuItem("Tools/MusicGame/Jukebox/Preview Premium Features (Editor only)")]
    private static void LegacyMenu() => Open();
    private static void Apply()
    {
        if (SessionState.GetBool(EnabledKey, false)) MusicAccess.Store = Simulator;
        else if (ReferenceEquals(MusicAccess.Store, Simulator)) MusicAccess.Store = null;
        MusicAccess.NotifyChanged();
    }
    private void OnInspectorUpdate() => Repaint();
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Compres de prova", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Només a l’editor. No es cobra res ni es desen compres reals. Pots canviar els permisos durant Play.", MessageType.Info);
        bool enabled = EditorGUILayout.Toggle("Activar simulació", SessionState.GetBool(EnabledKey, false));
        if (enabled != SessionState.GetBool(EnabledKey, false)) { SessionState.SetBool(EnabledKey, enabled); Apply(); }
        int mask = OwnedMask();
        EditorGUI.BeginChangeCheck();
        for (int i = 0; i < Names.Length; i++)
        {
            bool owned = EditorGUILayout.ToggleLeft(Names[i], (mask & (1 << (int)Products[i])) != 0);
            if (owned) mask |= 1 << (int)Products[i]; else mask &= ~(1 << (int)Products[i]);
        }
        if (EditorGUI.EndChangeCheck()) SetOwned(mask);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Cap compra")) SetOwned(0);
        if (GUILayout.Button("Només Jukebox")) SetOwned(1 << (int)MusicProduct.Jukebox);
        if (GUILayout.Button("Les dues compres")) SetOwned((1 << (int)MusicProduct.Jukebox) | (1 << (int)MusicProduct.CustomAudio));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Permisos efectius", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Cançons completes", MusicAccess.Jukebox ? "Sí" : "No");
        EditorGUILayout.LabelField("Segon pla", MusicAccess.Background ? "Sí" : "No");
        EditorGUILayout.LabelField("Importacions il·limitades", MusicAccess.CustomAudio ? "Sí" : "No");
        EditorGUILayout.HelpBox("Jukebox Premium inclou segon pla a PC i mòbil. Sense premium, perdre el focus pausa la música. Amb segon pla activat, continua mentre l’aplicació segueix oberta. Tancar el joc acaba la reproducció a PC.", MessageType.None);
    }
    private static void SetOwned(int mask) { SessionState.SetInt(OwnedKey, mask); Apply(); }
}
#endif
