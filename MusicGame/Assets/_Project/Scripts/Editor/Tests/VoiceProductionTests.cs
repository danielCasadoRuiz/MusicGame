#if UNITY_EDITOR
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Localization;
using UnityEngine;

/// <summary>
/// Edit-mode checks of the voice production state for the generated sample voices (run after a generation):
/// incremental detection (one edited phrase → only that phrase pending, nothing else), languages without an
/// engine (Catalan) never pending, packs consistent with the tables (hashes) and registered as remote
/// Addressables per voice × language, vocalisations present, the game audio library complete.
/// Run: -runTests -testPlatform EditMode -testFilter VoiceProductionTests
/// </summary>
public class VoiceProductionTests
{
    // Every composer voice with dialogue (all generated now); the announcers are checked in the library test.
    private static string[] SampleVoices => VoiceProduction.Definitions().Where(d => d.extraLines == null || d.extraLines.Count == 0).Select(d => d.voiceId).ToArray();

    [Test]
    public void TextHashIgnoresWhitespaceOnly()
    {
        Assert.AreEqual(VoiceTextHash.Of("Hello  world "), VoiceTextHash.Of(" Hello world"));
        Assert.AreNotEqual(VoiceTextHash.Of("Hello world"), VoiceTextHash.Of("Hello world!"));
    }

    [Test]
    public void SampleIsGeneratedAndOnlyAnEditedLineBecomesPending()
    {
        var manifest = VoiceProduction.LoadManifest();
        var lines = VoiceProduction.Inventory(manifest).Where(l => SampleVoices.Contains(l.Voice) && l.Language == "en").ToList();
        Assert.Greater(lines.Count, 0);
        Assert.IsTrue(lines.All(l => l.State == VoiceProduction.LineState.UpToDate), string.Join("\n", lines.Where(l => l.State != VoiceProduction.LineState.UpToDate).Select(l => $"{l.Id} {l.State}")));

        // Edit ONE phrase in memory (never saved), re-inventory, restore.
        var target = lines.First(l => l.Language == "en");
        var table = LocalizationEditorSettings.GetStringTableCollection(ComposerDialogueKeys.TableName(target.Voice)).StringTables.First(t => t.LocaleIdentifier.Code == "en");
        var entry = table.GetEntry(target.Key);
        string original = entry.Value;
        try
        {
            entry.Value = original + " Indeed.";
            var after = VoiceProduction.Inventory(manifest).Where(l => SampleVoices.Contains(l.Voice) && l.Language == "en").ToList();
            var pending = after.Where(VoiceProduction.IsPending).ToList();
            Assert.AreEqual(1, pending.Count, string.Join(", ", pending.Select(p => p.Id)));
            Assert.AreEqual(target.Id, pending[0].Id);
            Assert.AreEqual(VoiceProduction.LineState.TextChanged, pending[0].State);
        }
        finally { entry.Value = original; }
        Assert.IsTrue(VoiceProduction.Inventory(manifest).Where(l => l.Id == target.Id).All(l => l.State == VoiceProduction.LineState.UpToDate));
    }

    [Test]
    public void LanguagesWithoutAnEngineAreNeverPending()
    {
        var ca = VoiceProduction.Inventory(VoiceProduction.LoadManifest()).Where(l => l.Language == "ca").ToList();
        Assert.Greater(ca.Count, 0, "the project has Catalan lines");
        Assert.IsTrue(ca.All(l => l.State == VoiceProduction.LineState.NoEngine && !VoiceProduction.IsPending(l)));
    }

    [Test]
    public void PacksMatchTheTablesAndAreRemoteAddressables()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        foreach (var v in SampleVoices)
        {
            var pack = AssetDatabase.LoadAssetAtPath<ComposerVoicePack>($"{VoiceProduction.GameRoot}/{v}/en/VoicePack_{v}_en.asset");
            Assert.IsNotNull(pack, v);
            var table = LocalizationEditorSettings.GetStringTableCollection(ComposerDialogueKeys.TableName(v)).StringTables.First(t => t.LocaleIdentifier.Code == "en");
            int tableLines = VoiceProduction.Inventory(VoiceProduction.LoadManifest()).Count(l => l.Voice == v && l.Language == "en");
            Assert.AreEqual(tableLines, pack.lines.Count, $"{v}: every English line voiced, none extra");
            foreach (var l in pack.lines)
            {
                Assert.IsNotNull(l.clip, l.key);
                Assert.AreEqual(VoiceTextHash.Of(table.GetEntry(l.key).Value), l.textHash, l.key);
                Assert.Greater(l.duration, 0.3f, l.key);
            }
            var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pack)));
            Assert.IsNotNull(entry, v);
            Assert.AreEqual(ComposerVoicePack.Address(v, "en"), entry.address);
            Assert.AreEqual(ComposerVoicePack.GroupName(v, "en"), entry.parentGroup.Name);

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<ComposerVoicePack>($"{VoiceProduction.GameRoot}/{v}/fx/VoicePack_{v}_fx.asset"), "no TTS vocalisation pack any more");
        }
    }

    [Test]
    public void GameLibraryIsCompleteIncludingPlayerVocalisations()
    {
        var lib = AssetDatabase.LoadAssetAtPath<GameSfxLibrary>(GameAudioSetup.LibraryPath);
        Assert.IsNotNull(lib?.mixer);
        Assert.IsNotNull(lib.musicGroup); Assert.IsNotNull(lib.sfxGroup); Assert.IsNotNull(lib.voiceGroup);
        Assert.IsTrue(lib.AllCues().All(c => c.HasClips), string.Join(",", lib.AllCues().Where(c => !c.HasClips).Select(c => c.name)));
        Assert.IsNotNull(lib.playerEfforts?.hurt);
        Assert.IsNotNull(lib.defaultComposerEfforts?.defeat);
        Assert.GreaterOrEqual(lib.announcers?.Count(a => a != null && a.IsComplete && a.inRotation) ?? 0, 1, "at least one complete announcer set in rotation");
    }
}
#endif
