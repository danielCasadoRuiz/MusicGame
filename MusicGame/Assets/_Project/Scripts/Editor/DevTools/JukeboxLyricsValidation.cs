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
            var view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<JukeboxView>(UIScreenPrefabContext.ScreensFolder + "/Jukebox.prefab")); screenRoot = view.gameObject;
            Check(view.lyricsPanel != null && view.libraryPanel != null && view.lyricsLine != null && view.lyricsScroll != null && view.lyricsLock != null, "Prefab contains lyrics panel, line template and lock", log);
            Check(!view.lyricsPanel.activeSelf && string.IsNullOrEmpty(view.lyricsLine.text), "Lyrics start hidden and empty", log);
            fixture = new GameObject("Lyrics isolated fixture"); fixture.SetActive(false);
            var controller = fixture.AddComponent<JukeboxController>();
            var player = fixture.AddComponent<JukeboxPlayer>();
            var lyrics = SongLyrics.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(SongContentKeys.LyricsFolder + "/song_017.json").text, "song_017");
            var track = new JukeboxTrack { id = "song_017", title = "All together (meh!)", artist = "The Backup Boys", lyricData = lyrics, lyrics = lyrics.PlainText };
            typeof(JukeboxPlayer).GetProperty("Current").SetValue(player, track);
            Set(controller, "_view", view); Set(controller, "_player", player); Set(controller, "_selected", track);
            var store = new Store(); MusicAccess.Store = store;
            Call(controller, "RefreshControls");
            Check(!view.lyricsPanel.activeSelf && view.libraryPanel.activeSelf, "Free users cannot view lyrics", log);
            store.premium = true; Call(controller, "RefreshControls");
            Check(view.lyricsPanel.activeSelf && view.libraryPanel.activeSelf, "Premium shows synced lyrics alongside the library", log);
            var setLyricAt = typeof(JukeboxController).GetMethod("SetLyricAt", BindingFlags.Instance | BindingFlags.NonPublic);
            setLyricAt.Invoke(controller, new object[] { lyrics.lines[0].start + lyrics.offset });
            Check(view.lyricsLine.text == lyrics.lines[0].text, "Only the current lyric phrase is shown", log);
            setLyricAt.Invoke(controller, new object[] { lyrics.lines[1].start + lyrics.offset });
            Check(view.lyricsLine.text == lyrics.lines[1].text, "The next phrase replaces the preceding phrase at its cue", log);
            setLyricAt.Invoke(controller, new object[] { -1f });
            Check(view.lyricsLine.text == "", "Lyrics clear when no phrase is timed", log);
            store.premium = false; Call(controller, "RefreshControls");
            Check(!view.lyricsPanel.activeSelf, "Revoking Premium immediately hides lyrics", log);
            store.premium = true;
            var imported = new JukeboxTrack { id = "import_test", path = "test.wav", title = "Voice note" };
            Set(controller, "_selected", imported); typeof(JukeboxPlayer).GetProperty("Current").SetValue(player, imported);
            Call(controller, "RefreshControls");
            Check(!view.lyrics.gameObject.activeSelf && !view.lyricsPanel.activeSelf, "Imported audio has no lyrics button or pane", log);
            Set(controller, "_selected", track); typeof(JukeboxPlayer).GetProperty("Current").SetValue(player, track); Call(controller, "RefreshControls");
            Check(!view.background.gameObject.activeSelf && !view.lyrics.gameObject.activeSelf, "Dedicated background and lyrics buttons are hidden", log);
            view.ConfigurePlaybackHeader();
            Capture(view, log);
            Check(!view.preview.gameObject.activeSelf && !view.full.gameObject.activeSelf,
                "Playback starts with one transport button and no Premium card", log);
            store.premium = false;
            Set(controller, "_opened", true);
            typeof(JukeboxPlayer).GetProperty("Current").SetValue(player, null);
            typeof(JukeboxController).GetMethod("OnPreviewFinished", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { track });
            Check(view.full.gameObject.activeSelf && !view.selected.gameObject.activeSelf &&
                  !view.nowPlaying.gameObject.activeSelf && !view.cover.gameObject.activeSelf &&
                  !view.cabinetContent.Find("SleeveBezel").gameObject.activeSelf && !view.lyricsPanel.activeSelf &&
                  ((RectTransform)view.full.transform).rect.height > 300 && view.playPause.gameObject.activeSelf,
                "A completed preview replaces the track display with a Premium card and keeps the transport", log);
            Set(controller, "_selected", imported);
            Call(controller, "RefreshControls");
            Check(!view.full.gameObject.activeSelf && view.selected.gameObject.activeSelf,
                "Selecting another track restores the playback header", log);
            Set(controller, "_selected", track);
            store.premium = true;
            Call(controller, "RefreshControls");
            Check(!view.full.gameObject.activeSelf && view.nowPlaying.gameObject.activeSelf,
                "Premium users never see the preview offer", log);
            Set(controller, "_opened", false);
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
            view.selected.text = "All together (meh!)";
            view.detail.text = "The Backup Boys · Dance";
            view.nowPlaying.text = "Sonant ara";
            view.summary.text = "3 / 22";
            view.cover.sprite = view.defaultCover;
            view.cover.gameObject.SetActive(true);
            view.delete.gameObject.SetActive(false);
            view.transportIcon.sprite = view.pauseIcon;
            var transport = new[] { view.previous.image, view.playPause.image, view.next.image };
            Check(transport.All(image => image.type == Image.Type.Simple && image.sprite != null) &&
                  transport.Select(image => image.sprite).Distinct().Count() == 3,
                "Transport buttons use three dedicated seamless full-face sprites", log);
            // This fixture has three playable songs. Its controls should reflect that state.
            view.previous.interactable = view.playPause.interactable = view.next.interactable = true;
            view.FitCabinet();
            view.ApplyTitleCurve();
            var screen = ((RectTransform)view.transform).rect.size;
            var scaledCabinet = Vector2.Scale(view.cabinetContent.rect.size, view.cabinetContent.localScale);
            Check(Vector2.Distance(scaledCabinet, screen) < .5f, "The cabinet fills the 16:9 viewport", log);
            view.SetProgress(48, 246);
            Check(view.tempoFury != null && view.tempoFury.letters.Length == 9,
                "Cabinet uses the game's separate Tempo Fury logo letters", log);
            view.ApplyBrand(GameMusicStyle.Classical);
            Check(view.tempoFury.Shown == GameMusicStyle.Classical, "Cabinet logo can use the classical skin", log);
            view.ApplyBrand(GameMusicStyle.Electronic);
            var electronicLetter = view.tempoFury.letters[0].sprite;
            view.ApplyBrand(GameMusicStyle.Rock);
            Check(view.tempoFury.Shown == GameMusicStyle.Rock && view.tempoFury.letters[0].sprite != electronicLetter,
                "Changing game style swaps the original logo letter sprites", log);
            var letters = view.tempoFury.letters;
            Check(letters[5].sprite == view.rockFuryLetter, "Rock Fury uses the red F sprite", log);
            Check(letters.Max(letter => letter.rectTransform.anchoredPosition.y) -
                  letters.Min(letter => letter.rectTransform.anchoredPosition.y) < 10f &&
                  letters.Zip(letters.Skip(1), (left, right) =>
                      left.rectTransform.anchoredPosition.x < right.rectTransform.anchoredPosition.x).All(value => value),
                "Tempo Fury follows a shallow continuous arch", log);
            Check(view.GetComponentsInChildren<JukeboxNeon>().Length == 4, "Cabinet retains four animated light paths", log);
            Check(view.medallions.Length == 4 && view.medallions.All(image => image.GetComponentInParent<Mask>() != null),
                "The four medallions use separate masked album artwork", log);
            view.cover.sprite = view.styleSleeves[7];
            var previewRows = new JukeboxRowView[JukeboxCarousel.SlotCount];
            for (int i = 0; i < previewRows.Length; i++)
                previewRows[i] = Object.Instantiate(AssetDatabase.LoadAssetAtPath<JukeboxRowView>(
                    UIScreenPrefabContext.ItemsFolder + "/JukeboxRow.prefab"), view.list);
            var exampleTracks = new[]
            {
                new JukeboxTrack { title = "Zero Nine" },
                new JukeboxTrack { title = "All together (meh!)" },
                new JukeboxTrack { title = "Milonga" }
            };
            int middle = previewRows.Length / 2;
            view.carousel.Setup(previewRows, exampleTracks.Take(2).ToArray(), null, null);
            view.carousel.Focus(1);
            Check(previewRows.Count(row => row.button.interactable) == 2 &&
                  previewRows.Count(row => row.background != null && ((RectTransform)row.transform).localScale.x > 1) == 1,
                "Two songs yield two selectable plates with only one enlarged center", log);
            int selectedIndex = -1;
            view.carousel.Setup(previewRows, exampleTracks, index => selectedIndex = index, null);
            var moveReel = typeof(JukeboxCarousel).GetMethod("MoveTo", BindingFlags.Instance | BindingFlags.NonPublic);
            moveReel.Invoke(view.carousel, new object[] { 0.6f });
            Check(selectedIndex == 1 && previewRows[middle].title.text == exampleTracks[1].title &&
                  previewRows.Count(row => row.button.interactable) == exampleTracks.Length &&
                  previewRows.Where(row => row.button.interactable).Select(row => row.title.text).Distinct().Count() == exampleTracks.Length,
                "Moving a short reel selects the center track without duplicate plates", log);
            var longReel = Enumerable.Range(0, JukeboxCarousel.SlotCount).Select(i => new JukeboxTrack { title = i.ToString() }).ToArray();
            view.carousel.Setup(previewRows, longReel, null, null);
            Check(previewRows[middle - 1].title.text == longReel.Last().title &&
                  previewRows[middle + 1].title.text == longReel[1].title,
                "A full reel wraps in the original order", log);
            view.carousel.Setup(previewRows, exampleTracks, null, null);
            view.carousel.Focus(1);
            Check(previewRows[middle].title.text == exampleTracks[1].title &&
                  ((RectTransform)previewRows[middle].transform).anchoredPosition == Vector2.zero &&
                  previewRows[middle].plate.sprite == previewRows[middle].selectedArt,
                "The selected track is centered and rendered on the red plate", log);
            var reelViewport = view.libraryPanel.GetComponent<ScrollRect>().viewport;
            Check(reelViewport.GetComponent<Mask>()?.showMaskGraphic == false &&
                  Mathf.Approximately(((RectTransform)previewRows[middle].transform).rect.width, reelViewport.rect.width),
                "Full-width plates are clipped by the cabinet aperture", log);
            Check(previewRows[middle].rowLight.gameObject.activeSelf && previewRows.Count(row => row.rowLight.gameObject.activeSelf) == 1,
                "Only the selected plate has a soft live LED", log);
            view.lyricsLine.text = "All together, here";
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
