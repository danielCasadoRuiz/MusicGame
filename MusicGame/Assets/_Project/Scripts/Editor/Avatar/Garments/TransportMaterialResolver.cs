using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Maps every submesh of an imported `_cuts` mesh to its manifest slot (zone + source material) from the
/// ACTUAL imported material names — never from manifest order: the FBX importer may reorder material slots,
/// and names can carry suffixes (Blender ".001", Unity " (Instance)", "Material::" namespaces, renames).
///
/// Per imported name, the first strategy that yields candidates wins:
///   1. exact name (case-insensitive) after normalisation (suffixes/namespace stripped on both sides);
///   2. prefix: the imported name starts with the manifest name (extra suffix) or vice versa;
///   3. transport token: the "CCZ_###_" index, which the producer makes unique per object.
/// Exactly one candidate → resolved. None → unknown, several → ambiguous: both leave the submesh UNRESOLVED
/// (its geometry is always kept) with an actionable diagnostic.
/// </summary>
public static class TransportMaterialResolver
{
    public sealed class Result
    {
        /// <summary>Zone per imported submesh index ("" = unresolved).</summary>
        public string[] Regions;
        /// <summary>Manifest slot per imported submesh index (null = unresolved).</summary>
        public GarmentCutsPackage.SubmeshEntry[] Slots;
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public int Resolved;
    }

    private static readonly Regex Token = new(@"CCZ_(\d+)_", RegexOptions.IgnoreCase);
    private static readonly Regex BlenderSuffix = new(@"\.\d{3,}$");
    private static readonly Regex UnitySuffix = new(@"\s*\((Instance|Clone)\)$", RegexOptions.IgnoreCase);

    public static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        string n = name.Trim();
        int ns = n.LastIndexOf("::", StringComparison.Ordinal);
        if (ns >= 0) n = n.Substring(ns + 2);
        string previous;
        do
        {
            previous = n;
            n = UnitySuffix.Replace(n, "");
            n = BlenderSuffix.Replace(n, "");
        } while (n != previous);
        return n.Trim();
    }

    public static Result Resolve(IReadOnlyList<string> importedNames, IReadOnlyList<GarmentCutsPackage.SubmeshEntry> slots, string context)
    {
        var result = new Result { Regions = new string[importedNames.Count], Slots = new GarmentCutsPackage.SubmeshEntry[importedNames.Count] };
        var claimed = new Dictionary<GarmentCutsPackage.SubmeshEntry, int>();

        for (int i = 0; i < importedNames.Count; i++)
        {
            string imported = importedNames[i] ?? "";
            var candidates = Candidates(imported, slots);
            result.Regions[i] = "";
            if (candidates.Count == 0)
            {
                result.Errors.Add($"{context}: submesh {i} material '{imported}' matches no transportMaterial in the manifest — its geometry is always kept.");
                continue;
            }
            if (candidates.Count > 1)
            {
                result.Errors.Add($"{context}: submesh {i} material '{imported}' is ambiguous ({string.Join(", ", candidates.ConvertAll(c => c.transportMaterial))}) — its geometry is always kept.");
                continue;
            }
            var slot = candidates[0];
            if (claimed.TryGetValue(slot, out int other))
                result.Warnings.Add($"{context}: submeshes {other} and {i} both resolve to '{slot.transportMaterial}' (zone '{slot.zone}').");
            claimed[slot] = i;
            result.Slots[i] = slot;
            result.Regions[i] = slot.zone ?? "";
            result.Resolved++;
        }

        foreach (var slot in slots)
            if (!claimed.ContainsKey(slot))
                result.Warnings.Add($"{context}: manifest slot '{slot.transportMaterial}' (zone '{slot.zone}') has no imported submesh (empty after import, or renamed beyond recognition).");
        return result;
    }

    private static List<GarmentCutsPackage.SubmeshEntry> Candidates(string imported, IReadOnlyList<GarmentCutsPackage.SubmeshEntry> slots)
    {
        string n = Normalize(imported);
        var found = new List<GarmentCutsPackage.SubmeshEntry>();

        foreach (var s in slots)
            if (string.Equals(Normalize(s.transportMaterial), n, StringComparison.OrdinalIgnoreCase)) found.Add(s);
        if (found.Count > 0) return found;

        foreach (var s in slots)
        {
            string m = Normalize(s.transportMaterial);
            if (m.Length > 0 && (n.StartsWith(m, StringComparison.OrdinalIgnoreCase) || m.StartsWith(n, StringComparison.OrdinalIgnoreCase) && n.Length > 0))
                found.Add(s);
        }
        if (found.Count > 0) return found;

        var token = Token.Match(imported);
        if (!token.Success) return found;
        foreach (var s in slots)
        {
            var slotToken = Token.Match(s.transportMaterial ?? "");
            if (slotToken.Success && int.Parse(slotToken.Groups[1].Value) == int.Parse(token.Groups[1].Value)) found.Add(s);
        }
        return found;
    }
}
