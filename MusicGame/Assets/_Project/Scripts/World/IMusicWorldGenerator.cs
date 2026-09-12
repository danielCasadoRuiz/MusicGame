using UnityEngine;

/// <summary>
/// Contract for all world/path generators.
/// Implementors convert SongProfile + config into a MusicPath ready for gameplay.
/// Called synchronously before the warmup timer starts — keep it fast (< ~200 ms).
/// </summary>
public interface IMusicWorldGenerator
{
    string Name { get; }

    /// <summary>
    /// Generate a complete MusicPath.
    /// startPosition = world position where the path should begin (usually the player's spawn point).
    /// The returned path covers musicDistance [0 .. (warmupTime + songDuration) * unitsPerSecond].
    /// </summary>
    MusicPath Generate(SongProfile profile, GameplayConfig config, Vector3 startPosition);
}
