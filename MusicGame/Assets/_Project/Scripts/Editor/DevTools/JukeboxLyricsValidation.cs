#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Isolated lyrics data, prefab and entitlement checks; never loads or saves a player profile.</summary>
public static class JukeboxLyricsValidation
{
    private sealed class Store : IMusicStore
    {
        public bool premium;
        public bool Owns(MusicProduct p) => premium && p == MusicProduct.Jukebox;
        public void Purchase(MusicProduct p) { }
        public void Restore() { }
    }
    private static void Check(bool condition, string description, StringBuilder log)
    {
        if (!condition) throw new Exception(description);
        log.AppendLine("PASS " + description);
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    [MenuItem("Tools/MusicGame/Jukebox/Verify Synced Lyrics")]
    public static void Run()
    {
        var log = new StringBuilder(); bool success = false;
        var previousStore = MusicAccess.Store;
        GameObject fixture = null, screenRoot = null;
        try
        {
            var lib = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>(UIScreenPrefabContext.LibraryPath);
            JukeboxSetup.Build(new UIScreenPrefabContext(lib, false, log));
            EditorUtility.SetDirty(lib); AssetDatabase.SaveAssets();
            const string sample = "{\"schemaVersion\":1,\"songId\":\"song_test\",\"offset\":0.5,\"lines\":[{\"start\":1,\"end\":3,\"text\":\"first\"},{\"start\":-1,\"end\":-1,\"text\":\"untimed\"},{\"start\":4,\"end\":5,\"text\":\"second\"}]}";
            var data = SongLyrics.Parse(sample, "song_test");
            Check(data.LineAt(1.49f) == -1 && data.LineAt(1.5f) == 0, "Line onset uses song time and offset", log);
            Check(data.LineAt(3.5f) == -1 && data.LineAt(4.5f) == 2 && data.LineAt(5.5f) == -1, "End boundaries, instrumental gaps and untimed lines", log);
            Check(data.LineAt(float.NaN) == -1 && data.LineAt(float.PositiveInfinity) == -1, "Invalid player positions are ignored", log);
            Check(data.PlainText == "first\nuntimed\nsecond".Replace("\n", Environment.NewLine), "Authored untimed words remain available as text", log);
            bool rejects = false; try { SongLyrics.Parse(sample, "song_other"); } catch (FormatException) { rejects = true; }
            Check(rejects, "Wrong song identifiers rejected", log);
            rejects = false; try { SongLyrics.Parse(sample.Replace("\"start\":4", "\"start\":2"), "song_test"); } catch (FormatException) { rejects = true; }
            Check(rejects, "Overlapping cues rejected", log);
            SongLyrics importedLyrics = new SongLyrics();
            SongLyrics.Load("import_test", value => importedLyrics = value);
            Check(importedLyrics == null, "Imported songs never acquire original-song lyrics", log);
            var assets = Directory.GetFiles(SongContentKeys.LyricsFolder, "*.json").Select(AssetDatabase.LoadAssetAtPath<TextAsset>).ToArray();
            var catalog = Resources.Load<AppConfigSO>("AppConfig").songCatalog;
            Check(catalog.songs.Select(song => song.songId).ToHashSet().SetEquals(assets.Select(asset => asset.name)),
                "Every song in the playable catalog has lyrics", log);
            TpsContentProtection.Setup();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            foreach (var asset in assets)
            {
                var doc = SongLyrics.Parse(asset.text, asset.name);
                Check(doc.lines.Length > 0 && doc.lines.All(l => l.Timed) && doc.textSource != "whisper_draft", asset.name + " author text and automatic timings loaded", log);
                Check(doc.audioSha256?.Length == 64, asset.name + " source audio fingerprint retained", log);
                var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));
                var schema = entry?.parentGroup.GetSchema<BundledAssetGroupSchema>();
                Check(entry != null && entry.address == SongContentKeys.LyricsAddress(asset.name) &&
                      entry.labels.Contains(SongContentKeys.ContentLabel(asset.name)) &&
                      schema?.AssetBundleProviderType.Value == typeof(TpsBundleProvider),
                    asset.name + " registered with its song and the encrypted bundle provider", log);
            }
            var view = Object.Instantiate(lib.jukebox); screenRoot = view.gameObject;
            Check(view.lyricsPanel != null && view.libraryPanel != null && view.lyricsLine != null && view.lyricsScroll != null && view.lyricsLock != null, "Prefab contains lyrics panel, line template and lock", log);
            Check(!view.lyricsPanel.activeSelf && !view.lyricsLine.gameObject.activeSelf, "Lyrics start hidden", log);
            fixture = new GameObject("Lyrics isolated fixture"); fixture.SetActive(false);
            var controller = fixture.AddComponent<JukeboxController>();
            var player = fixture.AddComponent<JukeboxPlayer>();
            var lyrics = SongLyrics.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(SongContentKeys.LyricsFolder + "/song_017.json").text, "song_017");
            var track = new JukeboxTrack { id = "song_017", title = "All together (meh!)", artist = "The Backup Boys", lyricData = lyrics, lyrics = lyrics.PlainText };
            typeof(JukeboxPlayer).GetProperty("Current").SetValue(player, track);
            Set(controller, "_view", view); Set(controller, "_player", player); Set(controller, "_selected", track); Set(controller, "_lyricsWanted", true);
            var store = new Store(); MusicAccess.Store = store;
            Call(controller, "RefreshControls");
            Check(!view.lyricsPanel.activeSelf && view.libraryPanel.activeSelf && view.lyricsLock.activeSelf, "Free users see the lock and cannot view lyrics", log);
            store.premium = true; Call(controller, "RefreshControls");
            Check(view.lyricsPanel.activeSelf && !view.libraryPanel.activeSelf && !view.lyricsLock.activeSelf, "Premium swaps the library for synced lyrics when requested", log);
            Check(view.lyricsContent.GetComponentsInChildren<TextMeshProUGUI>().Length == lyrics.lines.Length, "One readable label per authored lyric line", log);
            Set(controller, "_lyricsWanted", false); Call(controller, "RefreshControls");
            Check(!view.lyricsPanel.activeSelf && view.libraryPanel.activeSelf, "Lyrics can be hidden without changing playback", log);
            Set(controller, "_lyricsWanted", true); store.premium = false; Call(controller, "RefreshControls");
            Check(!view.lyricsPanel.activeSelf, "Revoking Premium immediately hides lyrics", log);
            store.premium = true;
            var imported = new JukeboxTrack { id = "import_test", path = "test.wav", title = "Voice note" };
            Set(controller, "_selected", imported); typeof(JukeboxPlayer).GetProperty("Current").SetValue(player, imported);
            Call(controller, "RefreshControls");
            Check(!view.lyrics.gameObject.activeSelf && !view.lyricsPanel.activeSelf, "Imported audio has no lyrics button or pane", log);
            Set(controller, "_selected", track); typeof(JukeboxPlayer).GetProperty("Current").SetValue(player, track); Call(controller, "RefreshControls");
            Capture(view, log);
            success = true;
        }
        catch (Exception e) { log.AppendLine("FAIL " + e); }
        finally
        {
            MusicAccess.Store = previousStore;
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (screenRoot != null) Object.DestroyImmediate(screenRoot);
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/JukeboxLyricsValidation.txt", log.ToString());
        }
        if (success) Debug.Log(log); else Debug.LogError(log);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }
    private static void Capture(JukeboxView view, StringBuilder log)
    {
        var root = new GameObject("Lyrics QA canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var camRoot = new GameObject("Lyrics QA camera", typeof(Camera));
        var camera = camRoot.GetComponent<Camera>(); camera.orthographic = true;
        var target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
        view.transform.SetParent(root.transform, false); UIFactory.Stretch((RectTransform)view.transform);
        view.dialog.SetActive(false); view.gameObject.SetActive(true);
        try
        {
            foreach (var localized in root.GetComponentsInChildren<LocalizedText>(true))
            {
                var entry = LocalizationSetup.Entries.FirstOrDefault(e => e.key == localized.key);
                if (entry.key != null) localized.GetComponent<TMP_Text>().text = entry.ca;
            }
            view.selected.text = "All together (meh!)\nThe Backup Boys · Dance · Original";
            view.nowPlaying.text = "Sona: All together (meh!)"; view.summary.text = "LLETRES";
            view.cover.gameObject.SetActive(false); view.delete.gameObject.SetActive(false);
            var line = view.lyricsContent.GetComponentsInChildren<TextMeshProUGUI>().First();
            line.color = new Color(.4f, 1f, .88f); line.fontStyle = FontStyles.Bold;
            Canvas.ForceUpdateCanvases(); foreach (var t in root.GetComponentsInChildren<TMP_Text>()) t.ForceMeshUpdate();
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            Directory.CreateDirectory("../VisualReviews/Jukebox"); File.WriteAllBytes("../VisualReviews/Jukebox/lyrics-premium.png", image.EncodeToPNG());
            Object.DestroyImmediate(image); log.AppendLine("PASS premium lyrics visual captured");
        }
        finally
        {
            RenderTexture.active = null; camera.targetTexture = null;
            view.transform.SetParent(null, false); Object.DestroyImmediate(root); Object.DestroyImmediate(camRoot); Object.DestroyImmediate(target);
        }
    }
}
#endif
