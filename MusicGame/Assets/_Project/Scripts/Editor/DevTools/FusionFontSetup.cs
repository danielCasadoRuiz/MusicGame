#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

public static class FusionFontSetup
{
    const string Root="Assets/_Project/UI/Fonts/TempoFuryFusion";
    const string Old="Assets/_Project/UI/Fonts/TempoFuryBrush/TempoFuryBrush SDF.asset";
    [Serializable] class Data{public string characters;public int unitsPerEm;public Pair[] pairs;}
    [Serializable] class Pair{public uint left,right;public float adjustment;}
    static TMP_FontAsset font,old,bitmap;
    static Font source,oldSource;
    static readonly List<string> changed=new List<string>();
    static bool rebuild;
    public static void ReviewCurrent(){Run();SongShowcaseReview.CaptureTitleTintTrials();}
    public static void RebuildAndReview()
    {
        rebuild=true;
        try{Run();SongShowcaseReview.CaptureTitleTintTrials();}
        finally{rebuild=false;}
    }
    [MenuItem("MusicGame/UI/Fonts/Install Tempo Fury Fusion")]
    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var data=JsonUtility.FromJson<Data>(File.ReadAllText(Root+"/Source/Metrics.json"));
        source=AssetDatabase.LoadAssetAtPath<Font>(Root+"/TempoFuryFusion-Regular.ttf");
        if(source==null)throw new Exception("Fusion TTF import failed");
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/TempoFuryFusion SDF.asset");
        if(font==null||rebuild)
        {
            var existing=font;
            font=TMP_FontAsset.CreateFontAsset(source,128,8,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
            if(!font.TryAddCharacters(data.characters,out string missing,false))throw new Exception("Missing Fusion glyphs: "+missing);
            font.name="TempoFuryFusion SDF";font.atlasPopulationMode=AtlasPopulationMode.Static;
            font.material.name="TempoFuryFusion SDF Material";
            var material=font.material;var textures=font.atlasTextures.ToArray();
            if(existing==null)
            {
                AssetDatabase.CreateAsset(font,Root+"/TempoFuryFusion SDF.asset");
                AssetDatabase.AddObjectToAsset(material,font);
                for(int i=0;i<textures.Length;i++){textures[i].name="TempoFuryFusion Atlas "+i;AssetDatabase.AddObjectToAsset(textures[i],font);}
                font.material=material;font.atlasTextures=textures;
            }
            else
            {
                // Keep GUID and subasset IDs: every existing prefab/material stays valid.
                var keptMaterial=existing.material;var keptTextures=existing.atlasTextures.ToArray();
                if(keptTextures.Length!=textures.Length)throw new Exception("Fusion atlas count changed");
                for(int i=0;i<textures.Length;i++){EditorUtility.CopySerialized(textures[i],keptTextures[i]);EditorUtility.SetDirty(keptTextures[i]);}
                EditorUtility.CopySerialized(material,keptMaterial);keptMaterial.mainTexture=keptTextures[0];EditorUtility.SetDirty(keptMaterial);
                var transient=font;EditorUtility.CopySerialized(transient,existing);
                existing.material=keptMaterial;existing.atlasTextures=keptTextures;
                transient.material=null;transient.atlasTextures=Array.Empty<Texture2D>();
                UnityEngine.Object.DestroyImmediate(transient);UnityEngine.Object.DestroyImmediate(material);
                foreach(var texture in textures)UnityEngine.Object.DestroyImmediate(texture);
                font=existing;
            }
        }
        font.fallbackFontAssetTable=new List<TMP_FontAsset>();
        var fallback=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if(fallback!=null)font.fallbackFontAssetTable.Add(fallback);
        font.ReadFontAssetDefinition();
        var records=new Dictionary<ulong,GlyphPairAdjustmentRecord>();
        foreach(var p in data.pairs)
        {
            var l=font.characterLookupTable[p.left];var r=font.characterLookupTable[p.right];
            records[((ulong)l.glyphIndex<<32)|r.glyphIndex]=new GlyphPairAdjustmentRecord(
                new GlyphAdjustmentRecord(l.glyphIndex,new GlyphValueRecord(0,0,p.adjustment*font.faceInfo.pointSize/data.unitsPerEm,0)),
                new GlyphAdjustmentRecord(r.glyphIndex,new GlyphValueRecord()));
        }
        font.fontFeatureTable.glyphPairAdjustmentRecords=records.Values.ToList();font.ReadFontAssetDefinition();
        EditorUtility.SetDirty(font);AssetDatabase.SaveAssets();
        foreach(char c in data.characters)if(!font.HasCharacter(c))throw new Exception("Missing U+"+((int)c).ToString("X4"));
        old=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Old);
        bitmap=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/TempoFuryBrush/TempoFuryBrush Bitmap.asset");
        oldSource=AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/UI/Fonts/TempoFuryBrush/TempoFuryBrush-Regular.ttf");
        changed.Clear();
        // Update the shared source first: ExecuteAlways titles read it on prefab load.
        var library=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>("Assets/_Project/Configs/UI/StyleSkinLibrary.asset");
        library.titleFont=font;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        changed.Add(AssetDatabase.GetAssetPath(library));
        string[] oldGuids={AssetDatabase.AssetPathToGUID(Old),AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(bitmap)),AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(oldSource))};
        foreach(var path in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/_Project/")&&!p.Contains("/Fonts/")&&(p.EndsWith(".prefab")||p.EndsWith(".asset")||p.EndsWith(".mat")||p.EndsWith(".unity"))))
        {
            string yaml=File.ReadAllText(path);
            if(!oldGuids.Any(g=>!string.IsNullOrEmpty(g)&&yaml.Contains(g))&&path!=SongSelectionSkinSetup.ScreenPath)continue;
            // ExecuteAlways title components may already swap to the library font
            // during prefab loading; still persist that change when the source had old references.
            bool dirty=path.EndsWith(".prefab")&&oldGuids.Any(g=>!string.IsNullOrEmpty(g)&&yaml.Contains(g));
            if(path.EndsWith(".prefab"))
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if(path==SongSelectionSkinSetup.ScreenPath){SongShowcaseSetup.ConfigureSelectedHeadline(root.GetComponent<SongSelectionView>());dirty=true;}
                    foreach(var c in root.GetComponentsInChildren<Component>(true))if(c!=null)dirty|=Replace(c);
                    foreach(var progress in root.GetComponentsInChildren<SongTitlePreviewProgress>(true))
                        if(progress.title!=null&&progress.title.font==font){progress.nativeItalicAngle=15;EditorUtility.SetDirty(progress);dirty=true;}
                    if(dirty)PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            else if(!path.EndsWith(".unity"))
            {
                foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))if(asset!=null)dirty|=Replace(asset);
            }
            else if(File.ReadAllText(path).Contains(AssetDatabase.AssetPathToGUID(Old)))
            {
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                foreach(var root in scene.GetRootGameObjects())foreach(var c in root.GetComponentsInChildren<Component>(true))if(c!=null)dirty|=Replace(c);
                if(dirty)EditorSceneManager.SaveScene(scene);
            }
            if(dirty)changed.Add(path);
        }
        AssetDatabase.SaveAssets();
        Validate(data);
        SongShowcaseReview.CaptureFusionTypography();
        File.WriteAllText("../fusion-font-validation.txt","PASS: "+data.characters.Length+" mapped characters, all legacy characters covered; "+font.glyphTable.Count+" SDF glyphs; "+font.atlasTextures.Length+" atlas; "+records.Count+" optical kerning pairs.\nMigrated:\n"+string.Join("\n",changed));
        Debug.Log("FUSION_FONT_INSTALL_OK "+changed.Count+" assets migrated");
    }
    static bool Replace(UnityEngine.Object target)
    {
        bool dirty=false;
        if(target is Material material&&old!=null&&material.HasProperty("_MainTex")&&material.GetTexture("_MainTex")==old.material.GetTexture("_MainTex"))
        {
            material.mainTexture=font.material.mainTexture;
            foreach(string p in new[]{"_TextureWidth","_TextureHeight","_GradientScale","_ScaleX","_ScaleY"})if(material.HasProperty(p)&&font.material.HasProperty(p))material.SetFloat(p,font.material.GetFloat(p));
            dirty=true;
        }
        var so=new SerializedObject(target);var it=so.GetIterator();
        while(it.Next(true))
        {
            if(it.propertyType!=SerializedPropertyType.ObjectReference)continue;
            var value=it.objectReferenceValue;UnityEngine.Object next=null;
            if(value==old||value==bitmap)next=font;
            else if(value==oldSource)next=source;
            else if(old!=null&&value==old.material||bitmap!=null&&value==bitmap.material)next=font.material;
            if(next!=null&&value!=null){it.objectReferenceValue=next;dirty=true;}
        }
        if(dirty){so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(target);}
        return dirty;
    }
    static void Validate(Data data)
    {
        var canvas=new GameObject("Fusion validation canvas",typeof(RectTransform),typeof(Canvas));
        var go=new GameObject("Fusion glyph validation",typeof(RectTransform));go.transform.SetParent(canvas.transform,false);
        try
        {
            var text=go.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSize=100;text.rectTransform.sizeDelta=new Vector2(5000,500);
            foreach(string sample in new[]{"À Á È É Ì Í Ò Ó Ù Ú","à á è é ì í ò ó ù ú","Ç ç L·L Ï ï Ü ü","ZERO NINE","CHOOSE YOUR SONG"})
            {
                text.text=sample;text.ForceMeshUpdate(true,true);
                if(text.textInfo.characterCount!=sample.Length)throw new Exception("Invalid Fusion mesh: "+sample+" count="+text.textInfo.characterCount);
                foreach(var c in text.textInfo.characterInfo.Take(text.textInfo.characterCount))if(c.character!=' '&&c.fontAsset!=font)throw new Exception("Fallback used for "+c.character);
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(canvas);}
        foreach(var path in changed.Where(p=>p.EndsWith(".prefab")||p.EndsWith(".asset")))
            if(File.ReadAllText(path).Contains(AssetDatabase.AssetPathToGUID(Old)))throw new Exception("Old title reference remains: "+path);
    }
}
#endif
