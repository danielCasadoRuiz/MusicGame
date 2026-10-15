using System.Text.RegularExpressions;

/// <summary>The three moments a composer speaks. The names are part of the table KEYS
/// ("Composer.{id}.Dialogue.{Category}.{index}") — never rename them.</summary>
public enum ComposerDialogueCategory
{
    /// <summary>Before the Versus — the composer mocks the player's song.</summary>
    Intro,
    /// <summary>The COMPOSER lost (player MatchWon) — grudging praise.</summary>
    Defeat,
    /// <summary>The COMPOSER won (player MatchLost) — insufferable gloating.</summary>
    Victory,
}

/// <summary>
/// Naming contract of the composer dialogue content (see ComposerDialogueService):
///
///   String Table Collection  ComposerDialogues_{OpponentDefinition.id}   (one per composer, ca + en)
///   Entry key                Composer.{id}.Dialogue.{Category}.{index}   (index = 3+ digits, gaps allowed)
///   Addressables label       ComposerDialogues                           (every table + shared data)
///
/// Variants are DISCOVERED from whatever keys the loaded table contains — nothing here (or anywhere
/// in compiled code) knows how many variants exist, so a content update can add, fix or retire
/// lines without a new app version.
/// </summary>
public static class ComposerDialogueKeys
{
    public const string TablePrefix      = "ComposerDialogues_";
    public const string AddressableLabel = "ComposerDialogues";

    private static readonly Regex KeyPattern = new(
        @"^Composer\.(?<id>[a-z0-9_]+)\.Dialogue\.(?<cat>Intro|Defeat|Victory)\.(?<idx>\d{3,6})$",
        RegexOptions.CultureInvariant);

    public static string TableName(string composerId) => TablePrefix + composerId;

    /// <summary>"Composer.{id}.Dialogue." — every key of that composer's dialogues starts with it.</summary>
    public static string KeyPrefix(string composerId) => $"Composer.{composerId}.Dialogue.";

    public static string Key(string composerId, ComposerDialogueCategory category, int index) =>
        $"{KeyPrefix(composerId)}{category}.{index:000}";

    /// <summary>Strict parse of a dialogue key. Anything else (typos, other composers' prefixes, a
    /// missing index) is NOT a variant.</summary>
    public static bool TryParse(string key, out string composerId, out ComposerDialogueCategory category, out int index)
    {
        composerId = null; category = default; index = 0;
        if (string.IsNullOrEmpty(key)) return false;
        var m = KeyPattern.Match(key);
        if (!m.Success) return false;
        composerId = m.Groups["id"].Value;
        category   = (ComposerDialogueCategory)System.Enum.Parse(typeof(ComposerDialogueCategory), m.Groups["cat"].Value);
        index      = int.Parse(m.Groups["idx"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}
