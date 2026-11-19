#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class JukeboxPlayCheck
{
    private const string Active = "JukeboxPlayCheck.Active";
    private static bool started;
    private static double deadline;
    static JukeboxPlayCheck() { EditorApplication.update += Tick; }
    public static void Run()
    {
        SessionState.SetBool(Active, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void GuardRealSave()
    {
        if (SessionState.GetBool(Active, false))
        {
            PlayerProgressStore.SuspendWrites = true;
            ImportedAudioLibrary.TestFolder = Path.GetFullPath("Temp/JukeboxPlayCheckStartup");
            ImportedAudioLibrary.ReloadForTests();
        }
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (!started)
        {
            started = true; deadline = EditorApplication.timeSinceStartup + 160;
            PlayerProgressStore.SuspendWrites = true;
            var go = new GameObject("Jukebox integration checks"); Object.DontDestroyOnLoad(go); go.AddComponent<JukeboxPlayCheckRunner>();
        }
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup > deadline) Finish(false, "Timed out waiting for Jukebox integration checks");
    }
    public static void Finish(bool success, string report)
    {
        SessionState.SetBool(Active, false);
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/JukeboxPlayCheck.txt", (success ? "PASS\n" : "FAIL\n") + report);
        if (success) Debug.Log(report); else Debug.LogError(report);
        // Keep writes suspended during shutdown; no test profile can reach the real save.
        EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public class JukeboxPlayCheckRunner : MonoBehaviour
{
    private class Picker : ILocalSongPicker { public string path; public string PickFile() => path; }
    private class Premium : IMusicStore
    {
        public bool Owns(MusicProduct p) => p == MusicProduct.Jukebox || p == MusicProduct.CustomAudio;
        public void Purchase(MusicProduct p) { }
        public void Restore() { }
    }
    private readonly List<string> checks = new();
    private string folder;
    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message); checks.Add(message);
    }
    private IEnumerator Start()
    {
        var routine = Run();
        while (true)
        {
            object next;
            try { if (!routine.MoveNext()) break; next = routine.Current; }
            catch (Exception e) { Cleanup(); JukeboxPlayCheck.Finish(false, string.Join("\n", checks) + "\n" + e); yield break; }
            yield return next;
        }
        Cleanup(); JukeboxPlayCheck.Finish(true, string.Join("\n", checks));
    }
    private IEnumerator Run()
    {
        while (AppBootstrap.Context == null || JukeboxController.Instance == null || GameSession.Instance == null) yield return null;
        AppBootstrap.Context.AppFlow.RequestState(GameFlowState.MainMenu);
        yield return new WaitForSecondsRealtime(2);
        AudioListener.volume = 0;
        var profile = new PlayerProgressData();
        typeof(PlayerProgressService).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(PlayerProgressService.Instance, profile);
        folder = Path.GetFullPath("Temp/JukeboxPlayCheck_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        ImportedAudioLibrary.TestFolder = Path.Combine(folder, "library"); ImportedAudioLibrary.ReloadForTests(); MusicAccess.Store = null;
        string first = Path.Combine(folder, "A voice recording.wav"), second = Path.Combine(folder, "Another recording.wav");
        MakeWave(first, 220); MakeWave(second, 330);
        SelectedSongInfo? info = null;
        yield return new LocalFileSongSource(new Picker { path = null }).Load(result => info = result);
        Check(!info.HasValue && ImportedAudioLibrary.CanImport, "Cancelling a real import does not consume the trial");
        yield return new LocalFileSongSource(new Picker { path = first }).Load(result => info = result);
        Check(info.HasValue && info.Value.Clip != null && info.Value.Clip.length > 3.9f, "Local source decodes a real WAV and keeps a durable import");
        Check(info.Value.DisplayName == "A voice recording", "Imported display name stays readable instead of becoming its content hash");
        string id = ImportedAudioLibrary.FindByPath(info.Value.LocalFilePath).id;
        Check(!PlayerProgressService.Instance.IsJukeboxUnlocked(id), "Importing alone never unlocks listening");
        Check(!JukeboxTrack.Build().Any(t => t.id == id), "An imported track is absent from Jukebox before victory");
        SelectedSongInfo? retry = null;
        yield return new LocalFileSongSource(new Picker { path = first }).Load(result => retry = result);
        Check(retry.HasValue, "The same trial audio can be selected again without another purchase");
        if (retry.HasValue && retry.Value.Clip != null) Object.Destroy(retry.Value.Clip);
        GameSession.Instance.SelectedSong = info;
        AppBootstrap.Context.AppFlow.RequestState(GameFlowState.SongSelection);
        AppBootstrap.Context.AppFlow.RequestState(GameFlowState.MainMenu);
        yield return null;
        Check(ImportedAudioLibrary.Find(id) == null && !GameSession.Instance.SelectedSong.HasValue && File.Exists(first), "Returning to Main Menu discards an unwon import and session while preserving the original");
        yield return new LocalFileSongSource(new Picker { path = first }).Load(result => info = result);
        Check(info.HasValue && !ImportedAudioLibrary.CanImport, "The same trial file can be imported again after abandonment without granting a new trial");
        GameSession.Instance.SelectedSong = info; GameSession.Instance.DetectedGameStyle = GameMusicStyle.Rock;
        GameSession.Instance.RegisterCompletedSong(false);
        Check(PlayerProgressService.Instance.IsJukeboxUnlocked(id) && ImportedAudioLibrary.Find(id).style == GameMusicStyle.Rock, "The fight-victory hook unlocks an import and records its detected style");
        JukeboxController.Instance.Open();
        yield return new WaitForSecondsRealtime(2);
        var view = Object.FindFirstObjectByType<JukeboxView>(FindObjectsInactive.Include);
        var player = Object.FindFirstObjectByType<JukeboxPlayer>();
        Check(view != null && view.gameObject.activeInHierarchy && view.list.childCount > 0, "Main-menu Jukebox opens and populates the actual prefab");
        Check(view.delete.gameObject.activeSelf && !view.lyrics.gameObject.activeSelf, "Imported tracks offer deletion and never offer lyrics");
        view.full.onClick.Invoke(); yield return null;
        Check(view.dialog.activeSelf && !player.Playing && !MusicAccess.Jukebox, "Full-song action shows a purchase placeholder without granting or playing premium");
        view.cancel.onClick.Invoke();
        Check(JukeboxTrack.Build().All(t => t.Unlocked), "Jukebox contains only unlocked songs, including originals");
        view.list.GetChild(0).GetComponent<JukeboxRowView>().button.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.5f);
        Check(player.Current?.id == id && !player.Loading && !player.FullPlayback, "Free unlocked preview plays through the real audio loader");
        view.playPause.onClick.Invoke(); yield return null;
        Check(player.Paused, "Pause works during preview playback");
        view.playPause.onClick.Invoke(); yield return null;
        Check(!player.Paused, "Resume works without reloading the audio");
        player.Stop();
        MusicAccess.Store = new Premium();
        SelectedSongInfo? next = null;
        yield return new LocalFileSongSource(new Picker { path = second }).Load(result => next = result);
        Check(next.HasValue, "Premium Custom Audio imports another real file after the trial");
        GameSession.Instance.SelectedSong = next; GameSession.Instance.DetectedGameStyle = GameMusicStyle.Acoustic;
        GameSession.Instance.RegisterCompletedSong(false);
        string secondId = ImportedAudioLibrary.FindByPath(next.Value.LocalFilePath).id;
        JukeboxController.Instance.Close(); JukeboxController.Instance.Open();
        yield return new WaitForSecondsRealtime(1);
        MusicAccess.Store = null;
        view.list.GetChild(0).GetComponent<JukeboxRowView>().button.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.4f);
        Check(view.next.interactable && view.previous.interactable, "Free preview navigation is available without premium");
        view.next.onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
        Check(player.Current?.id == secondId && !player.FullPlayback, "Free Next starts only the next song preview");
        view.previous.onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
        Check(player.Current?.id == id && !player.FullPlayback, "Free Previous follows collection order and stays in preview mode");
        yield return new WaitForSecondsRealtime(4.2f);
        Check(!player.Playing && view.next.interactable, "A finished free preview stops instead of auto-advancing; manual navigation remains available");
        view.next.onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
        Check(player.Current?.id == secondId && !player.FullPlayback, "Manual Next still works after a preview has ended");
        view.previous.onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
        player.Stop();
        MusicAccess.Store = new Premium();
        view.full.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.5f);
        Check(player.Current?.id == id && player.FullPlayback, "Premium plays the full first unlocked song");
        view.next.onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
        Check(player.Current?.id == secondId, "Next follows first-victory chronology across imported audio");
        view.previous.onClick.Invoke(); yield return new WaitForSecondsRealtime(.4f);
        Check(player.Current?.id == id, "Previous returns to the earlier discovery");
        yield return new WaitForSecondsRealtime(4.2f);
        Check(player.Current?.id == secondId, "Full playback automatically advances to the next unlocked song");
        view.delete.onClick.Invoke(); yield return null;
        Check(view.dialog.activeSelf && ImportedAudioLibrary.Find(secondId) != null, "Deleting an import requires confirmation");
        view.confirm.onClick.Invoke(); yield return null;
        Check(ImportedAudioLibrary.Find(secondId) == null && File.Exists(second) && !PlayerProgressService.Instance.IsJukeboxUnlocked(secondId), "Confirmed deletion removes the game copy and unlock while preserving the original");
        JukeboxController.Instance.Close(); yield return null;
        Check(!player.Playing && !view.gameObject.activeSelf, "Leaving the Jukebox stops playback and releases the screen");
        if (info.Value.Clip != null) Object.Destroy(info.Value.Clip);
        if (next.Value.Clip != null) Object.Destroy(next.Value.Clip);
    }
    private void Cleanup()
    {
        JukeboxController.Instance?.Close();
        MusicAccess.Store = null; ImportedAudioLibrary.TestFolder = null; ImportedAudioLibrary.ReloadForTests();
        if (!string.IsNullOrEmpty(folder) && folder.StartsWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(folder)) Directory.Delete(folder, true);
    }
    private static void MakeWave(string path, float tone)
    {
        const int frequency = 22050, samples = frequency * 4;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples * 2 + 36);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(frequency); writer.Write(frequency * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * tone / frequency) * 1000));
    }
}
#endif
