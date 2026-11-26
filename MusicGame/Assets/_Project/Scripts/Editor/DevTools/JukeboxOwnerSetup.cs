#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class JukeboxOwnerSetup
{
    const string Art="Assets/_Project/Art/UI/Jukebox/";
    const string Prefab="Assets/_Project/Prefabs/UI/Jukebox.prefab";
    const string MaterialPath="Assets/_Project/Configs/UI/JukeboxOwnerEngraving.mat";
    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string file in new[]{"OwnerPlaqueBlank","OwnerMedallionStand","ScatteredScoresV7","CabinetClearedTop","CabinetOwnerIntegrated"})
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(Art+file+".png");
            importer.textureType=file=="CabinetOwnerIntegrated"?TextureImporterType.Sprite:TextureImporterType.Default;
            if(file=="CabinetOwnerIntegrated"){importer.spriteImportMode=SpriteImportMode.Single;var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);}importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;importer.isReadable=false;importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.CompressedHQ;
            importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try{Configure(root);PrefabUtility.SaveAsPrefabAsset(root,Prefab);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();ValidateAndCapture();
    }
    public static bool AssetsReady=>File.Exists(Art+"OwnerPlaqueBlank.png")&&AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"OwnerMedallionStand.png")!=null&&AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"CabinetOwnerIntegrated.png")!=null;
    public static void Configure(GameObject root)
    {
        var view=root.GetComponent<JukeboxView>();view.framingInsets=new Vector2(90,50);
        var cabinet=view.cabinetContent.Find("Cabinet").GetComponent<Image>();
        var crop=cabinet.GetComponent<JukeboxCabinetBaseCrop>()??cabinet.gameObject.AddComponent<JukeboxCabinetBaseCrop>();crop.clearUpperRight=false;
        cabinet.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+"CabinetOwnerIntegrated.png");
        foreach(string name in new[]{"OwnerDisplayRoot","PaperSpillRoot","CabinetClearedTop","Medallion0","Medallion1","Medallion2","Medallion3"})
        {var old=view.cabinetContent.Find(name);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);}
        var display=Rect("OwnerDisplayRoot",view.cabinetContent,0,0,1740,980);
        var aperture=AssetDatabase.LoadAssetAtPath<Sprite>(Art+"MedallionAperture.png");
        var centers=new[]{new Vector2(1198,90),new Vector2(1303,90),new Vector2(1405,90),new Vector2(1507,90)};
        var diameters=new[]{74f,74f,74f,74f};view.medallions=new Image[4];
        for(int i=0;i<4;i++)
        {
            float diameter=diameters[i]*1740/1670;
            float cx=centers[i].x*1740/1670,cy=centers[i].y*980/942;
            var maskRect=Rect("MedallionMask"+i,display,cx-diameter/2,cy-diameter/2,diameter,diameter);
            var image=maskRect.gameObject.AddComponent<Image>();image.sprite=aperture;image.raycastTarget=false;
            var mask=maskRect.gameObject.AddComponent<Mask>();mask.showMaskGraphic=false;
            var face=Rect("Artwork",maskRect,0,0,diameter,diameter).gameObject.AddComponent<Image>();
            face.raycastTarget=false;face.sprite=view.styleSleeves[i];face.color=Color.white;
            face.gameObject.AddComponent<AspectRatioFitter>();face.gameObject.AddComponent<JukeboxMedallionArtwork>().Refresh();view.medallions[i]=face;
        }
        var plateRect=Rect("Owner brass plaque",display,1408,173,320,40);
        plateRect.pivot=new Vector2(.5f,.5f);plateRect.anchoredPosition=new Vector2(1408,-173);
        var plate=plateRect.gameObject.AddComponent<JukeboxPlaqueSurface>();
        plate.artwork=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"OwnerPlaqueBlank.png");
        plate.pixelRect=new Rect(38,224,2096,276);plate.endFraction=.16f;plate.raycastTarget=false;plate.color=Color.white;
        var label=Rect("Engraved composer name",plateRect,0,0,240,36).gameObject.AddComponent<TextMeshProUGUI>();
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/CollectionNames/CollectionName SDF.asset");
        label.font=font;label.fontSharedMaterial=EngravingMaterial(font);label.fontStyle=FontStyles.Normal;label.fontWeight=FontWeight.Regular;
        label.alignment=TextAlignmentOptions.Center;label.characterSpacing=1;label.color=new Color(.18f,.095f,.035f);
        label.raycastTarget=false;label.margin=Vector4.zero;label.overflowMode=TextOverflowModes.Overflow;
        var owner=display.gameObject.AddComponent<JukeboxOwnerPlaque>();owner.plate=plate;owner.nameText=label;owner.plateHeight=40;
        view.ownerPlaque=owner;owner.SetOwnerName("Mozart");
        var paperRoot=Rect("PaperSpillRoot",view.cabinetContent,0,0,1740,980);
        var paperTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"ScatteredScoresV7.png");
        // Tight source regions preserve the approved registration without drawing a full-screen transparent quad.
        foreach(var region in new[]{new Rect(0,590,176,185),new Rect(1515,629,157,176),new Rect(100,770,1370,171)})
            Raw(paperRoot,"Manuscript region",paperTexture,region,-90+region.x*1920/paperTexture.width,-50+region.y*1080/paperTexture.height,region.width*1920/paperTexture.width,region.height*1080/paperTexture.height);
        paperRoot.SetAsLastSibling();
    }
    static Material EngravingMaterial(TMP_FontAsset font)
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null){material=new Material(font.material);AssetDatabase.CreateAsset(material,MaterialPath);}
        material.name="Jukebox Owner Engraved Brass";material.EnableKeyword("UNDERLAY_ON");
        material.SetColor("_UnderlayColor",new Color(1,.85f,.48f,.8f));material.SetFloat("_UnderlayOffsetX",0);
        material.SetFloat("_UnderlayOffsetY",-.3f);material.SetFloat("_UnderlaySoftness",0);material.SetFloat("_FaceDilate",-.02f);
        EditorUtility.SetDirty(material);return material;
    }
    static RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);
        r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);return r;
    }
    static RawImage Raw(Transform parent,string name,Texture2D texture,Rect pixels,float x,float y,float width,float height)
    {
        var image=Rect(name,parent,x,y,width,height).gameObject.AddComponent<RawImage>();image.texture=texture;image.raycastTarget=false;
        image.uvRect=new Rect(pixels.x/texture.width,1-pixels.yMax/texture.height,pixels.width/texture.width,pixels.height/texture.height);return image;
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void ValidateAndCapture()
    {
        var live=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<JukeboxView>(Prefab));var notes=new System.Collections.Generic.List<string>();
        try
        {
            var owner=live.ownerPlaque;Check(owner!=null,"Owner plaque missing");float cap=owner.plate.EndWidth;
            foreach(string name in new[]{"Bach","Mozart","Beethoven","Tchaikovsky","Pachelbel","Johann Sebastian Bach"})
            {
                owner.SetOwnerName(name);owner.nameText.ForceMeshUpdate(true,true);
                Check(Mathf.Abs(owner.plate.EndWidth-cap)<.01f,"Rivets stretch with name");
                Check(owner.plate.rectTransform.rect.width>=owner.minimumWidth&&owner.plate.rectTransform.rect.width<=owner.maximumWidth,"Plate outside width limits");
                Check(!owner.nameText.isTextOverflowing,"Owner text overflows: "+name);
                notes.Add(name+": "+owner.plate.rectTransform.rect.width+" wide, end cap "+cap);
            }
            var roster=Resources.Load<AppConfigSO>("AppConfig")?.opponentRoster;
            if(roster?.opponents?.Count(o=>o!=null)>1)
                for(int i=0;i<20;i++){string previous=owner.OwnerId;owner.ChooseOwner(roster);Check(owner.OwnerId!=previous,"Owner repeated on next visit");}
            Check(live.medallions.Length==4,"Wrong medallion count");
            foreach(var face in live.medallions)
            {
                var r=(RectTransform)face.transform.parent;
                Check(Mathf.Abs(r.rect.width-r.rect.height)<.01f,"Medallion mask is not circular");
                Check(r.GetComponent<Mask>()!=null&&!face.raycastTarget,"Mask missing or blocking controls");
            }
            // A non-square cover must fill the circle and crop evenly about its centre.
            var first=live.medallions[0];var original=first.sprite;
            var texture=new Texture2D(30,20);var wide=Sprite.Create(texture,new UnityEngine.Rect(0,0,30,20),Vector2.one*.5f);
            try
            {
                first.sprite=wide;first.GetComponent<JukeboxMedallionArtwork>().Refresh();
                var rect=first.rectTransform;var parent=(RectTransform)rect.parent;
                Check(Mathf.Abs(rect.rect.width/rect.rect.height-1.5f)<.01f,"Non-square sleeve is stretched");
                Check(Vector3.Distance(rect.TransformPoint(rect.rect.center),parent.TransformPoint(parent.rect.center))<.01f,"Cover crop is not centred");
            }
            finally{first.sprite=original;first.GetComponent<JukeboxMedallionArtwork>().Refresh();UnityEngine.Object.DestroyImmediate(wide);UnityEngine.Object.DestroyImmediate(texture);}
            live.ConfigurePlaybackHeader();
            Check(((RectTransform)live.cabinetContent.Find("OwnerDisplayRoot")).anchoredPosition==Vector2.zero,"Owner stand moved with the controls");
        }
        finally{UnityEngine.Object.DestroyImmediate(live.gameObject);}
        try
        {
            foreach(string name in new[]{"Bach","Mozart","Tchaikovsky"})
            {
                TitleScreensReview.OwnerReviewName=name;TitleScreensReview.OutputOverride="../VisualReviews/JukeboxOwner/"+name;
                TitleScreensReview.CaptureAcceptedJukebox();
            }
            TitleScreensReview.OwnerReviewName="Tchaikovsky";TitleScreensReview.OutputOverride="../VisualReviews/JukeboxOwner/WidePhone";
            TitleScreensReview.CaptureResolution=new Vector2Int(2400,1080);TitleScreensReview.CaptureAcceptedJukebox();
        }
        finally{TitleScreensReview.OwnerReviewName=null;TitleScreensReview.OutputOverride=null;TitleScreensReview.CaptureResolution=new Vector2Int(1920,1080);}
        File.WriteAllText("../jukebox-owner-validation.txt","PASS: random owner on entry, no immediate repeats; separate engraved name; fixed endcaps/rivets; bounded adaptive plate; four circular masks with cover cropping; single cabinet texture with integrated medallion stand; selected paper bank integrated.\n"+string.Join("\n",notes));
    }
}
#endif
