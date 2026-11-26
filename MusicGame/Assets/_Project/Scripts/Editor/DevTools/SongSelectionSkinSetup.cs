#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.UI;

public static class SongSelectionSkinSetup
{
    public const string ScreenPath="Assets/_Project/Prefabs/UI/SongSelection.prefab";
    public const string RowPath="Assets/_Project/Prefabs/UI/Items/SongRow.prefab";
    public const string MaterialPath="Assets/_Project/Configs/UI/RectangularSongButton.mat";

    [InitializeOnLoadMethod]
    private static void Listen()
    {
        EditorApplication.update-=Poll;EditorApplication.update+=Poll;
    }
    private static void Poll()
    {
        string request=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/SongSelectionSkinSetup.request"));
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        try{File.Delete(request);}catch(IOException){return;}
        try{InstallAndVerify();}catch(Exception e){File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../song-selection-skin-validation.txt")),"FAIL: "+e);Debug.LogException(e);}
    }

    [MenuItem("Tools/MusicGame/UI/Setup Styled Song Selection")]
    public static void InstallAndVerify(){Install();SongHorizontalCatalogValidation.Verify();}

    public static void Install()
    {
        SongMetadataSetup.Sync();
        var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(AppearanceSetup.LibraryPath);
        lib.songCards.Clear();
        lib.songCardSprite=null;
        lib.songNormalInk=new Color(.025f,.035f,.055f);lib.songSelectedInk=Color.white;
        foreach(string name in new[]{"Acoustic","Catchy","Chill","Cinematic","Dance","Dark","Dreamy","Electronic","Energetic","Experimental","Funky","Groove","Heavy","Jazzy","Rock","Soulful"})
        {
            string path="Assets/_Project/Art/UI/SongCards/Styles/"+name+".png";
            if(name=="Dance"&&File.Exists("Assets/_Project/Art/UI/SongCards/Styles/Dance-selected-v2.png"))path="Assets/_Project/Art/UI/SongCards/Styles/Dance-selected-v2.png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            if(importer==null)throw new InvalidOperationException("Missing song card: "+name);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spriteBorder=Vector4.zero;importer.maxTextureSize=512;importer.mipmapEnabled=false;importer.isReadable=false;
            importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
            bool dark=new[]{"Energetic","Experimental","Groove"}.Contains(name);
            Color tint=name switch
            {
                "Catchy"=>Color.white,
                "Acoustic"=>new Color(.46f,.58f,.42f),
                "Chill"=>new Color(.36f,.58f,.63f),
                "Cinematic"=>new Color(.56f,.43f,.27f),
                "Funky"=>new Color(.50f,.27f,.42f),
                _=>Color.white
            };
            string normalPath="Assets/_Project/Art/UI/SongCards/Normal/"+name+".png";
            AssetDatabase.ImportAsset(normalPath,ImportAssetOptions.ForceSynchronousImport);
            var normalImporter=(TextureImporter)AssetImporter.GetAtPath(normalPath);
            if(normalImporter==null)throw new InvalidOperationException("Missing normal song card: "+name);
            normalImporter.textureType=TextureImporterType.Sprite;normalImporter.spriteImportMode=SpriteImportMode.Single;
            normalImporter.spriteBorder=Vector4.zero;normalImporter.maxTextureSize=512;normalImporter.mipmapEnabled=false;normalImporter.isReadable=false;
            normalImporter.textureCompression=TextureImporterCompression.Compressed;normalImporter.SaveAndReimport();
            lib.songCards.Add(new StyleSkinLibrarySO.SongCardSkin{style=(GameMusicStyle)Enum.Parse(typeof(GameMusicStyle),name),normal=AssetDatabase.LoadAssetAtPath<Sprite>(normalPath),sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path),darkInk=dark,selectedTint=tint});
        }
        AssetDatabase.ImportAsset("Assets/_Project/Shaders/RectangularStyleButton.shader",ImportAssetOptions.ForceSynchronousImport);
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null){material=new Material(Shader.Find("MusicGame/UI/RectangularStyleButton"));AssetDatabase.CreateAsset(material,MaterialPath);}
        lib.songRowMaterial=material;EditorUtility.SetDirty(lib);
        AssetDatabase.ImportAsset("Assets/_Project/Shaders/SongCardAppearance.shader",ImportAssetOptions.ForceSynchronousImport);
        const string cardMaterialPath="Assets/_Project/Configs/UI/SongCardAppearance.mat";
        var cardMaterial=AssetDatabase.LoadAssetAtPath<Material>(cardMaterialPath);
        if(cardMaterial==null){cardMaterial=new Material(Shader.Find("MusicGame/UI/SongCardAppearance"));AssetDatabase.CreateAsset(cardMaterial,cardMaterialPath);}
        lib.songCardAppearanceMaterial=cardMaterial;EditorUtility.SetDirty(lib);
        Patch(RowPath,ConfigureRow);Patch(ScreenPath,ConfigureScreen);
        Patch(RowPath,SongShowcaseSetup.ConfigureRow);Patch(ScreenPath,SongShowcaseSetup.ConfigureScreen);
        var collection=LocalizationEditorSettings.GetStringTableCollection("UIText");
        foreach(var table in collection.StringTables)
        {
            table.AddEntry("SongSelection.PlayYourSong",table.LocaleIdentifier.Code=="ca"?"PUNXA LA TEVA CANÇÓ":"PLAY YOUR SONG");EditorUtility.SetDirty(table);
            table.AddEntry("SongSelection.RemainingBelow",table.LocaleIdentifier.Code=="ca"?"{0} PER SOTA":"{0} BELOW");EditorUtility.SetDirty(table);
            bool ca=table.LocaleIdentifier.Code=="ca";
            table.AddEntry("SongSelection.AllDecades",ca?"TOTES":"ALL");
            table.AddEntry("SongSelection.EmptyDecade",ca?"Encara no hi ha cançons assignades a aquesta dècada":"No songs assigned to this decade yet");
            table.AddEntry("SongSelection.CatalogCount",ca?"{0} CANÇONS":"{0} SONGS");
            table.AddEntry("SongSelection.SelectedSong","<size=75%><b>{1}</b></size> <size=70%>—</size> <b>{0}</b>");
            table.AddEntry("SongSelection.Completed",ca?"SUPERADA":"COMPLETED");
            table.AddEntry("SongSelection.SelectedLocal","{0}");
            table.AddEntry("SongSelection.ChooseTrack",ca?"Selecciona una cançó":"Select a song");
            table.AddEntry("SongSelection.PreviewRemaining",ca?"PREVIEW · {0} / {1} · QUEDEN {2}":"PREVIEW · {0} / {1} · {2} LEFT");
            table.AddEntry("SongSelection.LoadingPreview",ca?"Carregant preview…":"Loading preview…");
            table.AddEntry("SongSelection.ImportOwn",ca?"+ IMPORTA LA TEVA CANÇÓ":"+ IMPORT YOUR SONG");
            table.AddEntry("SongSelection.UnlockOwn",ca?"+ CANÇÓ PRÒPIA · DESBLOQUEJA":"+ YOUR SONG · UNLOCK");
            table.AddEntry("SongSelection.CustomPackTitle","CUSTOM SONGS");
            table.AddEntry("SongSelection.CustomPackBody",ca?"Desbloqueja el pack Custom Songs per importar les teves cançons i jugar amb elles.":"Unlock the Custom Songs pack to import your own songs and play with them.");
            table.AddEntry("SongSelection.CustomPackBack",ca?"TORNAR":"BACK");
        }
        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssets();
    }
    private static void Patch(string path,Action<GameObject> change)
    {
        var root=PrefabUtility.LoadPrefabContents(path);
        try{change(root);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    private static void RemoveReceivers(GameObject go)
    {foreach(var receiver in go.GetComponents<ThemeElementReceiver>())UnityEngine.Object.DestroyImmediate(receiver);}

    private static RectangularStyleButton Rectangle(Button button,TextMeshProUGUI label)
    {
        var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(AppearanceSetup.LibraryPath);
        RemoveReceivers(button.gameObject);RemoveReceivers(label.gameObject);
        var skin=button.GetComponent<RectangularStyleButton>();if(skin==null)skin=button.gameObject.AddComponent<RectangularStyleButton>();
        skin.library=lib;skin.button=button;skin.image=button.GetComponent<Image>();skin.label=label;
        skin.image.useSpriteMesh=false;skin.image.type=Image.Type.Simple;skin.image.material=lib.songRowMaterial;
        if(button.GetComponent<RectangularButtonMesh>()==null)button.gameObject.AddComponent<RectangularButtonMesh>();
        button.transition=Selectable.Transition.None;button.targetGraphic=skin.image;
        label.raycastTarget=false;skin.SetSelection(false,GameMusicStyle.Acoustic);return skin;
    }

    public static void ConfigureRow(GameObject root)
    {
        var view=root.GetComponent<SongRowView>();if(view==null)return;
        ((RectTransform)root.transform).sizeDelta=new Vector2(1080,82);
        view.skin=Rectangle(view.button,view.label);
        var label=view.label;label.fontSize=24;label.enableAutoSizing=true;label.fontSizeMin=18;label.fontSizeMax=24;
        label.fontStyle=FontStyles.Normal;label.alignment=TextAlignmentOptions.MidlineLeft;label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;
        label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.offsetMin=new Vector2(30,8);label.rectTransform.offsetMax=new Vector2(-242,-8);
        var right=new Vector2(1,.5f);
        UIFactory.SetBox(view.playedCheck.rectTransform,right,right,right,new Vector2(-192,0),new Vector2(28,28));view.playedCheck.raycastTarget=false;
        var preview=view.playPauseButton;RemoveReceivers(preview.gameObject);RemoveReceivers(view.playPauseLabel.gameObject);
        UIFactory.SetBox((RectTransform)preview.transform,right,right,right,new Vector2(-20,0),new Vector2(148,52));
        preview.image.sprite=null;preview.transition=Selectable.Transition.ColorTint;
        var colors=ColorBlock.defaultColorBlock;colors.highlightedColor=new Color(1.13f,1.13f,1.13f);colors.pressedColor=new Color(.8f,.8f,.8f);colors.disabledColor=new Color(.6f,.6f,.6f);preview.colors=colors;
        var text=view.playPauseLabel;text.fontSize=19;text.enableAutoSizing=true;text.fontSizeMin=15;text.fontSizeMax=19;text.alignment=TextAlignmentOptions.Center;text.fontStyle=FontStyles.Bold;UIFactory.Stretch(text.rectTransform);text.margin=new Vector4(5,0,5,0);
        view.SetSelection(false,GameMusicStyle.Acoustic);
        ((RectTransform)root.transform).sizeDelta=new Vector2(500,198);
        label.fontSize=29;label.fontSizeMin=23;label.fontSizeMax=29;label.fontStyle=FontStyles.Bold;
        UIFactory.SetBox(label.rectTransform,new Vector2(0,1),Vector2.one,new Vector2(.5f,1),new Vector2(0,-18),new Vector2(-44,46));
        label.margin=new Vector4(0,0,48,0);
        view.artistLabel=CardLabel(root.transform,"Artist",label,view.artistLabel,72,34,23);
        view.genreLabel=CardLabel(root.transform,"Genre",label,view.genreLabel,132,34,22);
        view.genreLabel.fontStyle=FontStyles.Italic;
        UIFactory.SetBox((RectTransform)preview.transform,Vector2.one,Vector2.one,Vector2.one,new Vector2(-20,-128),new Vector2(140,48));
        view.genreLabel.rectTransform.offsetMax=new Vector2(-182,view.genreLabel.rectTransform.offsetMax.y);
        UIFactory.SetBox(view.playedCheck.rectTransform,Vector2.one,Vector2.one,Vector2.one,new Vector2(-14,-10),new Vector2(18,18));
        view.skin.songCard=true;
        if(root.GetComponent<SongCardSurfaceMesh>()==null)root.AddComponent<SongCardSurfaceMesh>();
        var appearance=root.GetComponent<SongCardAppearance>();if(appearance==null)appearance=root.AddComponent<SongCardAppearance>();
        appearance.library=view.skin.library;appearance.image=view.background;appearance.label=view.label;
        var mesh=root.GetComponent<RectangularButtonMesh>();if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
        var outline=root.GetComponent<Outline>();if(outline==null)outline=root.AddComponent<Outline>();outline.effectDistance=new Vector2(2,2);outline.enabled=false;
        label.fontStyle=FontStyles.Bold;label.fontSize=32;label.fontSizeMin=26;label.fontSizeMax=32;label.characterSpacing=0;
        view.artistLabel.fontSize=25;view.artistLabel.fontStyle=FontStyles.Bold;
        var watermark=root.transform.Find("StyleWatermark") as RectTransform;if(watermark==null)watermark=UIFactory.CreateRect("StyleWatermark",(RectTransform)root.transform);
        UIFactory.Stretch(watermark);watermark.offsetMin=new Vector2(8,8);watermark.offsetMax=new Vector2(-8,-8);watermark.SetAsFirstSibling();
        view.styleWatermark=watermark.GetComponent<RawImage>();if(view.styleWatermark==null)view.styleWatermark=watermark.gameObject.AddComponent<RawImage>();view.styleWatermark.raycastTarget=false;
        var edge=root.transform.Find("StyleEdge") as RectTransform;if(edge==null)edge=UIFactory.CreateRect("StyleEdge",(RectTransform)root.transform);
        edge.anchorMin=Vector2.zero;edge.anchorMax=new Vector2(0,1);edge.offsetMin=new Vector2(10,14);edge.offsetMax=new Vector2(15,-14);
        view.styleEdge=edge.GetComponent<Image>();if(view.styleEdge==null)view.styleEdge=edge.gameObject.AddComponent<Image>();view.styleEdge.raycastTarget=false;
        var badge=root.transform.Find("GenreBadge") as RectTransform;if(badge==null)badge=UIFactory.CreateRect("GenreBadge",(RectTransform)root.transform);
        UIFactory.SetBox(badge,new Vector2(0,1),new Vector2(0,1),new Vector2(0,1),new Vector2(24,-130),new Vector2(188,42));
        view.genreBadge=badge.GetComponent<Image>();if(view.genreBadge==null)view.genreBadge=badge.gameObject.AddComponent<Image>();view.genreBadge.raycastTarget=false;
        view.genreLabel.transform.SetParent(badge,false);UIFactory.Stretch(view.genreLabel.rectTransform);
        view.genreBadge.enabled=false;view.genreLabel.fontStyle=FontStyles.Normal;view.genreLabel.fontSize=21;view.genreLabel.alignment=TextAlignmentOptions.MidlineLeft;
        view.styleWatermark.gameObject.SetActive(false);view.styleEdge.gameObject.SetActive(false);
        var rowGroup=root.GetComponent<CanvasGroup>();if(rowGroup==null)rowGroup=root.AddComponent<CanvasGroup>();
        var frame=root.transform.Find("SelectionFrame") as RectTransform;if(frame==null)frame=UIFactory.CreateRect("SelectionFrame",(RectTransform)root.transform);
        UIFactory.Stretch(frame);frame.offsetMin=new Vector2(-6,-6);frame.offsetMax=new Vector2(6,6);frame.SetAsLastSibling();
        Edge(frame,"Top",new Vector2(0,1),Vector2.one,new Vector2(0,-12),Vector2.zero);
        Edge(frame,"Bottom",Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,12));
        Edge(frame,"Left",Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(12,0));
        Edge(frame,"Right",new Vector2(1,0),Vector2.one,new Vector2(-12,0),Vector2.zero);
        view.selectionFrame=frame.gameObject;
        var playedRoot=root.transform.Find("PlayedBadge") as RectTransform;if(playedRoot==null)playedRoot=UIFactory.CreateRect("PlayedBadge",(RectTransform)root.transform);
        UIFactory.SetBox(playedRoot,Vector2.one,Vector2.one,Vector2.one,new Vector2(-12,-12),new Vector2(38,38));
        view.playedBadge=playedRoot.GetComponent<Image>();if(view.playedBadge==null)view.playedBadge=playedRoot.gameObject.AddComponent<Image>();
        view.playedBadge.color=new Color(.025f,.035f,.055f,.9f);view.playedBadge.raycastTarget=false;
        var checkGroup=playedRoot.GetComponent<CanvasGroup>();if(checkGroup==null)checkGroup=playedRoot.gameObject.AddComponent<CanvasGroup>();checkGroup.ignoreParentGroups=true;checkGroup.blocksRaycasts=false;
        view.playedCheck.transform.SetParent(playedRoot,false);UIFactory.Stretch(view.playedCheck.rectTransform);view.playedCheck.rectTransform.offsetMin=Vector2.one*6;view.playedCheck.rectTransform.offsetMax=Vector2.one*-6;
        // A quiet style caption above the song, and a full-width Preview footer.
        UIFactory.SetBox(badge,Vector2.one,Vector2.one,Vector2.one,new Vector2(-20,-12),new Vector2(220,28));
        view.genreLabel.alignment=TextAlignmentOptions.MidlineRight;
        var topLeft=new Vector2(0,1);
        UIFactory.SetBox(playedRoot,topLeft,topLeft,topLeft,new Vector2(12,-10),new Vector2(32,32));
        UIFactory.SetBox(label.rectTransform,topLeft,Vector2.one,new Vector2(.5f,1),new Vector2(0,-46),new Vector2(-44,42));
        label.margin=Vector4.zero;
        UIFactory.SetBox(view.artistLabel.rectTransform,topLeft,Vector2.one,new Vector2(.5f,1),new Vector2(0,-90),new Vector2(-44,34));
        var footer=(RectTransform)preview.transform;
        footer.anchorMin=Vector2.zero;footer.anchorMax=new Vector2(1,0);footer.pivot=new Vector2(.5f,0);
        footer.offsetMin=new Vector2(10,8);footer.offsetMax=new Vector2(-10,60);
        text.fontSize=23;text.fontSizeMin=19;text.fontSizeMax=23;
        // Keep the primary copy high in the card; history never reserves a row.
        UIFactory.SetBox(label.rectTransform,topLeft,Vector2.one,new Vector2(.5f,1),new Vector2(0,-30),new Vector2(-44,42));
        UIFactory.SetBox(view.artistLabel.rectTransform,topLeft,Vector2.one,new Vector2(.5f,1),new Vector2(0,-73),new Vector2(-44,32));
        view.genreLabel.fontSize=17;
        foreach(Transform child in frame)child.gameObject.SetActive(false);
        frame.offsetMin=new Vector2(-6,-6);frame.offsetMax=new Vector2(6,6);
        if(frame.GetComponent<CanvasRenderer>()==null)frame.gameObject.AddComponent<CanvasRenderer>();
        view.selectionRivet=frame.GetComponent<SongSelectionFrame>();if(view.selectionRivet==null)view.selectionRivet=frame.gameObject.AddComponent<SongSelectionFrame>();view.selectionRivet.raycastTarget=false;
        view.selectionRivet.simple=true;
        view.completedLabel=CardLabel(root.transform,"Completed",label,view.completedLabel,110,22,16);
        view.completedLabel.fontStyle=FontStyles.Bold;view.completedLabel.alignment=TextAlignmentOptions.MidlineRight;
        var eq=root.transform.Find("Equalizer") as RectTransform;
        if(eq==null)eq=preview.transform.Find("Equalizer") as RectTransform;
        if(eq==null)eq=UIFactory.CreateRect("Equalizer",(RectTransform)root.transform);
        eq.SetParent(root.transform,false);
        UIFactory.SetBox(eq,new Vector2(1,0),new Vector2(1,0),new Vector2(1,0),new Vector2(-22,18),new Vector2(32,28));
        view.equalizer=eq;view.equalizerBars=new RectTransform[4];
        for(int i=0;i<4;i++)
        {
            var bar=eq.Find("Bar"+i) as RectTransform;if(bar==null)bar=UIFactory.CreateRect("Bar"+i,eq);
            UIFactory.SetBox(bar,Vector2.zero,Vector2.zero,Vector2.zero,new Vector2(i*8,1),new Vector2(4,16));
            var ink=bar.GetComponent<Image>();if(ink==null)ink=bar.gameObject.AddComponent<Image>();ink.raycastTarget=false;view.equalizerBars[i]=bar;
        }
        eq.gameObject.SetActive(false);
        preview.gameObject.SetActive(false);
        label.fontSize=36;label.fontSizeMax=36;label.fontSizeMin=26;
        UIFactory.SetBox(label.rectTransform,topLeft,Vector2.one,new Vector2(.5f,1),new Vector2(0,-46),new Vector2(-44,48));
        view.artistLabel.fontSize=27;view.artistLabel.fontSizeMax=27;
        UIFactory.SetBox(view.artistLabel.rectTransform,topLeft,Vector2.one,new Vector2(.5f,1),new Vector2(0,-100),new Vector2(-44,36));
        view.completedLabel.rectTransform.anchorMin=view.completedLabel.rectTransform.anchorMax=view.completedLabel.rectTransform.pivot=new Vector2(0,0);
        view.completedLabel.rectTransform.anchoredPosition=new Vector2(22,18);view.completedLabel.rectTransform.sizeDelta=new Vector2(260,24);
        view.completedLabel.alignment=TextAlignmentOptions.MidlineLeft;
        view.SetSelection(false,GameMusicStyle.Acoustic);
    }
    private static void Edge(RectTransform parent,string name,Vector2 min,Vector2 max,Vector2 from,Vector2 to)
    {
        var rect=parent.Find(name) as RectTransform;if(rect==null)rect=UIFactory.CreateRect(name,parent);
        rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=from;rect.offsetMax=to;
        var image=rect.GetComponent<Image>();if(image==null)image=rect.gameObject.AddComponent<Image>();image.color=Color.white;image.raycastTarget=false;
        var outline=rect.GetComponent<Outline>();if(outline==null)outline=rect.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.015f,.02f,.035f,.9f);outline.effectDistance=new Vector2(1,-1);
    }
    private static TextMeshProUGUI CardLabel(Transform parent,string name,TextMeshProUGUI source,TextMeshProUGUI existing,float y,float height,float size)
    {
        var label=existing;
        if(label==null){var rect=UIFactory.CreateRect(name,(RectTransform)parent);label=rect.gameObject.AddComponent<TextMeshProUGUI>();}
        label.transform.SetParent(parent,false);
        label.font=source.font;label.raycastTarget=false;label.fontSize=size;label.fontStyle=FontStyles.Normal;label.alignment=TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;
        UIFactory.SetBox(label.rectTransform,new Vector2(0,1),Vector2.one,new Vector2(.5f,1),new Vector2(0,-y),new Vector2(-44,height));
        return label;
    }

    public static void ConfigureScreen(GameObject root)
    {
        var view=root.GetComponent<SongSelectionView>();if(view==null)return;
        FrontendBackdropSetup.Configure(root);
        var background=root.GetComponent<FrontendStyleBackground>();
        var content=root.transform.Find("SongSelectionContent") as RectTransform;
        if(content==null)content=UIFactory.CreateRect("SongSelectionContent",(RectTransform)root.transform);
        view.contentRoot=content;UIFactory.SetBox(content,Vector2.one*.5f,Vector2.one*.5f,Vector2.one*.5f,Vector2.zero,new Vector2(1920,1080));
        var scroll=view.songListRoot.GetComponentInParent<ScrollRect>(true);
        var streaming=view.spotifyButton.transform.parent as RectTransform;
        foreach(var item in new Transform[]{view.titleText.transform,scroll.transform,view.playYourSongButton.transform,streaming,view.backButton.transform,view.playButton.transform,view.statusText.transform})
            item.SetParent(content,false);
        Box(view.titleText.rectTransform,0,26,1660,125);
        var title=view.titleText.GetComponent<TitleText>();if(title!=null)title.Apply();
        Box((RectTransform)scroll.transform,0,418,1540,400);
        scroll.horizontal=false;scroll.vertical=true;scroll.scrollSensitivity=42;
        var panel=scroll.GetComponent<Image>();if(panel!=null)panel.color=Color.clear;
        var carousel=scroll.GetComponent<SongCatalogCarousel>();
        if(carousel==null)carousel=scroll.gameObject.AddComponent<SongCatalogCarousel>();
        var viewport=scroll.viewport;
        viewport.offsetMin=new Vector2(0,0);viewport.offsetMax=new Vector2(0,0);
        var mask=viewport.GetComponent<RectMask2D>();if(mask!=null){mask.softness=Vector2Int.zero;mask.padding=Vector4.zero;}
        carousel.decadeButtons=new Button[7];
        var tabs=content.Find("Decades") as RectTransform;if(tabs==null)tabs=UIFactory.CreateRect("Decades",content);
        Box(tabs,0,334,1540,62);
        var support=content.Find("CatalogSupport") as RectTransform;if(support==null)support=UIFactory.CreateRect("CatalogSupport",content);
        Box(support,0,321,1540,587);support.SetAsFirstSibling();
        view.catalogSupport=support.GetComponent<Image>();if(view.catalogSupport==null)view.catalogSupport=support.gameObject.AddComponent<Image>();view.catalogSupport.raycastTarget=false;
        var supportBorder=support.GetComponent<Outline>();if(supportBorder==null)supportBorder=support.gameObject.AddComponent<Outline>();supportBorder.effectDistance=new Vector2(1,-1);supportBorder.effectColor=new Color(.8f,.8f,.8f,.18f);
        view.catalogSupport.color=Color.clear;supportBorder.enabled=false;
        for(int i=0;i<7;i++)
        {
            var tab=tabs.Find("Decade"+i) as RectTransform;if(tab==null)tab=UIFactory.CreateRect("Decade"+i,tabs);
            UIFactory.SetBox(tab,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(i*220,0),new Vector2(220,62));
            if(tab.GetComponent<Image>()==null)tab.gameObject.AddComponent<Image>();
            var button=tab.GetComponent<Button>();if(button==null)button=tab.gameObject.AddComponent<Button>();
            var text=tab.GetComponentInChildren<TextMeshProUGUI>();if(text==null){var textRect=UIFactory.CreateRect("Label",tab);text=textRect.gameObject.AddComponent<TextMeshProUGUI>();}
            text.font=view.statusText.font;text.fontSize=28;text.fontStyle=FontStyles.Bold;text.fontWeight=FontWeight.Bold;text.characterSpacing=.7f;text.alignment=TextAlignmentOptions.Center;UIFactory.Stretch(text.rectTransform);
            var flat=tab.Find("StyleSurface");if(flat!=null)UnityEngine.Object.DestroyImmediate(flat.gameObject);
            Rectangle(button,text);button.GetComponent<RectangularButtonMesh>().diagonal=true;
            button.GetComponent<RectangularStyleButton>().invertWithAppearance=true;
            carousel.decadeButtons[i]=button;
        }
        var position=scroll.transform.Find("CatalogPosition") as RectTransform;
        if(position==null)position=UIFactory.CreateRect("CatalogPosition",(RectTransform)scroll.transform);
        var positionText=position.GetComponent<TextMeshProUGUI>();if(positionText==null)positionText=position.gameObject.AddComponent<TextMeshProUGUI>();
        positionText.font=view.statusText.font;positionText.fontSize=19;positionText.alignment=TextAlignmentOptions.Center;positionText.raycastTarget=false;
        UIFactory.SetBox(position,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,-22),new Vector2(1620,28));
        carousel.positionLabel=positionText;
        positionText.gameObject.SetActive(false);
        var track=scroll.transform.Find("CatalogTrack") as RectTransform;
        if(track==null)track=UIFactory.CreateRect("CatalogTrack",(RectTransform)scroll.transform);
        UIFactory.SetBox(track,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,-8),new Vector2(1540,4));
        var trackImage=track.GetComponent<Image>();if(trackImage==null)trackImage=track.gameObject.AddComponent<Image>();trackImage.raycastTarget=false;carousel.positionTrack=trackImage;
        var thumb=track.Find("Thumb") as RectTransform;if(thumb==null)thumb=UIFactory.CreateRect("Thumb",track);
        var thumbImage=thumb.GetComponent<Image>();if(thumbImage==null)thumbImage=thumb.gameObject.AddComponent<Image>();thumbImage.raycastTarget=false;carousel.positionThumb=thumb;
        var layout=view.songListRoot.GetComponent<VerticalLayoutGroup>();layout.spacing=8;layout.padding=new RectOffset(0,0,0,0);
        layout.childControlHeight=false;layout.childForceExpandHeight=false;layout.childControlWidth=true;layout.childForceExpandWidth=true;
        Box((RectTransform)view.playYourSongButton.transform,0,856,1540,52);
        var importSkin=view.playYourSongButton.GetComponent<RectangularStyleButton>();if(importSkin!=null)UnityEngine.Object.DestroyImmediate(importSkin);
        var importMesh=view.playYourSongButton.GetComponent<RectangularButtonMesh>();if(importMesh!=null)UnityEngine.Object.DestroyImmediate(importMesh);
        RemoveReceivers(view.playYourSongButton.gameObject);RemoveReceivers(view.playYourSongLabel.gameObject);
        var importLocalized=view.playYourSongLabel.GetComponent<LocalizedText>();if(importLocalized!=null)UnityEngine.Object.DestroyImmediate(importLocalized);
        view.playYourSongButton.image.sprite=null;view.playYourSongButton.image.material=null;view.playYourSongButton.image.type=Image.Type.Simple;
        var importOutline=view.playYourSongButton.GetComponent<Outline>();if(importOutline==null)importOutline=view.playYourSongButton.gameObject.AddComponent<Outline>();
        importOutline.effectDistance=new Vector2(1,-1);importOutline.effectColor=new Color(.55f,.58f,.64f,.7f);
        view.playYourSongLabel.fontSize=22;
        view.customSurface=Surface(view.playYourSongButton,view.playYourSongLabel,false);
        var selected=content.Find("SelectedCard") as RectTransform;if(selected==null)selected=UIFactory.CreateRect("SelectedCard",content);
        Box(selected,0,189,1540,112);
        view.selectedCard=selected.GetComponent<Image>();if(view.selectedCard==null)view.selectedCard=selected.gameObject.AddComponent<Image>();view.selectedCard.raycastTarget=false;
        var selectedOutline=selected.GetComponent<Outline>();if(selectedOutline==null)selectedOutline=selected.gameObject.AddComponent<Outline>();selectedOutline.effectDistance=new Vector2(1,-1);selectedOutline.effectColor=new Color(.9f,.87f,.78f,.65f);
        selectedOutline.enabled=false;view.selectedCard.color=Color.clear;
        var hero=selected.Find("Surface") as RectTransform;if(hero==null)hero=UIFactory.CreateRect("Surface",selected);UIFactory.Stretch(hero);
        if(hero.GetComponent<CanvasRenderer>()==null)hero.gameObject.AddComponent<CanvasRenderer>();
        var oldSurface=hero.GetComponent<SongStyleSurface>();if(oldSurface!=null)UnityEngine.Object.DestroyImmediate(oldSurface);view.selectedSurface=null;
        view.selectedTexture=hero.GetComponent<Image>();if(view.selectedTexture==null)view.selectedTexture=hero.gameObject.AddComponent<Image>();
        view.selectedTexture.material=null;view.selectedTexture.raycastTarget=false;view.selectedTexture.type=Image.Type.Simple;
        var oldMesh=hero.GetComponent<RectangularButtonMesh>();if(oldMesh!=null)UnityEngine.Object.DestroyImmediate(oldMesh);
        var heroMesh=hero.GetComponent<SongCardSurfaceMesh>();if(heroMesh==null)heroMesh=hero.gameObject.AddComponent<SongCardSurfaceMesh>();heroMesh.trimFrame=true;
        var rivet=selected.Find("Rivet") as RectTransform;if(rivet==null)rivet=UIFactory.CreateRect("Rivet",selected);UIFactory.Stretch(rivet);
        if(rivet.GetComponent<CanvasRenderer>()==null)rivet.gameObject.AddComponent<CanvasRenderer>();
        view.selectedRivet=rivet.GetComponent<SongSelectionFrame>();if(view.selectedRivet==null)view.selectedRivet=rivet.gameObject.AddComponent<SongSelectionFrame>();view.selectedRivet.hero=true;view.selectedRivet.raycastTarget=false;
        rivet.gameObject.SetActive(false);
        view.selectedSongText=ScreenText(content,"SelectedSong",view.statusText.font,180,80,62);view.selectedSongText.text="";
        Box(view.selectedSongText.rectTransform,0,204,1340,80);
        view.selectedSongText.enableAutoSizing=true;view.selectedSongText.fontSizeMin=36;view.selectedSongText.fontSizeMax=62;
        view.selectedSongText.fontStyle=FontStyles.Normal;
        view.selectedSongText.richText=true;view.selectedSongText.lineSpacing=2;
        view.selectedSongText.transform.SetAsLastSibling();
        view.selectedAppearance=hero.GetComponent<SongCardAppearance>();if(view.selectedAppearance==null)view.selectedAppearance=hero.gameObject.AddComponent<SongCardAppearance>();
        view.selectedAppearance.library=background.library;view.selectedAppearance.image=view.selectedTexture;view.selectedAppearance.label=view.selectedSongText;
        heroMesh.trimFrame=false;
        view.previewTimeText=ScreenText(content,"PreviewTime",view.statusText.font,813,24,21);
        view.previewTimeText.gameObject.SetActive(false);
        var progress=content.Find("PreviewProgress") as RectTransform;if(progress==null)progress=UIFactory.CreateRect("PreviewProgress",content);
        Box(progress,0,299,1540,2);var trackImagePreview=progress.GetComponent<Image>();if(trackImagePreview==null)trackImagePreview=progress.gameObject.AddComponent<Image>();
        trackImagePreview.color=new Color(.5f,.5f,.5f,.25f);trackImagePreview.raycastTarget=false;
        var fill=progress.Find("Remaining") as RectTransform;if(fill==null)fill=UIFactory.CreateRect("Remaining",progress);UIFactory.Stretch(fill);
        var fillImage=fill.GetComponent<Image>();if(fillImage==null)fillImage=fill.gameObject.AddComponent<Image>();fillImage.color=new Color(1,.91f,.59f);fillImage.raycastTarget=false;
        view.previewProgressRoot=progress.gameObject;view.previewProgressFill=fill;progress.gameObject.SetActive(false);
        progress.SetAsLastSibling();
        ConfigurePurchase(view,content);
        var group=root.GetComponent<StyleBlendGroup>();if(group==null)group=root.AddComponent<StyleBlendGroup>();group.library=background.library;background.controls=group;
        foreach(var button in new[]{view.backButton,view.playButton})
            TempoFuryUISetup.SkinButton(button,button.GetComponentInChildren<TextMeshProUGUI>(true),background.library);
        Bottom((RectTransform)view.backButton.transform,-240,48,280,60);Bottom((RectTransform)view.playButton.transform,160,40,440,76);
        var backGroup=view.backButton.GetComponent<CanvasGroup>();if(backGroup==null)backGroup=view.backButton.gameObject.AddComponent<CanvasGroup>();backGroup.alpha=1;
        view.playAvailability=view.playButton.GetComponent<CanvasGroup>();if(view.playAvailability==null)view.playAvailability=view.playButton.gameObject.AddComponent<CanvasGroup>();
        Box(view.statusText.rectTransform,0,303,1540,16);view.statusText.fontSize=16;
        Bottom(streaming,0,26,1080,30);
        streaming.gameObject.SetActive(false);
        var streamingButtons=new[]{view.spotifyButton,view.youtubeMusicButton,view.amazonMusicButton};
        for(int i=0;i<streamingButtons.Length;i++)
        {
            var button=streamingButtons[i];RemoveReceivers(button.gameObject);button.image.color=Color.clear;button.transition=Selectable.Transition.None;
            UIFactory.SetBox((RectTransform)button.transform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(i*360,0),new Vector2(360,30));
            var label=button.GetComponentInChildren<TextMeshProUGUI>(true);RemoveReceivers(label.gameObject);label.fontSize=16;label.enableAutoSizing=true;label.fontSizeMax=16;label.fontSizeMin=12;
            var ink=label.GetComponent<AppearanceText>();if(ink==null)ink=label.gameObject.AddComponent<AppearanceText>();ink.background=background;ink.night=new Color(.75f,.77f,.81f);ink.day=new Color(.26f,.29f,.34f);
        }
    }
    private static void Box(RectTransform rect,float x,float y,float width,float height)
    {var top=new Vector2(.5f,1);UIFactory.SetBox(rect,top,top,top,new Vector2(x,-y),new Vector2(width,height));}
    private static SongStyleSurface Surface(Button button,TextMeshProUGUI label,bool diagonal)
    {
        var skin=button.GetComponent<RectangularStyleButton>();if(skin!=null)UnityEngine.Object.DestroyImmediate(skin);
        var mesh=button.GetComponent<RectangularButtonMesh>();if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
        button.image.material=null;button.image.sprite=null;button.image.color=Color.clear;button.transition=Selectable.Transition.None;
        var rect=button.transform.Find("StyleSurface") as RectTransform;if(rect==null)rect=UIFactory.CreateRect("StyleSurface",(RectTransform)button.transform);
        UIFactory.Stretch(rect);rect.SetAsFirstSibling();
        if(rect.GetComponent<CanvasRenderer>()==null)rect.gameObject.AddComponent<CanvasRenderer>();
        var surface=rect.GetComponent<SongStyleSurface>();if(surface==null)surface=rect.gameObject.AddComponent<SongStyleSurface>();
        surface.diagonal=diagonal;surface.raycastTarget=true;surface.Apply(new Color(.7f,.85f,1),new Color(.95f,.7f,.9f),Color.white);
        label.fontStyle=FontStyles.Bold;label.raycastTarget=false;label.transform.SetAsLastSibling();return surface;
    }
    private static void Bottom(RectTransform rect,float x,float y,float width,float height)
    {var bottom=new Vector2(.5f,0);UIFactory.SetBox(rect,bottom,bottom,bottom,new Vector2(x,y),new Vector2(width,height));}
    private static TextMeshProUGUI ScreenText(RectTransform parent,string name,TMP_FontAsset font,float y,float height,float size)
    {
        var rect=parent.Find(name) as RectTransform;if(rect==null)rect=UIFactory.CreateRect(name,parent);
        var text=rect.GetComponent<TextMeshProUGUI>();if(text==null)text=rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font=font;text.fontSize=size;text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;
        Box(rect,0,y,1540,height);return text;
    }
    private static void ConfigurePurchase(SongSelectionView view,RectTransform parent)
    {
        var source=AssetDatabase.LoadAssetAtPath<JukeboxView>("Assets/_Project/Prefabs/UI/Jukebox.prefab");
        var existing=parent.Find("CustomSongsPurchase");
        var dialog=existing!=null?existing.gameObject:UnityEngine.Object.Instantiate(source.dialog,parent,false);
        dialog.name="CustomSongsPurchase";UIFactory.Stretch((RectTransform)dialog.transform);dialog.transform.SetAsLastSibling();
        foreach(var localized in dialog.GetComponentsInChildren<LocalizedText>(true))UnityEngine.Object.DestroyImmediate(localized);
        var dim=dialog.GetComponent<Image>();if(dim!=null){dim.color=new Color(0,0,0,.65f);dim.raycastTarget=true;}
        view.customPurchaseDialog=dialog;
        view.customPurchaseTitle=ClonePart<TextMeshProUGUI>(source.dialogTitle.transform,source.dialog.transform,dialog.transform);
        view.customPurchaseBody=ClonePart<TextMeshProUGUI>(source.dialogBody.transform,source.dialog.transform,dialog.transform);
        view.customPurchaseConfirm=ClonePart<Button>(source.confirm.transform,source.dialog.transform,dialog.transform);
        view.customPurchaseClose=ClonePart<Button>(source.cancel.transform,source.dialog.transform,dialog.transform);
        view.customPurchaseConfirmLabel=ClonePart<TextMeshProUGUI>(source.confirmLabel.transform,source.dialog.transform,dialog.transform);
        view.customPurchaseCloseLabel=view.customPurchaseClose.GetComponentInChildren<TextMeshProUGUI>(true);
        var panel=dialog.transform.Find("Panel") as RectTransform;
        UIFactory.SetBox(panel,Vector2.one*.5f,Vector2.one*.5f,Vector2.one*.5f,Vector2.zero,new Vector2(1080,430));
        var rim=panel.GetComponent<Outline>();if(rim==null)rim=panel.gameObject.AddComponent<Outline>();rim.effectDistance=new Vector2(2,-2);rim.effectColor=new Color(.85f,.75f,.49f);
        Box(view.customPurchaseTitle.rectTransform,0,28,984,54);view.customPurchaseTitle.fontSize=34;view.customPurchaseTitle.fontStyle=FontStyles.Bold;
        var bodyScroll=view.customPurchaseBody.GetComponentInParent<ScrollRect>(true);Box((RectTransform)bodyScroll.transform,0,110,984,170);
        Bottom((RectTransform)view.customPurchaseConfirm.transform,-220,32,350,60);Bottom((RectTransform)view.customPurchaseClose.transform,220,32,350,60);
        var library=rootLibrary(view);
        TempoFuryUISetup.SkinButton(view.customPurchaseConfirm,view.customPurchaseConfirmLabel,library);
        TempoFuryUISetup.SkinButton(view.customPurchaseClose,view.customPurchaseCloseLabel,library);
        dialog.SetActive(false);
    }
    private static StyleSkinLibrarySO rootLibrary(SongSelectionView view)=>view.GetComponent<FrontendStyleBackground>().library;
    private static T ClonePart<T>(Transform source,Transform sourceRoot,Transform destination) where T:Component
    {return destination.Find(AnimationUtility.CalculateTransformPath(source,sourceRoot)).GetComponent<T>();}
}
#endif
