#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class CollectionCardSetup
{
    public const string Art = "Assets/_Project/Art/UI/Collection/";
    public const string CardPath = "Assets/_Project/Prefabs/UI/Collection/RivalCard.prefab";
    public const string ScreenPath = "Assets/_Project/Prefabs/UI/Collection/RivalCollectionScreen.prefab";
    public static readonly Vector2 CardSize = new(208,312);

    [InitializeOnLoadMethod]
    private static void CheckRequest()
    {
        EditorApplication.playModeStateChanged -= AfterPlay;
        EditorApplication.playModeStateChanged += AfterPlay;
        EditorApplication.update -= PollRequest;
        EditorApplication.update += PollRequest;
    }

    private static void PollRequest()
    {
        const string request = "Temp/CollectionCardSetup.request";
        if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try { File.Delete(request); }
            catch (IOException) { return; } // A request writer may still hold the file during this editor tick.
        try { Install(); CollectionCardValidation.Verify(); }
        catch (Exception e) { File.WriteAllText("../collection-card-validation.txt", "FAIL: " + e); Debug.LogException(e); }
    }

    private static void AfterPlay(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredEditMode) CheckRequest(); }

    [MenuItem("Tools/MusicGame/UI/Setup Composer Collection Cards")]
    public static void Install()
    {
        LocalizationSetup.AddMissingEntries(LocalizationSetup.Entries.Where(e=>e.key=="Rivals.CurrentVersion"||e.key.EndsWith(".Dates")).Select(e=>e.key));
        Import("ComposerCard02"); Import("ComposerNameplate02");
        ImportNameFont();
        Patch(CardPath, ConfigureCard);
        Patch(ScreenPath, ConfigureScreen);
        Patch("Assets/_Project/Prefabs/UI/Jukebox.prefab", ConfigureJukeboxBack);
        AssetDatabase.SaveAssets();
    }

    public static void InstallAndVerify() { Install(); CollectionCardValidation.Verify(); }

    private static void ImportNameFont()
    {
        const string folder="Assets/_Project/UI/Fonts/CollectionNames/";
        const string path=folder+"CollectionName SDF.asset";
        if(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path)!=null)return;
        AssetDatabase.ImportAsset(folder+"LibreBaskerville-Bold.ttf",ImportAssetOptions.ForceSynchronousImport);
        var font=AssetDatabase.LoadAssetAtPath<Font>(folder+"LibreBaskerville-Bold.ttf");
        if(font==null)return;
        var asset=TMP_FontAsset.CreateFontAsset(font,64,7,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
        asset.name="CollectionName SDF";AssetDatabase.CreateAsset(asset,path);
        asset.TryAddCharacters(string.Concat(Enumerable.Range(32,224).Select(i=>(char)i)),out string missing);
        foreach(var texture in asset.atlasTextures) {texture.name="CollectionName Atlas";AssetDatabase.AddObjectToAsset(texture,asset);}
        asset.material.name="CollectionName Material";AssetDatabase.AddObjectToAsset(asset.material,asset);
        EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
    }

    private static void Patch(string path, Action<GameObject> change)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try { change(root); PrefabUtility.SaveAsPrefabAsset(root,path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void Import(string name)
    {
        string path = Art + name + ".png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        // Crop transparent generation padding in the sprite's UV rectangle, leaving the PNG untouched.
        var inspect = new Texture2D(2,2,TextureFormat.RGBA32,false);
        inspect.LoadImage(File.ReadAllBytes(path));
        var pixels = inspect.GetPixels32();
        int minX=inspect.width,minY=inspect.height,maxX=0,maxY=0;
        for (int y=0;y<inspect.height;y++) for(int x=0;x<inspect.width;x++)
            if(pixels[y*inspect.width+x].a>=128) { minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y); }
        minX=Math.Max(0,minX-2);minY=Math.Max(0,minY-2);maxX=Math.Min(inspect.width-1,maxX+2);maxY=Math.Min(inspect.height-1,maxY+2);
        UnityEngine.Object.DestroyImmediate(inspect);
        importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Multiple;
        importer.alphaIsTransparency=true;
        importer.mipmapEnabled=false;
        importer.maxTextureSize=2048;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
        settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);
#pragma warning disable 0618
        importer.spritesheet=new[]{new SpriteMetaData {name=name,rect=new Rect(minX,minY,maxX-minX+1,maxY-minY+1),pivot=new Vector2(.5f,.5f),alignment=(int)SpriteAlignment.Center}};
#pragma warning restore 0618
        importer.SaveAndReimport();
    }

    private static Sprite Sprite(string name) => AssetDatabase.LoadAllAssetsAtPath(Art+name+".png").OfType<Sprite>().FirstOrDefault();

    public static void ConfigureCard(GameObject root)
    {
        var background=Sprite("ComposerCard02");var label=Sprite("ComposerNameplate02");
        if(background==null||label==null)return;
        var view=root.GetComponent<RivalCardView>();
        var rt=(RectTransform)root.transform;rt.sizeDelta=CardSize;
        var image=root.GetComponent<Image>()??root.AddComponent<Image>();
        image.sprite=background;image.color=Color.white;image.type=Image.Type.Simple;image.preserveAspect=false;
        view.button.targetGraphic=image;
        var colors=ColorBlock.defaultColorBlock;colors.highlightedColor=new Color(1f,.94f,.82f);colors.pressedColor=new Color(.78f,.72f,.60f);colors.selectedColor=colors.highlightedColor;colors.disabledColor=Color.white;view.button.colors=colors;
        var window=Rect(root.transform,"PortraitWindow");Box(window,13,15,182,246);
        if(window.GetComponent<RectMask2D>()==null)window.gameObject.AddComponent<RectMask2D>();
        view.portrait.transform.SetParent(window,false);UIFactory.Stretch(view.portrait.rectTransform);
        view.portrait.raycastTarget=false;view.portrait.color=Color.white;view.portrait.preserveAspect=false;
        var fit=view.portrait.GetComponent<AspectRatioFitter>()??view.portrait.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio=view.portrait.sprite!=null?view.portrait.sprite.rect.width/view.portrait.sprite.rect.height:1f;
        view.silhouetteColor=Color.black;
        var plate=Rect(root.transform,"Nameplate");Box(plate,17,242,174,34);
        view.namePlate=plate.GetComponent<Image>()??plate.gameObject.AddComponent<Image>();
        view.namePlate.sprite=label;view.namePlate.color=Color.white;view.namePlate.raycastTarget=false;
        view.nameText.transform.SetParent(plate,false);UIFactory.Stretch(view.nameText.rectTransform);
        view.nameText.color=new Color(.08f,.055f,.025f);view.nameText.alignment=TextAlignmentOptions.Center;
        var nameFont=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/CollectionNames/CollectionName SDF.asset");
        if(nameFont!=null){view.nameText.font=nameFont;view.nameText.fontSharedMaterial=nameFont.material;}
        view.nameText.fontStyle=FontStyles.Italic;view.nameText.fontSizeMax=21;view.nameText.fontSizeMin=12;view.nameText.enableAutoSizing=true;
        view.nameText.textWrappingMode=TextWrappingModes.NoWrap;view.nameText.margin=new Vector4(5,1,5,1);view.nameText.raycastTarget=false;
        if(view.footer!=null)
        {
            Box(view.footer.rectTransform,0,-30,208,26);
            view.footer.alignment=TextAlignmentOptions.Center;view.footer.fontSize=16;
            view.footer.color=new Color(1f,.82f,.43f);view.footer.raycastTarget=false;
            view.footer.gameObject.SetActive(false);
        }
        var star=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/UI/Star.png");
        var obsoleteStar=root.transform.Find("VersionStar5");
        if(obsoleteStar!=null)UnityEngine.Object.DestroyImmediate(obsoleteStar.gameObject);
        view.versionStars=new Image[4];
        for(int i=0;i<4;i++)
        {
            var back=Rect(root.transform,"VersionStar"+(i+1));Box(back,22+i*24,282,17,17);
            var outline=back.GetComponent<Image>()??back.gameObject.AddComponent<Image>();outline.sprite=star;outline.color=new Color(.025f,.018f,.008f);outline.raycastTarget=false;
            var edge=back.GetComponent<Outline>()??back.gameObject.AddComponent<Outline>();edge.effectColor=new Color(1f,.72f,.24f,.8f);edge.effectDistance=new Vector2(.65f,.65f);
            var fill=Rect(back,"Fill");UIFactory.Stretch(fill);
            var fillImage=fill.GetComponent<Image>()??fill.gameObject.AddComponent<Image>();fillImage.sprite=star;fillImage.color=view.starColor;fillImage.raycastTarget=false;fillImage.enabled=false;view.versionStars[i]=fillImage;
        }
        view.SetCollection(null,"",Array.Empty<int>());
    }

    public static void ConfigureScreen(GameObject root)
    {
        var view=root.GetComponent<RivalCollectionView>();if(view==null)return;
        var grid=view.grid.GetComponent<GridLayoutGroup>();grid.cellSize=CardSize;grid.spacing=new Vector2(20,22);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=6;grid.childAlignment=TextAnchor.UpperCenter;
        UIFactory.SetBox(view.grid,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-24),new Vector2(1348,646));
        view.detailRow.sizeDelta=new Vector2(1140,312);view.detailRow.localScale=new Vector3(.9f,.9f,1f);
        ConfigureDetail(view);
        FrontendBackdropSetup.Configure(root);
        var background=root.GetComponent<FrontendStyleBackground>();
        var group=root.GetComponent<StyleBlendGroup>()??root.AddComponent<StyleBlendGroup>();group.library=background.library;background.controls=group;
        foreach(var button in new[]{view.backButton})
            if(button!=null)TempoFuryUISetup.SkinButton(button,button.GetComponentInChildren<TextMeshProUGUI>(true),background.library);
        ((RectTransform)view.backButton.transform).sizeDelta=new Vector2(320,54);
        view.ApplyDetailStyle(background.HomeStyle);
    }

    private static void ConfigureDetail(RivalCollectionView view)
    {
        view.galleryInteraction = view.grid.GetComponent<CanvasGroup>();
        if(view.galleryInteraction == null) view.galleryInteraction = view.grid.gameObject.AddComponent<CanvasGroup>();
        var dim = view.detailRoot.Find("DetailDim").GetComponent<Image>();
        dim.color = new Color(0,0,0,.45f); dim.raycastTarget = true;
        view.detailPanel = view.detailRoot.Find("Panel") as RectTransform;
        var panel = view.detailPanel;
        foreach (var receiver in panel.GetComponents<ThemeColorReceiver>()) UnityEngine.Object.DestroyImmediate(receiver);
        panel.GetComponent<Image>().sprite = null;
        panel.GetComponent<Image>().color = new Color(.97f,.96f,.93f,1f);
        if(panel.GetComponent<RectMask2D>()==null)panel.gameObject.AddComponent<RectMask2D>();
        view.detailBands=new Image[2];
        for(int i=0;i<2;i++)
        {
            var band=Rect(panel,"StyleBand"+i);Box(band,-150,440+i*110,1580,180);
            band.localRotation=Quaternion.Euler(0,0, i==0?7:7);
            var fill=band.GetComponent<Image>();if(fill==null)fill=band.gameObject.AddComponent<Image>();
            fill.raycastTarget=false;view.detailBands[i]=fill;band.SetAsFirstSibling();
        }
        var accent=Rect(panel,"StyleAccent");Box(accent,50,45,5,60);
        view.detailAccent=accent.GetComponent<Image>();if(view.detailAccent==null)view.detailAccent=accent.gameObject.AddComponent<Image>();
        view.detailAccent.raycastTarget=false;
        UIFactory.SetBox(panel, Vector2.one*.5f, Vector2.one*.5f, Vector2.one*.5f, Vector2.zero, new Vector2(1240,790));
        var gold = new Color(.50f,.44f,.33f,.4f);
        foreach(var edge in new[]{"Top","Bottom","Left","Right"})
        {
            var line=Rect(panel,"Frame"+edge);
            var image=line.GetComponent<Image>()??line.gameObject.AddComponent<Image>(); image.color=gold;image.raycastTarget=false;
            if(edge=="Top")Box(line,14,14,1212,1);
            if(edge=="Bottom")Box(line,14,775,1212,1);
            if(edge=="Left")Box(line,14,14,1,762);
            if(edge=="Right")Box(line,1225,14,1,762);
        }
        foreach(var text in new[]{view.detailTitle,view.detailBio})
            foreach(var receiver in text.GetComponents<ThemeTextReceiver>())UnityEngine.Object.DestroyImmediate(receiver);
        Box(view.detailTitle.rectTransform,75,40,1020,68);
        view.detailTitle.alignment=TextAlignmentOptions.Left;
        view.detailTitle.fontSize=48; view.detailTitle.enableAutoSizing=true;view.detailTitle.fontSizeMin=30;view.detailTitle.fontSizeMax=48;
        view.detailTitle.color=new Color(.12f,.12f,.14f);view.detailTitle.raycastTarget=false;
        Box(view.detailBio.rectTransform,75,139,1090,132);
        view.detailBio.fontSize=22;view.detailBio.enableAutoSizing=true;view.detailBio.fontSizeMin=18;view.detailBio.fontSizeMax=22;
        view.detailBio.alignment=TextAlignmentOptions.TopLeft;view.detailBio.color=new Color(.24f,.24f,.26f);view.detailBio.raycastTarget=false;
        Box(view.detailRow,50,301,1140,312);view.detailRow.localScale=Vector3.one;
        var row=view.detailRow.GetComponent<HorizontalLayoutGroup>();row.spacing=28;row.childAlignment=TextAnchor.MiddleCenter;
        var close=view.detailClose;
        var skin=close.GetComponent<StyleBlendButton>();
        if(skin!=null)UnityEngine.Object.DestroyImmediate(skin);
        foreach(var layerName in new[]{"ClassicLayer","StyleLayer"})
        {var layer=close.transform.Find(layerName);if(layer!=null)UnityEngine.Object.DestroyImmediate(layer.gameObject);}
        // Clear every old art layer regardless of its generated name; keep only the caption.
        foreach(var image in close.GetComponentsInChildren<Image>(true))
            if(image!=close.image)UnityEngine.Object.DestroyImmediate(image.gameObject);
        foreach(var receiver in close.GetComponents<ThemeElementReceiver>())UnityEngine.Object.DestroyImmediate(receiver);
        var caption=close.GetComponentInChildren<TextMeshProUGUI>(true);
        foreach(var localized in caption.GetComponents<LocalizedText>())UnityEngine.Object.DestroyImmediate(localized);
        foreach(var receiver in caption.GetComponents<ThemeElementReceiver>())UnityEngine.Object.DestroyImmediate(receiver);
        caption.text="×";caption.fontStyle=FontStyles.Normal;caption.fontSize=38;caption.enableAutoSizing=false;
        caption.color=new Color(.2f,.2f,.22f);caption.margin=Vector4.zero;caption.characterSpacing=0;caption.alignment=TextAlignmentOptions.Center;
        close.image.sprite=null;close.image.color=new Color(.86f,.85f,.81f,1f);close.transition=Selectable.Transition.ColorTint;
        var feedback=ColorBlock.defaultColorBlock;feedback.highlightedColor=new Color(.82f,.82f,.82f);feedback.pressedColor=new Color(.65f,.65f,.65f);close.colors=feedback;
        Box((RectTransform)close.transform,1140,40,60,60);
        view.SetDetailVisible(false);
    }

    public static void ConfigureJukeboxBack(GameObject root)
    {
        var view=root.GetComponent<JukeboxView>();if(view==null||view.back==null)return;
        var button=view.back;
        foreach(var receiver in button.GetComponents<ThemeElementReceiver>())UnityEngine.Object.DestroyImmediate(receiver);
        const string transport="Assets/_Project/Art/UI/Jukebox/TransportCenter.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(transport);
        var border=new Vector4(180,26,180,26);
        if(importer.spriteBorder!=border){importer.spriteBorder=border;importer.SaveAndReimport();}
        button.image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(transport);
        button.image.type=Image.Type.Sliced;button.image.pixelsPerUnitMultiplier=2.7f;button.image.color=Color.white;button.transition=Selectable.Transition.ColorTint;
        var colors=button.colors;colors.normalColor=Color.white;button.colors=colors;JukeboxRowView.ApplyButtonFeedback(button);
        var label=button.GetComponentInChildren<TextMeshProUGUI>(true);
        foreach(var receiver in label.GetComponents<ThemeElementReceiver>())UnityEngine.Object.DestroyImmediate(receiver);
        label.color=new Color(1f,.94f,.81f);label.margin=new Vector4(66,0,66,0);label.alignment=TextAlignmentOptions.Center;
    }

    private static RectTransform Rect(Transform parent,string name)
    {var found=parent.Find(name) as RectTransform;return found!=null?found:UIFactory.CreateRect(name,(RectTransform)parent);}
    private static void Box(RectTransform rect,float x,float y,float width,float height)
    {var tl=new Vector2(0,1);UIFactory.SetBox(rect,tl,tl,tl,new Vector2(x,-y),new Vector2(width,height));}
}
#endif
