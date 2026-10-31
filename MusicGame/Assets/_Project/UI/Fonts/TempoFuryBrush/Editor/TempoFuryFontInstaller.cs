using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;

namespace MusicGame.Editor
{
    public static class TempoFuryFontInstaller
    {
        const string Root = "Assets/_Project/UI/Fonts/TempoFuryBrush";
        [Serializable] class Metrics { public int pointSize, atlasWidth, atlasHeight, spaceAdvance; public string characters; public Entry[] glyphs; public Alias[] aliases; public Pair[] pairs; }
        [Serializable] class Entry { public uint unicode, index; public int x,y,width,height,bearingY,advance; }
        [Serializable] class Alias { public uint unicode,target; }
        [Serializable] class Pair { public uint left,right; public float adjustment; }
        [Serializable] class Report { public string unityVersion; public int characters, bitmapGlyphs, sdfGlyphs, bitmapKerning, sdfKerning; public bool legacyLoaded, bitmapPassed, sdfPassed, previewRendered; public string legacyKerningObservation; public string[] assets; }

        [MenuItem("MusicGame/UI/Fonts/Build Tempo Fury Brush")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var data = JsonUtility.FromJson<Metrics>(File.ReadAllText(Root + "/Source/TempoFuryBrush-Metrics.json"));
            var source = AssetDatabase.LoadAssetAtPath<Font>(Root + "/TempoFuryBrush-Regular.ttf");
            if (source == null) throw new Exception("Tempo Fury TTF could not be imported.");
            var bitmap = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/TempoFuryBrush Bitmap.asset");
            if (!HasAtlas(bitmap)) bitmap = BuildBitmap(data);
            var sdf = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/TempoFuryBrush SDF.asset");
            if (!HasAtlas(sdf)) sdf = BuildSdf(source, data);
            var report = new Report { unityVersion = Application.unityVersion, characters = data.characters.Length,
                bitmapGlyphs = bitmap.glyphTable.Count, sdfGlyphs = sdf.glyphTable.Count,
                bitmapKerning = bitmap.fontFeatureTable.glyphPairAdjustmentRecords.Count,
                sdfKerning = sdf.fontFeatureTable.glyphPairAdjustmentRecords.Count,
                legacyLoaded = source.HasCharacter('Ç') && source.HasCharacter('Ó') && source.HasCharacter('·'),
                bitmapPassed = Validate(bitmap, data), sdfPassed = Validate(sdf, data),
                assets = new[] {Root + "/TempoFuryBrush-Regular.ttf", Root + "/TempoFuryBrush Bitmap.asset", Root + "/TempoFuryBrush SDF.asset", Root + "/TempoFuryBrush Preview.prefab"} };
            MakePreview(source, bitmap, sdf, report);
            File.WriteAllText(Root + "/ValidationReport.json", JsonUtility.ToJson(report, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            if (!report.legacyLoaded || !report.bitmapPassed || !report.sdfPassed)
                throw new Exception("Tempo Fury validation failed; see ValidationReport.json.");
            Debug.Log("TEMPO_FURY_FONT_INSTALL_OK " + JsonUtility.ToJson(report));
        }

        static bool HasAtlas(TMP_FontAsset f) => f != null && f.material != null && f.atlasTextures != null && f.atlasTextures.Length > 0 && f.atlasTextures[0] != null;

        static TMP_FontAsset BuildBitmap(Metrics d)
        {
            var png = File.ReadAllBytes(Root + "/Source/TempoFuryBrush-Atlas.png");
            var readable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!readable.LoadImage(png)) throw new Exception("Invalid font atlas.");
            var pixels = readable.GetPixels32();
            var alpha = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
            var texture = new Texture2D(d.atlasWidth, d.atlasHeight, TextureFormat.Alpha8, false)
                { name = "TempoFuryBrush Bitmap Atlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.LoadRawTextureData(alpha); texture.Apply(false, false);
            UnityEngine.Object.DestroyImmediate(readable);
            var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
            asset.name = "TempoFuryBrush Bitmap";
            var shader = Shader.Find("TextMeshPro/Mobile/Bitmap");
            if (shader == null) throw new Exception("TMP Bitmap shader missing.");
            asset.material = new Material(shader) {name = "TempoFuryBrush Bitmap Material", mainTexture = texture};
            asset.atlasTextures = new[] { texture };
            // Version setter is internal in Unity 6's TMP; initialize the serialized field.
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_Version").stringValue = "1.1.0";
            serialized.FindProperty("m_AtlasWidth").intValue = d.atlasWidth;
            serialized.FindProperty("m_AtlasHeight").intValue = d.atlasHeight;
            serialized.FindProperty("m_AtlasPadding").intValue = 0;
            serialized.FindProperty("m_AtlasRenderMode").intValue = (int)GlyphRenderMode.SMOOTH;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            asset.faceInfo = new FaceInfo { familyName = "Tempo Fury Brush", styleName = "Regular", pointSize = 128, scale = 1,
                lineHeight = 200, ascentLine = 155, capLine = 128, meanLine = 100, baseline = 0, descentLine = -32,
                underlineOffset = -12, underlineThickness = 4, strikethroughOffset = 48, strikethroughThickness = 4, tabWidth = 220 };
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            asset.atlasTextures = new[] { texture };
            asset.isMultiAtlasTexturesEnabled = false;
            asset.material.SetColor("_Color", Color.white);
            var glyphs = new Dictionary<uint,Glyph>();
            foreach (var g in d.glyphs)
            {
                var glyph = new Glyph(g.index, new GlyphMetrics(g.width,g.height,0,g.bearingY,g.advance),
                    new GlyphRect(g.x,d.atlasHeight-g.y-g.height,g.width,g.height),1,0);
                glyphs[g.unicode] = glyph; asset.glyphTable.Add(glyph); asset.characterTable.Add(new TMP_Character(g.unicode, asset, glyph));
            }
            var space = new Glyph(1,new GlyphMetrics(0,0,0,0,d.spaceAdvance),new GlyphRect(0,0,0,0),1,0);
            asset.glyphTable.Add(space); asset.characterTable.Add(new TMP_Character(32,asset,space));
            asset.characterTable.Add(new TMP_Character(160,asset,space));
            foreach (var alias in d.aliases) asset.characterTable.Add(new TMP_Character(alias.unicode,asset,glyphs[alias.target]));
            asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            asset.ReadFontAssetDefinition();
            SetPairs(asset,d);
            return SaveFont(asset, "TempoFuryBrush Bitmap.asset");
        }

        static TMP_FontAsset BuildSdf(Font source, Metrics d)
        {
            var asset = TMP_FontAsset.CreateFontAsset(source,128,8,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
            if (asset == null) throw new Exception("TMP could not load the TTF outline.");
            asset.name = "TempoFuryBrush SDF";
            if (!asset.TryAddCharacters(d.characters, out string missing, false)) throw new Exception("SDF missing glyphs: " + missing);
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            asset.material.name = "TempoFuryBrush SDF Material";
            for (int i=0;i<asset.atlasTextures.Length;i++) asset.atlasTextures[i].name = "TempoFuryBrush SDF Atlas " + i;
            asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (fallback != null) asset.fallbackFontAssetTable.Add(fallback);
            SetPairs(asset,d);
            return SaveFont(asset,"TempoFuryBrush SDF.asset");
        }

        static void SetPairs(TMP_FontAsset asset, Metrics d)
        {
            var pairs = new Dictionary<ulong,GlyphPairAdjustmentRecord>();
            foreach (var p in d.pairs)
            {
                if (!asset.characterLookupTable.TryGetValue(p.left,out var l) || !asset.characterLookupTable.TryGetValue(p.right,out var r)) continue;
                ulong key=((ulong)l.glyphIndex<<32)|r.glyphIndex;
                pairs[key]=new GlyphPairAdjustmentRecord(new GlyphAdjustmentRecord(l.glyphIndex,new GlyphValueRecord(0,0,p.adjustment,0)),
                    new GlyphAdjustmentRecord(r.glyphIndex,new GlyphValueRecord(0,0,0,0)));
            }
            asset.fontFeatureTable.glyphPairAdjustmentRecords = pairs.Values.ToList();
            asset.ReadFontAssetDefinition();
        }

        static TMP_FontAsset SaveFont(TMP_FontAsset asset, string filename)
        {
            string path=Root+"/"+filename;
            // Keep strong references across CreateAsset's import / serialization boundary.
            var material=asset.material;
            var textures=asset.atlasTextures.ToArray();
            var existing=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing==null) AssetDatabase.CreateAsset(asset,path);
            else
            {
                var transient=asset;
                EditorUtility.CopySerialized(transient,existing); asset=existing;
                transient.atlasTextures=Array.Empty<Texture2D>(); transient.material=null;
                UnityEngine.Object.DestroyImmediate(transient);
            }
            foreach(var texture in textures) AssetDatabase.AddObjectToAsset(texture,asset);
            AssetDatabase.AddObjectToAsset(material,asset);
            asset.atlasTextures=textures; asset.material=material;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static bool Validate(TMP_FontAsset asset, Metrics d)
        {
            if (!HasAtlas(asset)) throw new Exception("Font texture or material did not persist: "+asset.name);
            foreach(char c in d.characters) if(!asset.HasCharacter(c)) throw new Exception(asset.name+" missing "+c);
            var canvasGo=new GameObject("TempoFury Font Validation Canvas",typeof(RectTransform),typeof(Canvas));
            var go=new GameObject("TempoFury Font Validation",typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform,false);
            try
            {
                var t=go.AddComponent<TextMeshProUGUI>(); t.font=asset; t.fontSize=128; t.enableAutoSizing=false;
                t.rectTransform.sizeDelta=new Vector2(8000,1000); t.fontFeatures=new List<OTL_FeatureTag>{OTL_FeatureTag.kern};
                foreach(var pair in new[]{"CA","CO","ÇÓ","PA","A!","L·","·L"})
                {
                    t.text=pair; Canvas.ForceUpdateCanvases(); t.ForceMeshUpdate(true,true);
                    if(t.textInfo.characterCount!=2) throw new Exception(asset.name+" TMP pair didn't render: "+pair+" (count="+t.textInfo.characterCount+")");
                    var match=d.pairs.FirstOrDefault(p=>p.left==pair[0]&&p.right==pair[1]);
                    float expected=asset.characterLookupTable[pair[0]].glyph.metrics.horizontalAdvance+(match==null?0:match.adjustment);
                    float actual=t.textInfo.characterInfo[1].origin-t.textInfo.characterInfo[0].origin;
                    if(Math.Abs(expected-actual)>.25f) throw new Exception(asset.name+" kerning mismatch "+pair+": "+actual+" expected "+expected);
                }
                t.text="CANÇÓ COL·LECCIÓ ÀÁ ÈÉ ÌÍ ÒÓ ÙÚ PAUSE TEMPO VICTORY 3 2 1"; t.ForceMeshUpdate(true,true);
                return t.textInfo.characterCount==t.text.Length;
            }
            finally { UnityEngine.Object.DestroyImmediate(canvasGo); }
        }

        static void MakePreview(Font legacy, TMP_FontAsset bitmap, TMP_FontAsset sdf, Report report)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Tempo Fury Brush - Font Preview");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            try
            {
                var cameraGo=new GameObject("Preview Camera",typeof(Camera));cameraGo.transform.SetParent(root.transform);
                var camera=cameraGo.GetComponent<Camera>();camera.transform.position=new Vector3(900,600,-10);camera.orthographic=true;camera.orthographicSize=600;
                camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.075f,.12f);camera.nearClipPlane=.1f;camera.farClipPlane=100;
                var canvasGo=new GameObject("Font Samples",typeof(RectTransform),typeof(Canvas));canvasGo.transform.SetParent(root.transform);
                var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
                var rect=canvasGo.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(1800,1200);rect.position=new Vector3(900,600,0);
                string[] samples={"TEMPO FURY  PAUSE  VICTORY!","CANÇÓ  COCA  COL·LECCIÓ","À Á È É Ì Í Ò Ó Ù Ú","3  2  1   ROCK!   MÚSICA"};
                int y=35;
                Label(canvasGo.transform,sdf,"TMP BITMAP — exact approved atlas",22,y,new Color(.5f,.8f,1));y+=55;
                foreach(var sample in samples){Label(canvasGo.transform,bitmap,sample,64,y,Color.white);y+=100;}
                Label(canvasGo.transform,sdf,"TMP SDF — scalable font asset",22,y,new Color(.5f,.8f,1));y+=55;
                Label(canvasGo.transform,sdf,samples[0],64,y,new Color(1,.75f,.3f));y+=115;
                Label(canvasGo.transform,sdf,"LEGACY UI TEXT — TrueType",22,y,new Color(.5f,.8f,1));y+=55;
                var textGo=new GameObject("Legacy Text (TTF)",typeof(RectTransform));textGo.transform.SetParent(canvasGo.transform,false);
                Position(textGo.GetComponent<RectTransform>(),y,140);var text=textGo.AddComponent<Text>();text.font=legacy;text.fontSize=64;text.text=samples[0];text.color=Color.white;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;
                Canvas.ForceUpdateCanvases();
                var settings=text.GetGenerationSettings(new Vector2(1700,140));settings.fontSize=128;settings.horizontalOverflow=HorizontalWrapMode.Overflow;
                var generator=new TextGenerator();generator.Populate("CA",settings);var infos=generator.GetCharactersArray();
                report.legacyKerningObservation=infos.Length>=2 ? "Legacy CA cursor advance at128: "+(infos[1].cursorPos.x-infos[0].cursorPos.x)+". TMP explicitly applies approved pair adjustments." : "Legacy TTF renders; use TMP for the approved pair adjustment table.";
                PrefabUtility.SaveAsPrefabAsset(root,Root+"/TempoFuryBrush Preview.prefab");
                foreach(var label in root.GetComponentsInChildren<TMP_Text>()) label.ForceMeshUpdate(true,true);
                Canvas.ForceUpdateCanvases();
                var target=new RenderTexture(1800,1200,24);camera.targetTexture=target;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                var previous=RenderTexture.active;RenderTexture.active=target;var shot=new Texture2D(1800,1200,TextureFormat.RGB24,false);shot.ReadPixels(new Rect(0,0,1800,1200),0,0);shot.Apply();
                report.previewRendered=shot.GetPixels32().Count(p=>p.r>120&&p.g>120&&p.b>120)>1000;
                if(!report.previewRendered) throw new Exception("Unity font preview contains no rendered text.");
                File.WriteAllBytes(Root+"/TempoFuryBrush Preview.png",shot.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(shot);target.Release();UnityEngine.Object.DestroyImmediate(target);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void Label(Transform parent,TMP_FontAsset font,string value,int size,int y,Color color)
        {
            var go=new GameObject(value,typeof(RectTransform));go.transform.SetParent(parent,false);Position(go.GetComponent<RectTransform>(),y,110);
            var text=go.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSize=size;text.text=value;text.color=color;text.enableAutoSizing=false;
            text.fontFeatures=new List<OTL_FeatureTag>{OTL_FeatureTag.kern};text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Overflow;
        }
        static void Position(RectTransform rect,int y,int height)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(35,-y);rect.sizeDelta=new Vector2(1730,height);}
    }
}
