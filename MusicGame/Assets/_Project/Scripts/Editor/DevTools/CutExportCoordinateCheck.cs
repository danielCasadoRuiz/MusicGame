using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Checks segmented FBX export coordinates against the original rigged asset.</summary>
public static class CutExportCoordinateCheck
{
    public static void RunFromCommandLine()
    {
        const string folder = "Assets/__CutExportCheck";
        const string imported = folder + "/jacket.fbx";
        const string original = "Assets/_Project/Art/Opponents/Monteverdi/Monteverdi_jacket/Low/Monteverdi_jacket_LOW_5k_rigged.fbx";
        int code = 1;
        string report;
        try
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-cutExportProbe");
            if (i < 0 || i + 1 == args.Length) throw new ArgumentException("Missing -cutExportProbe FBX path.");
            if (AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("Scratch folder already exists.");
            AssetDatabase.CreateFolder("Assets", "__CutExportCheck");
            File.Copy(args[i + 1], imported);
            AssetDatabase.ImportAsset(imported, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(imported);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.importBlendShapes = true;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.SaveAndReimport();
            var before = AssetDatabase.LoadAssetAtPath<GameObject>(original).GetComponentInChildren<SkinnedMeshRenderer>();
            var after = AssetDatabase.LoadAssetAtPath<GameObject>(imported).GetComponentInChildren<SkinnedMeshRenderer>();
            var a = before.sharedMesh.bounds;
            var b = after.sharedMesh.bounds;
            float centerError = Vector3.Distance(a.center, b.center);
            float sizeError = Vector3.Distance(a.size, b.size);
            float boneError = 0;
            foreach (var bone in after.bones)
            {
                var reference = before.bones.FirstOrDefault(t => t.name == bone.name);
                if (reference == null) throw new InvalidOperationException("Unknown exported bone " + bone.name);
                boneError = Mathf.Max(boneError, Vector3.Distance(reference.position, bone.position));
            }
            bool passed = centerError < .002f && sizeError < .002f && boneError < .002f;
            report = $"{(passed ? "PASS" : "FAIL")} original rigged vs segmented export\n" +
                $"Original bounds: {a}\nSegmented bounds: {b}\nCenter error: {centerError:R}\nSize error: {sizeError:R}\nBone rest-position error: {boneError:R}\n";
            code = passed ? 0 : 1;
        }
        catch (Exception e) { report = "FAIL " + e; }
        finally
        {
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            AssetDatabase.SaveAssets();
        }
        File.WriteAllText("Logs/CutExportCoordinateCheck.txt", report);
        Debug.Log(report);
        EditorApplication.Exit(code);
    }
}
