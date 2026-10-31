using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > MusicGame > Avatars > Garments > Import Cuts Package — the interactive side of GarmentCutsImporter:
/// pick a package folder (…/3D_pipeline/ClothingCutsOutput/&lt;date&gt;), review the package/asset issues, tick the
/// assets to import (valid ones are ticked by default, invalid ones cannot be ticked), optionally assign each
/// garment's final material, then Import. The report is shown below and written to Logs/GarmentCutsImport.txt.
/// </summary>
public class GarmentCutsImportWindow : EditorWindow
{
    private string _packagePath = "";
    private GarmentCutsPackage _package;
    private readonly Dictionary<string, bool> _selected = new();
    private readonly Dictionary<string, Material> _materials = new();
    private readonly GarmentCutsImporter.Options _options = new();
    private Vector2 _scroll;
    private string _report = "";

    [MenuItem("Tools/MusicGame/Avatars/Garments/Import Cuts Package")]
    public static void Open() => GetWindow<GarmentCutsImportWindow>("Garment Cuts Import").Show();

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Package", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _packagePath = EditorGUILayout.TextField("Folder", _packagePath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                string start = Directory.Exists(_packagePath) ? _packagePath : Path.GetFullPath("../AssetsMaterial/3D_pipeline/ClothingCutsOutput");
                string picked = EditorUtility.OpenFolderPanel("Clothing cuts package (folder with manifest.json)", start, "");
                if (!string.IsNullOrEmpty(picked)) { _packagePath = picked; Analyse(); }
            }
            if (GUILayout.Button("Analyse", GUILayout.Width(70))) Analyse();
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_package != null)
        {
            foreach (var issue in _package.Issues) DrawIssue(issue);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Assets ({_package.Assets.Count})", EditorStyles.boldLabel);
            foreach (var asset in _package.Assets)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!asset.IsValid))
                            _selected[asset.Key] = EditorGUILayout.ToggleLeft($"{asset.Key}   ({asset.source})", asset.IsValid && _selected.GetValueOrDefault(asset.Key));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(asset.IsValid ? (asset.coverageConfigured ? "valid" : "valid, no coverage") : "INVALID", EditorStyles.miniBoldLabel);
                    }
                    _materials.TryGetValue(asset.GarmentId, out var material);
                    _materials[asset.GarmentId] = (Material)EditorGUILayout.ObjectField("Final material (optional)", material, typeof(Material), false);
                    foreach (var issue in asset.Issues.Where(i => i.Severity != GarmentCutsPackage.Severity.Info)) DrawIssue(issue);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            _options.ArtRoot = EditorGUILayout.TextField("Art root", _options.ArtRoot);
            _options.ContentRoot = EditorGUILayout.TextField("Content root", _options.ContentRoot);
            _options.ImportMasks = EditorGUILayout.Toggle("Import body masks", _options.ImportMasks);
            _options.SeamPaddingTexels = EditorGUILayout.IntSlider("Seam padding (texels)", _options.SeamPaddingTexels, 0, 16);
            _options.AssignCatalogToBaseAvatar = EditorGUILayout.Toggle("Assign catalog to base avatar", _options.AssignCatalogToBaseAvatar);
            _options.RegisterAddressables = EditorGUILayout.Toggle("Register Addressables", _options.RegisterAddressables);
            using (new EditorGUI.DisabledScope(!_options.RegisterAddressables))
                _options.AddressablesGroupFormat = EditorGUILayout.TextField("Group format ({0}=quality)", _options.AddressablesGroupFormat);

            using (new EditorGUI.DisabledScope(!_package.IsUsable || !_selected.Values.Any(v => v)))
                if (GUILayout.Button("Import selected", GUILayout.Height(28))) Import();
        }

        if (!string.IsNullOrEmpty(_report))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(_report, EditorStyles.wordWrappedMiniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    private void Analyse()
    {
        _package = GarmentCutsPackage.Load(_packagePath);
        _selected.Clear();
        foreach (var asset in _package.Assets) _selected[asset.Key] = asset.IsValid;
        _report = "";
    }

    private void Import()
    {
        _options.Selected = new HashSet<string>(_selected.Where(kv => kv.Value).Select(kv => kv.Key));
        _options.FinalMaterials = _materials.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        var result = GarmentCutsImporter.Import(_package, _options);
        File.WriteAllLines("Logs/GarmentCutsImport.txt", result.Log);
        _report = string.Join("\n", result.Log);
        if (!result.Success) Debug.LogWarning("[GarmentCutsImport] Finished with errors — see the window or Logs/GarmentCutsImport.txt.");
    }

    private static void DrawIssue(GarmentCutsPackage.Issue issue)
    {
        var type = issue.Severity == GarmentCutsPackage.Severity.Error ? MessageType.Error
                 : issue.Severity == GarmentCutsPackage.Severity.Warning ? MessageType.Warning : MessageType.Info;
        EditorGUILayout.HelpBox(issue.Message, type);
    }
}
