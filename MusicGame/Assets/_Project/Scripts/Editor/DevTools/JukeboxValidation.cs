#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class JukeboxValidation
{
    private sealed class TestStore : IMusicStore
    {
        public MusicProduct product;
        public bool Owns(MusicProduct p) => p == product;
        public void Purchase(MusicProduct p) { }
        public void Restore() { }
    }
    private static void Check(bool condition, string description, StringBuilder log)
    {
        if (!condition) throw new Exception(description);
        log.AppendLine("PASS " + description);
    }
    [MenuItem("Tools/MusicGame/Jukebox/Verify Purchase Configuration")]
    public static void VerifyTwoPurchases()
    {
        var log = new StringBuilder();
        const string legacy = "Jukebox.StoreSimulation.Owned", current = "Jukebox.StoreSimulation.Owned.V2";
        int previousLegacy = SessionState.GetInt(legacy, int.MinValue), previousCurrent = SessionState.GetInt(current, int.MinValue);
        var previousStore = MusicAccess.Store;
        bool success = false;
        try
        {
            LocalizationSetup.AddMissingEntries(LocalizationSetup.Entries.Where(e => e.key.StartsWith("Jukebox.Purchase")).Select(e => e.key));
            Check(Enum.GetValues(typeof(MusicProduct)).Length == 2, "Exactly two purchasable products", log);
            var method = typeof(JukeboxEditorPreview).GetMethod("OwnedMask", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var pair in new (int oldMask, int expected)[] { (0,0), (1,1), (2,0), (4,1), (8,8), (16,9), (10,8), (6,1), (31,9) })
            {
                SessionState.SetInt(legacy, pair.oldMask); SessionState.EraseInt(current);
                int actual = (int)method.Invoke(null, null);
                Check(actual == pair.expected, "Editor simulation migration " + pair.oldMask + " -> " + pair.expected, log);
            }
            MusicAccess.Store = null;
            Check(!MusicAccess.Jukebox && !MusicAccess.Background && !MusicAccess.CustomAudio, "Free mode grants no paid access", log);
            var store = new TestStore { product = MusicProduct.Jukebox }; MusicAccess.Store = store;
            Check(MusicAccess.Jukebox && MusicAccess.Background && !MusicAccess.CustomAudio, "Jukebox Premium includes background only", log);
            store.product = MusicProduct.CustomAudio;
            Check(!MusicAccess.Jukebox && !MusicAccess.Background && MusicAccess.CustomAudio, "Custom Audio remains independent", log);
            success = true;
        }
        catch (Exception e) { log.AppendLine("FAIL " + e); }
        finally
        {
            if (previousLegacy == int.MinValue) SessionState.EraseInt(legacy); else SessionState.SetInt(legacy, previousLegacy);
            if (previousCurrent == int.MinValue) SessionState.EraseInt(current); else SessionState.SetInt(current, previousCurrent);
            MusicAccess.Store = previousStore;
        }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/JukeboxTwoPurchases.txt", log.ToString());
        if (success) Debug.Log(log); else Debug.LogError(log);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }
    [MenuItem("Tools/MusicGame/Jukebox/Verify Purchase Configuration", true)]
    private static bool CanVerifyPurchases() => !EditorApplication.isPlayingOrWillChangePlaymode;

    public static void InstallAndVerify()
    {
        var log = new StringBuilder();
        try
        {
            var lib = AssetDatabase.LoadAssetAtPath<UIPrefabLibrarySO>(UIScreenPrefabContext.LibraryPath);
            JukeboxSetup.Build(new UIScreenPrefabContext(lib, true, log));
            EditorUtility.SetDirty(lib); AssetDatabase.SaveAssets();
            Verify(log);
            Capture(lib, log);
            // Native audio is unchanged by UI validation.
            File.WriteAllText("Logs/JukeboxValidation.txt", log.ToString());
            Debug.Log(log); EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            log.AppendLine("FAIL " + e); Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/JukeboxValidation.txt", log.ToString());
            Debug.LogError(log); EditorApplication.Exit(1);
        }
    }
    private static void Verify(StringBuilder log)
    {
        Directory.CreateDirectory("Logs");
        MusicAccess.Store = null;
        Check(!MusicAccess.Jukebox && !MusicAccess.Background && !MusicAccess.CustomAudio, "No paid access is granted by the placeholder", log);
        var store = new TestStore(); MusicAccess.Store = store;
        store.product = MusicProduct.CustomAudio;
        Check(MusicAccess.CustomAudio && !MusicAccess.Jukebox && !MusicAccess.Background, "Custom Audio does not grant Jukebox or background", log);
        store.product = MusicProduct.Jukebox;
        Check(MusicAccess.Jukebox && MusicAccess.Background && !MusicAccess.CustomAudio, "Jukebox Premium includes background without granting imports", log);
        MusicAccess.Store = null;

        var old = new PlayerProgressData { version = 9, completedSongIds = new List<string> { "song_003", "song_001", "song_003" } };
        PlayerProgressStore.Migrate(old);
        Check(old.jukeboxUnlockedIds.SequenceEqual(new[] { "song_003", "song_001" }), "Old wins migrate in first-victory order with no duplicates", log);
        var fixture = new GameObject("Jukebox test profile"); fixture.SetActive(false);
        var progress = fixture.AddComponent<PlayerProgressService>();
        typeof(PlayerProgressService).GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(progress, old);
        progress.UnlockJukebox("song_001", false); progress.UnlockJukebox("import_test", false); progress.UnlockJukebox("song_003", false); progress.UnlockJukebox("song_004", false);
        Check(old.jukeboxUnlockedIds.SequenceEqual(new[] { "song_003", "song_001", "import_test", "song_004" }), "Later-lap replays keep order and imports share the same chronology", log);
        Check(!new PlayerProgressData().jukeboxUnlockedIds.Any(), "A fresh progress profile has no musical unlocks", log);
        Object.DestroyImmediate(fixture);

        string testRoot = Path.GetFullPath("Temp/JukeboxValidation_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        string source = Path.Combine(testRoot, "Voice note.wav");
        File.WriteAllBytes(source, new byte[] { 82, 73, 70, 70, 1, 2, 3, 4 }); // storage tests, decoding is tested separately in Play mode
        try
        {
            ImportedAudioLibrary.TestFolder = Path.Combine(testRoot, "library"); ImportedAudioLibrary.ReloadForTests();
            Check(ImportedAudioLibrary.CanImport, "A clean profile offers one import trial", log);
            var record = ImportedAudioLibrary.Import(source, "Voice note");
            Check(record != null && record.title == "Voice note" && File.Exists(ImportedAudioLibrary.PathFor(record)), "Import keeps a durable app-owned copy and readable title", log);
            Check(!ImportedAudioLibrary.CanImport && File.Exists(source), "Successful import consumes trial and preserves original", log);
            var repeat = ImportedAudioLibrary.Import(source, "Voice note");
            Check(repeat.id == record.id && ImportedAudioLibrary.Tracks.Count == 1, "Identical bytes do not duplicate an import", log);
            ImportedAudioLibrary.ReloadForTests();
            Check(ImportedAudioLibrary.Tracks.Count == 1 && !ImportedAudioLibrary.CanImport, "Library and consumed trial survive reload", log);
            ImportedAudioLibrary.Delete(record.id);
            Check(File.Exists(source) && !File.Exists(ImportedAudioLibrary.PathFor(record)) && ImportedAudioLibrary.Tracks.Count == 0, "Deletion removes only the game copy, preserving the original", log);
            ImportedAudioLibrary.ReloadForTests();
            Check(!ImportedAudioLibrary.CanImport && ImportedAudioLibrary.Import(source, "Again") == null, "Deleting the free import does not renew the trial", log);
            MusicAccess.Store = new TestStore { product = MusicProduct.CustomAudio };
            Check(ImportedAudioLibrary.Import(source, "Again") != null, "Custom Audio permits further imports after the trial", log);
        }
        finally
        {
            ImportedAudioLibrary.TestFolder = null; ImportedAudioLibrary.ReloadForTests(); MusicAccess.Store = null;
            // Only files created under this explicit temporary test root.
            if (testRoot.StartsWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(testRoot, true);
        }
        var metadata = Resources.Load<JukeboxMetadataSO>("JukeboxMetadata");
        Check(metadata != null && metadata.songs.Count > 0, "Own-song metadata is editable and keyed by stable IDs", log);
        var view = AssetDatabase.LoadAssetAtPath<JukeboxView>(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab");
        Check(view != null && view.nowPlaying != null && view.dialogBody != null && view.list != null, "Screen references and scrollable lyrics dialog are wired", log);
        var menu = AssetDatabase.LoadAssetAtPath<MainMenuView>(UIScreenPrefabContext.ScreensFolder + "/MainMenu.prefab");
        Check(menu.jukeboxButton != null && menu.buttonSlots.Length == 6, "Main menu includes Jukebox with six non-overlapping slots", log);
    }
    private static void Capture(UIPrefabLibrarySO lib, StringBuilder log)
    {
        const int width = 1920, height = 1080;
        var cameraGO = new GameObject("Jukebox QA camera", typeof(Camera)); var camera = cameraGO.GetComponent<Camera>();
        camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var render = new RenderTexture(width, height, 24); camera.targetTexture = render;
        var canvasGO = new GameObject("Jukebox QA canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGO.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        var scaler = canvasGO.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(width, height);
        var screen = Object.Instantiate(lib.jukebox, canvasGO.transform); screen.gameObject.SetActive(true); UIFactory.Stretch((RectTransform)screen.transform);
        try
        {
            var index = AssetDatabase.LoadAssetAtPath<SongCatalogIndexSO>(SongContentKeys.IndexAssetPath);
            screen.summary.text = "8 cançons a la teva col·lecció";
            screen.selected.text = "Zero Nine\nAll But Water · Dance · Original";
            screen.nowPlaying.text = "Sona: Zero Nine";
            screen.status.text = "Guanya un combat per desbloquejar la cançó. Repetir-la no en canvia l’ordre.";
            screen.playLabel.text = "PAUSA"; screen.backgroundLabel.text = "SEGON PLA";
            screen.cover.gameObject.SetActive(false); screen.delete.gameObject.SetActive(false); screen.lyrics.gameObject.SetActive(false);
            int n = 0;
            foreach (var e in index.songs.Take(8))
            {
                var row = Object.Instantiate(lib.jukeboxRow, screen.list);
                JukeboxTrack.SplitName(e.displayName, out var artist, out var title);
                row.title.text = title; row.detail.text = artist + " · " + e.gameStyle + " · Original";
                row.state.text = n++ < 8 ? "DESBLOQUEJADA" : "BLOQUEJADA · GUANYA PER DESCOBRIR-LA";
                row.cover.gameObject.SetActive(false);
            }
            Directory.CreateDirectory("../VisualReviews/Jukebox");
            SaveImage("../VisualReviews/Jukebox/jukebox.png", camera, render, width, height);
            screen.gameObject.SetActive(false);
            var menu = Object.Instantiate(lib.mainMenu, canvasGO.transform); menu.gameObject.SetActive(true); menu.root.SetActive(true); menu.settingsPanel.SetActive(false);
            menu.playButtonLabel.text = "CONTINUA"; menu.profileText.gameObject.SetActive(false);
            if (menu.logo != null) menu.logo.Show(GameMusicStyle.Rock);
            if (menu.buttonStyle != null) menu.buttonStyle.Apply(GameMusicStyle.Rock, 1);
            SaveImage("../VisualReviews/Jukebox/main-menu.png", camera, render, width, height);
            Object.DestroyImmediate(menu.gameObject);
            log.AppendLine("PASS visual captures saved to VisualReviews/Jukebox");
        }
        finally
        {
            RenderTexture.active = null; camera.targetTexture = null;
            Object.DestroyImmediate(screen.gameObject); Object.DestroyImmediate(canvasGO); Object.DestroyImmediate(cameraGO); Object.DestroyImmediate(render);
        }
    }
    private static void SaveImage(string path, Camera camera, RenderTexture render, int width, int height)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) text.ForceMeshUpdate();
        foreach (var localized in Object.FindObjectsByType<LocalizedText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var entry = LocalizationSetup.Entries.FirstOrDefault(e => e.key == localized.key);
            if (entry.key != null) localized.GetComponent<TMP_Text>().text = entry.ca;
        }
        Canvas.ForceUpdateCanvases();
        camera.Render(); RenderTexture.active = render;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); Object.DestroyImmediate(image);
        RenderTexture.active = null;
    }
}
#endif
