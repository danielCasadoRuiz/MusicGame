using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// One parsed MakeHuman .target file: a sparse list of (raw base-mesh vertex index, delta) pairs,
/// plus the macro corner tokens parsed from its filename (e.g.
/// "universal-female-young-maxmuscle-maxweight" -> female / young / maxmuscle / maxweight).
/// Ported verbatim from AvatarLab's MakeHumanTarget.
/// </summary>
public class MakeHumanTarget
{
    public readonly string Name;
    public readonly string[] CornerTokens;
    public readonly int[] VertexIndices;
    public readonly Vector3[] Deltas;

    public MakeHumanTarget(string name, string[] cornerTokens, int[] vertexIndices, Vector3[] deltas)
    {
        Name = name;
        CornerTokens = cornerTokens;
        VertexIndices = vertexIndices;
        Deltas = deltas;
    }
}

/// <summary>
/// Loads every macrodetails .target (bundled as .txt under Source/Targets/macrodetails) — ported
/// from AvatarLab's MakeHumanTargetLibrary. Same filename tokenization as MakeHuman's own
/// Component.update() (lib/targets.py): tokens matching a known macro corner are recorded,
/// everything else ("universal", ...) ignored. The only change from AvatarLab: reads plain files
/// from a folder (Editor bake time) instead of Resources.LoadAll (AvatarLab evaluated at runtime;
/// MusicGame bakes, so none of this ships in a build).
///
/// Only the corners the bake actually reaches are bundled (young + caucasian: 20 files) — every
/// other corner has an exact-zero weight at the fixed Age/Race MusicGame uses, so leaving them out
/// changes nothing. Adding more .target files later (e.g. to expose Age) needs no code change.
/// </summary>
public class MakeHumanTargetLibrary
{
    private static readonly HashSet<string> KnownCornerTokens = new HashSet<string>
    {
        "male", "female",
        "baby", "child", "young", "old",
        "caucasian", "asian", "african",
        "minmuscle", "averagemuscle", "maxmuscle",
        "minweight", "averageweight", "maxweight",
        "minheight", "averageheight", "maxheight",
        "uncommonproportions", "regularproportions", "idealproportions",
    };

    public IReadOnlyList<MakeHumanTarget> Targets => _targets;
    private readonly List<MakeHumanTarget> _targets = new List<MakeHumanTarget>();

    public static MakeHumanTargetLibrary LoadFolder(string folder)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"MakeHuman targets folder not found at '{folder}'.");

        var library = new MakeHumanTargetLibrary();
        foreach (string file in Directory.GetFiles(folder, "*.txt", SearchOption.AllDirectories))
        {
            var target = Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
            if (target.VertexIndices.Length > 0) library._targets.Add(target);
        }
        library._targets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name)); // deterministic summation order
        return library;
    }

    /// <summary>Every target must index inside the base mesh's raw vertex list — a target authored
    /// for another topology fails here, named, instead of silently writing garbage.</summary>
    public void Validate(int rawVertexCount, List<string> errors)
    {
        if (_targets.Count == 0) errors.Add("No MakeHuman targets were loaded.");
        foreach (var target in _targets)
        {
            foreach (int index in target.VertexIndices)
            {
                if (index < 0 || index >= rawVertexCount)
                {
                    errors.Add($"Target '{target.Name}' references raw vertex {index}, outside the base mesh's 0..{rawVertexCount - 1} — incompatible topology.");
                    break;
                }
            }
            foreach (var d in target.Deltas)
            {
                if (float.IsNaN(d.x) || float.IsNaN(d.y) || float.IsNaN(d.z))
                {
                    errors.Add($"Target '{target.Name}' contains NaN deltas.");
                    break;
                }
            }
        }
    }

    private static MakeHumanTarget Parse(string fileName, string text)
    {
        var corners = new List<string>();
        foreach (string token in fileName.Split('-'))
            if (KnownCornerTokens.Contains(token)) corners.Add(token);

        string[] lines = text.Split('\n');
        var indices = new List<int>(lines.Length);
        var deltas = new List<Vector3>(lines.Length);

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int vertexIndex)) continue;

            // Deltas live in the same raw space as base positions — same conversion (scale +
            // handedness), or a target would push vertices the wrong way.
            indices.Add(vertexIndex);
            deltas.Add(MakeHumanUnits.ToUnity(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
        }

        return new MakeHumanTarget(fileName, corners.ToArray(), indices.ToArray(), deltas.ToArray());
    }

    private static float ParseFloat(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
