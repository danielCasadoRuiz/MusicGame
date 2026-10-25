using UnityEngine;

public enum GameAudioChannel { Music, Sfx, Voice }

/// <summary>Where a sound belongs: everything of a scope is stopped when that part of the game is left
/// (GameAudio), so nothing ever sounds after a screen change or a cancellation.</summary>
public enum GameAudioScope { UI, Runner, Fight }

/// <summary>
/// One game sound event with its variants: a random clip (never the same one twice in a row), a small pitch /
/// volume spread, and anti-pile-up limits (at most maxInstances playing, at least cooldown seconds between two
/// starts). Played by GameAudio on its channel (independent volume) — clips are preloaded with the library.
/// </summary>
[CreateAssetMenu(fileName = "SfxCue", menuName = "MusicGame/Audio/Sfx Cue")]
public class SfxCue : ScriptableObject
{
    public AudioClip[] clips;
    public GameAudioChannel channel = GameAudioChannel.Sfx;
    [Range(0f, 1f)] public float volume = 0.7f;
    [Tooltip("Random volume reduction per play (0 = always the same).")]
    [Range(0f, 0.5f)] public float volumeJitter = 0.08f;
    [Tooltip("Random pitch range per play.")]
    public Vector2 pitch = new(0.97f, 1.03f);
    [Tooltip("At most this many of this cue at once (a new one is skipped).")]
    [Min(1)] public int maxInstances = 2;
    [Tooltip("Minimum unscaled seconds between two starts of this cue.")]
    [Min(0f)] public float cooldown = 0.05f;
    [Tooltip("Origin / licence of the clips (also listed in Assets/_Project/Audio/Sfx/CREDITS.md).")]
    [TextArea] public string credits;

    public bool HasClips => clips != null && clips.Length > 0;
}
