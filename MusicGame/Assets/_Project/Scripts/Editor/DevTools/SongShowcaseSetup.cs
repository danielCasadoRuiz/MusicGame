#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Installs the approved D composition using existing skin art, never generating duplicate textures.</summary>
[InitializeOnLoad]
public static class SongShowcaseSetup
{
    static SongShowcaseSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string request="Temp/SongShowcase.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlaying){EditorApplication.ExitPlaymode();return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        try
        {
            Patch(SongSelectionSkinSetup.RowPath,ConfigureRow);Patch(SongSelectionSkinSetup.ScreenPath,ConfigureScreen);
            AssetDatabase.SaveAssets();
            // Retire only the two font experiments created for this revision, once no prefab depends on them.
            foreach(var unused in new[]{"Assets/_Project/UI/Fonts/SongShowcase/ShowcaseHeadline SDF.asset","Assets/_Project/UI/Fonts/SongShowcase/BarlowCondensed-BlackItalic.ttf"})
            {
                bool needed=Array.IndexOf(AssetDatabase.GetDependencies(SongSelectionSkinSetup.ScreenPath),unused)>=0||Array.IndexOf(AssetDatabase.GetDependencies(SongSelectionSkinSetup.RowPath),unused)>=0;
                if(!needed&&AssetDatabase.LoadMainAssetAtPath(unused)!=null)AssetDatabase.DeleteAsset(unused);
            }
            SongShowcaseReview.Run();
            File.WriteAllText("../song-showcase-validation.txt","PASS: D composition, finite single-row catalogue, skin-specific header/logo, four visible tracks, selection frame, day/night and sixteen material pairs verified.\n");
        }
        catch(Exception e){File.WriteAllText("../song-showcase-validation.txt","FAIL: "+e);Debug.LogException(e);}
    }
    static void Patch(string path,Action<GameObject> configure)
    {
        var root=PrefabUtility.LoadPrefabContents(path);
        try{configure(root);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    public static void ConfigureRow(GameObject root)
    {
        var row=root.GetComponent<SongRowView>();row.showcase=true;
        var source=root.GetComponent<Image>();source.sprite=null;source.material=null;source.color=Color.clear;source.raycastTarget=false;
        var oldMesh=root.GetComponent<SongCardSurfaceMesh>();if(oldMesh!=null)oldMesh.enabled=false;
        var surface=root.transform.Find("CardSurface") as RectTransform;if(surface==null)surface=UIFactory.CreateRect("CardSurface",(RectTransform)root.transform);
        UIFactory.Stretch(surface);surface.SetAsFirstSibling();
        var image=surface.GetComponent<Image>();if(image==null)image=surface.gameObject.AddComponent<Image>();image.raycastTarget=true;
        var geometry=surface.GetComponent<SongCardSurfaceMesh>();if(geometry==null)geometry=surface.gameObject.AddComponent<SongCardSurfaceMesh>();geometry.slant=72;geometry.trimFrame=false;
        var shadow=root.transform.Find("CardShadow") as RectTransform;if(shadow==null)shadow=UIFactory.CreateRect("CardShadow",(RectTransform)root.transform);
        UIFactory.Stretch(shadow);shadow.SetAsFirstSibling();
        row.cardShadow=shadow.GetComponent<Image>();if(row.cardShadow==null)row.cardShadow=shadow.gameObject.AddComponent<Image>();
        row.cardShadow.raycastTarget=false;row.cardShadow.color=new Color(0,0,0,.32f);row.cardShadow.material=NeonMaterial(true);
        var shadowMesh=shadow.GetComponent<SongNeonStroke>();if(shadowMesh==null)shadowMesh=shadow.gameObject.AddComponent<SongNeonStroke>();shadowMesh.padding=14;shadowMesh.slant=72;
        row.background=image;row.skin.image=image;row.button.targetGraphic=image;root.GetComponent<SongCardAppearance>().image=image;
        row.label.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");row.label.fontStyle=FontStyles.Bold|FontStyles.Italic;row.label.fontWeight=FontWeight.Bold;
        row.label.fontSize=38;row.label.fontSizeMax=38;row.label.fontSizeMin=28;row.label.enableAutoSizing=true;
        TextBox(row.label,84,28,-128,86);row.label.textWrappingMode=TextWrappingModes.Normal;row.label.alignment=TextAlignmentOptions.TopLeft;
        row.artistLabel.fontStyle=FontStyles.Normal;row.artistLabel.fontSize=28;row.artistLabel.fontSizeMax=28;row.artistLabel.fontSizeMin=21;
        TextBox(row.artistLabel,84,100,-154,40);row.artistLabel.alignment=TextAlignmentOptions.TopLeft;
        row.genreLabel.fontSize=13;TextBox(row.genreLabel,36,9,-70,18);row.genreLabel.alignment=TextAlignmentOptions.MidlineRight;
        row.completedLabel.rectTransform.anchoredPosition=new Vector2(84,10);row.completedLabel.fontSize=12;
        row.equalizer.anchoredPosition=new Vector2(-92,12);
        var frame=(RectTransform)row.selectionFrame.transform;frame.offsetMin=frame.offsetMax=Vector2.zero;
        row.selectionRivet.enabled=false;
        var artwork=frame.Find("Artwork") as RectTransform;if(artwork==null)artwork=UIFactory.CreateRect("Artwork",frame);UIFactory.Stretch(artwork);
        row.selectionArtwork=artwork.GetComponent<Image>();if(row.selectionArtwork==null)row.selectionArtwork=artwork.gameObject.AddComponent<Image>();
        row.selectionArtwork.sprite=null;row.selectionArtwork.color=Color.white;row.selectionArtwork.raycastTarget=false;
        row.selectionArtwork.material=SelectionRimMaterial();
        var rimMesh=artwork.GetComponent<SongNeonStroke>();if(rimMesh==null)rimMesh=artwork.gameObject.AddComponent<SongNeonStroke>();
        rimMesh.padding=10;rimMesh.slant=72;
    }
    static void TextBox(TextMeshProUGUI text,float x,float y,float width,float height)
    {
        var rect=text.rectTransform;rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(0,1);
        rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);
    }
    static void Box(RectTransform rect,float x,float y,float width,float height)
    {var top=new Vector2(.5f,1);UIFactory.SetBox(rect,top,top,top,new Vector2(x,-y),new Vector2(width,height));}
    public static void ConfigureScreen(GameObject root)
    {
        var view=root.GetComponent<SongSelectionView>();var content=view.contentRoot;
        var showcase=root.GetComponent<SongSelectionShowcase>();if(showcase==null)showcase=root.AddComponent<SongSelectionShowcase>();showcase.view=view;
        Box(view.titleText.rectTransform,0,21,1370,120);
        view.titleText.alignment=TextAlignmentOptions.Center;
        var logo=content.Find("SkinLogo");
        if(logo==null)
        {
            var source=AssetDatabase.LoadAssetAtPath<MainMenuView>("Assets/_Project/Prefabs/UI/MainMenu.prefab");
            showcase.logo=UnityEngine.Object.Instantiate(source.logo,content);showcase.logo.name="SkinLogo";
        }
        else showcase.logo=logo.GetComponent<StyledTitleView>();
        Box((RectTransform)showcase.logo.transform,-720,-440,1920,1080);
        ((RectTransform)showcase.logo.transform).pivot=Vector2.one*.5f;
        ((RectTransform)showcase.logo.transform).anchoredPosition=new Vector2(-760,-88);
        showcase.logo.transform.localScale=Vector3.one*.16f;
        foreach(var image in showcase.logo.GetComponentsInChildren<Image>(true))image.raycastTarget=false;
        var plate=content.Find("LogoPlate") as RectTransform;if(plate==null)plate=UIFactory.CreateRect("LogoPlate",content);
        Box(plate,-760,-15,520,220);plate.SetAsFirstSibling();
        if(plate.GetComponent<CanvasRenderer>()==null)plate.gameObject.AddComponent<CanvasRenderer>();
        showcase.logoPlate=plate.GetComponent<SongStyleSurface>();if(showcase.logoPlate==null)showcase.logoPlate=plate.gameObject.AddComponent<SongStyleSurface>();
        showcase.logoPlate.diagonal=true;showcase.logoPlate.slantWidth=155;showcase.logoPlate.raycastTarget=false;plate.gameObject.SetActive(false);
        Box((RectTransform)content.Find("Decades"),0,206,1580,65);
        var carousel=view.songListRoot.GetComponentInParent<SongCatalogCarousel>(true);
        carousel.rowCount=1;carousel.visibleColumns=4;carousel.cardPadding=40;carousel.cardGap=-28;carousel.referenceWidth=1920;
        showcase.catalogue=carousel;
        for(int i=0;i<carousel.decadeButtons.Length;i++)
        {
            var tab=(RectTransform)carousel.decadeButtons[i].transform;
            var texture=carousel.decadeButtons[i].GetComponent<RectangularStyleButton>();texture.emphasiseSelection=false;texture.nativeAspect=true;
            UIFactory.SetBox(tab,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(i*(1580f/7),0),new Vector2(1580f/7,65));
            var oldMesh=tab.GetComponent<RectangularButtonMesh>();if(oldMesh!=null)oldMesh.enabled=false;
            var hit=tab.GetComponent<Image>();hit.sprite=null;hit.material=null;hit.color=Color.clear;hit.raycastTarget=true;
            var native=tab.Find("NativeArt") as RectTransform;if(native==null)native=UIFactory.CreateRect("NativeArt",tab);
            var centre=Vector2.one*.5f;float scale=(1580f/7)/StyleSkinLibrarySO.ButtonBody.width;
            UIFactory.SetBox(native,centre,centre,centre,Vector2.zero,StyleSkinLibrarySO.ButtonCanvas*scale);native.SetAsFirstSibling();
            texture.image=native.GetComponent<Image>();if(texture.image==null)texture.image=native.gameObject.AddComponent<Image>();
            texture.image.preserveAspect=true;texture.image.raycastTarget=false;
            foreach(var effect in tab.GetComponents<Shadow>())effect.enabled=false;
        }
        Box((RectTransform)carousel.transform,0,594,1920,268);
        Box(view.catalogSupport.rectTransform,0,206,1800,710);
        var track=carousel.positionTrack.rectTransform;
        UIFactory.SetBox(track,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,-12),new Vector2(1720,3));
        Box(view.selectedCard.rectTransform,0,282,1920,410);
        // The catalogue crosses the painted stage's lower edge, and always renders in front of it.
        view.selectedCard.transform.SetSiblingIndex(2);
        view.selectedSongText.transform.SetParent(view.selectedCard.transform,false);
        Box(view.selectedSongText.rectTransform,-246,34,1060,204);
        view.selectedSongText.font=HeadlineFont();view.selectedSongText.fontStyle=FontStyles.Bold|FontStyles.Italic|FontStyles.UpperCase;view.selectedSongText.fontWeight=FontWeight.Bold;
        view.selectedSongText.fontSize=216;view.selectedSongText.fontSizeMax=216;view.selectedSongText.fontSizeMin=62;view.selectedSongText.characterSpacing=0;
        view.selectedSongText.alignment=TextAlignmentOptions.MidlineLeft;
        view.selectedTexture.GetComponent<SongCardSurfaceMesh>().slant=0;
        UIFactory.Stretch(view.selectedTexture.rectTransform);
        // Preserve the artist-adjusted surface: left/right -80, top -94, bottom -96.
        view.selectedTexture.rectTransform.offsetMin=new Vector2(-80,-96);
        view.selectedTexture.rectTransform.offsetMax=new Vector2(80,94);


        view.selectedAppearance.stageArtwork=null;
        foreach(var card in view.selectedAppearance.library.songCards)
        {
            string bandPath="Assets/_Project/Art/UI/SongCards/Stages/"+card.style+".png";
            if(!File.Exists(bandPath))throw new FileNotFoundException("Missing authored song band",bandPath);
            ImportTexture(bandPath,true,2048);
            var bandImport=(TextureImporter)AssetImporter.GetAtPath(bandPath);
            bandImport.textureCompression=TextureImporterCompression.CompressedHQ;bandImport.compressionQuality=100;
            foreach(string platform in new[]{"Standalone","Android","iPhone"})
            {
                var settings=bandImport.GetPlatformTextureSettings(platform);settings.name=platform;settings.overridden=true;
                settings.maxTextureSize=2048;settings.format=platform=="Standalone"?TextureImporterFormat.BC7:TextureImporterFormat.ASTC_6x6;
                settings.compressionQuality=100;bandImport.SetPlatformTextureSettings(settings);
            }
            bandImport.SaveAndReimport();
            card.stageArtwork=AssetDatabase.LoadAssetAtPath<Sprite>(bandPath);
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                source.LoadImage(File.ReadAllBytes(bandPath));
                int centre=source.width/2,lower=source.height/2,upper=source.height/2;
                while(lower>0&&source.GetPixel(centre,lower-1).a>.94f)lower--;
                while(upper<source.height-1&&source.GetPixel(centre,upper+1).a>.94f)upper++;
                float span=(upper-lower+1f)/source.height;
                card.stageUVRange=new Vector2(lower/(float)source.height-span*.2f,(upper+1f)/source.height+span*.2f);
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
            if(card.stageArtwork==null)throw new InvalidOperationException("Song band did not import: "+card.style);
        }
        EditorUtility.SetDirty(view.selectedAppearance.library);
        view.selectedAppearance.stage=true;view.selectedAppearance.stageMask=null;
        foreach(var name in new[]{"PaintLeft","PaintRight"}){var old=view.selectedCard.transform.Find(name);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);}
        showcase.paintLeft=null;showcase.paintRight=null;
        var artist=view.selectedCard.transform.Find("Artist") as RectTransform;
        if(artist==null)artist=UIFactory.CreateRect("Artist",view.selectedCard.rectTransform);
        showcase.artist=artist.GetComponent<TextMeshProUGUI>();if(showcase.artist==null)showcase.artist=artist.gameObject.AddComponent<TextMeshProUGUI>();
        showcase.artist.font=view.statusText.font;showcase.artist.fontSize=48;showcase.artist.fontStyle=FontStyles.Bold|FontStyles.Italic|FontStyles.UpperCase;
        showcase.artist.enableAutoSizing=true;showcase.artist.fontSizeMin=25;showcase.artist.fontSizeMax=48;
        showcase.artist.characterSpacing=12;showcase.artist.alignment=TextAlignmentOptions.MidlineLeft;
        showcase.artist.textWrappingMode=TextWrappingModes.NoWrap;showcase.artist.overflowMode=TextOverflowModes.Ellipsis;showcase.artist.raycastTarget=false;
        Box(artist,-246,217,1060,65);
        var bars=view.selectedCard.transform.Find("Spectrum") as RectTransform;
        if(bars==null)bars=UIFactory.CreateRect("Spectrum",view.selectedCard.rectTransform);
        Box(bars,514,90,520,170);
        if(bars.GetComponent<CanvasRenderer>()==null)bars.gameObject.AddComponent<CanvasRenderer>();
        showcase.bars=bars.GetComponent<SongMusicBars>();if(showcase.bars==null)showcase.bars=bars.gameObject.AddComponent<SongMusicBars>();showcase.bars.raycastTarget=false;
        showcase.bars.neonMaterial=NeonMaterial(false);showcase.bars.material=showcase.bars.neonMaterial;
        showcase.bars.gameObject.SetActive(false);
        view.previewProgressRoot.transform.SetParent(view.selectedCard.transform,false);
        Box((RectTransform)view.previewProgressRoot.transform,-246,200,1060,8);
        var progressOutline=view.previewProgressFill.GetComponent<Outline>();if(progressOutline==null)progressOutline=view.previewProgressFill.gameObject.AddComponent<Outline>();progressOutline.effectDistance=new Vector2(3,2);progressOutline.effectColor=new Color(1,.1f,.7f,.3f);
        progressOutline.enabled=false;
        var progressInk=view.previewProgressFill.GetComponent<Image>();progressInk.material=NeonMaterial(false);
        var progressMesh=view.previewProgressFill.GetComponent<SongNeonStroke>();if(progressMesh==null)progressMesh=view.previewProgressFill.gameObject.AddComponent<SongNeonStroke>();progressMesh.padding=16;
        var trim=view.selectedRivet;trim.gameObject.SetActive(false);
        view.selectedSongText.transform.SetAsLastSibling();artist.SetAsLastSibling();
        showcase.placeholderTitle=Placeholder(view.selectedSongText,"PlaceholderTitle");showcase.placeholderArtist=Placeholder(showcase.artist,"PlaceholderArtist");
        showcase.titleProgress=view.selectedSongText.GetComponent<SongTitlePreviewProgress>();
        if(showcase.titleProgress==null)showcase.titleProgress=view.selectedSongText.gameObject.AddComponent<SongTitlePreviewProgress>();
        showcase.titleProgress.title=view.selectedSongText;
        showcase.titleProgress.progressShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/SongTitleProgress.shader");
        showcase.titleProgress.reviewPlaying=false;
        view.previewProgressRoot.SetActive(false);
        var strings=LocalizationEditorSettings.GetStringTableCollection("UIText");
        foreach(var table in strings.StringTables)
        {
            bool ca=table.LocaleIdentifier.Code=="ca";
            table.AddEntry("SongSelection.PlaceholderTitle",ca?"TÍTOL DE LA CANÇÓ":"SONG TITLE");
            table.AddEntry("SongSelection.PlaceholderArtist",ca?"ARTISTA / GRUP":"ARTIST / BAND");EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(strings.SharedData);
        var bottom=new Vector2(.5f,0);
        float buttonAspect=StyleSkinLibrarySO.ButtonBody.width/StyleSkinLibrarySO.ButtonBody.height;
        float smallWidth=82*buttonAspect,largeWidth=104*buttonAspect;
        float gap=(1720-smallWidth*2-largeWidth)/2,left=-860;
        UIFactory.SetBox((RectTransform)view.playYourSongButton.transform,bottom,bottom,bottom,new Vector2(left+smallWidth*.5f,48),new Vector2(smallWidth,82));
        UIFactory.SetBox((RectTransform)view.backButton.transform,bottom,bottom,bottom,new Vector2(left+smallWidth+gap+smallWidth*.5f,48),new Vector2(smallWidth,82));
        UIFactory.SetBox((RectTransform)view.playButton.transform,bottom,bottom,bottom,new Vector2(left+smallWidth*2+gap*2+largeWidth*.5f,48),new Vector2(largeWidth,104));
        view.customSurface.diagonal=true;view.customSurface.slantWidth=112f*82/192;
        foreach(var button in new[]{view.playYourSongButton,view.backButton,view.playButton})
        {
            button.image.preserveAspect=true;
            foreach(var layer in button.GetComponentsInChildren<Image>(true))layer.preserveAspect=true;
        }
        Box(view.statusText.rectTransform,0,892,1700,24);
        view.playButtonLabel.fontStyle=FontStyles.Bold|FontStyles.Italic;view.playButtonLabel.fontSize=48;
        view.backButtonLabel.fontStyle=FontStyles.Bold|FontStyles.Italic;view.backButtonLabel.fontSize=36;
        view.playYourSongLabel.fontStyle=FontStyles.Bold|FontStyles.Italic;view.playYourSongLabel.fontSize=28;
    }
    static TextMeshProUGUI Placeholder(TextMeshProUGUI source,string name)
    {
        var existing=source.transform.parent.Find(name);
        var result=existing!=null?existing.GetComponent<TextMeshProUGUI>():UnityEngine.Object.Instantiate(source,source.transform.parent);
        result.name=name;result.rectTransform.anchorMin=source.rectTransform.anchorMin;result.rectTransform.anchorMax=source.rectTransform.anchorMax;
        result.rectTransform.pivot=source.rectTransform.pivot;result.rectTransform.anchoredPosition=source.rectTransform.anchoredPosition;result.rectTransform.sizeDelta=source.rectTransform.sizeDelta;
        result.text="";result.raycastTarget=false;return result;
    }
    static Material NeonMaterial(bool shadow)
    {
        AssetDatabase.ImportAsset("Assets/_Project/Shaders/SongNeon.shader",ImportAssetOptions.ForceSynchronousImport);
        string path="Assets/_Project/Configs/UI/"+(shadow?"SongCardShadow":"SongNeon")+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("MusicGame/UI/SongNeon"));AssetDatabase.CreateAsset(material,path);}
        material.SetFloat("_GlowRadius",shadow?5.5f:7);material.SetFloat("_ShadowOnly",shadow?1:0);material.SetFloat("_Slant",shadow?72:0);
        EditorUtility.SetDirty(material);return material;
    }
    static void ImportTexture(string path,bool sprite,int size)
    {
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=sprite?TextureImporterType.Sprite:TextureImporterType.Default;importer.spriteImportMode=SpriteImportMode.Single;
        importer.maxTextureSize=size;importer.mipmapEnabled=false;importer.isReadable=false;importer.npotScale=TextureImporterNPOTScale.None;
        importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
    }
    static Material SelectionRimMaterial()
    {
        const string shader="Assets/_Project/Shaders/SongSelectionRim.shader";
        AssetDatabase.ImportAsset(shader,ImportAssetOptions.ForceSynchronousImport);
        const string path="Assets/_Project/Configs/UI/SongSelectionRim.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(AssetDatabase.LoadAssetAtPath<Shader>(shader));AssetDatabase.CreateAsset(material,path);}
        material.SetFloat("_Slant",72);EditorUtility.SetDirty(material);return material;
    }
    static TMP_FontAsset HeadlineFont()
    {
        const string path="Assets/_Project/UI/Fonts/SongShowcase/ShowcaseHeadlineWide SDF.asset";
        var result=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);if(result!=null)return result;
        const string source="Assets/_Project/UI/Fonts/SongShowcase/Barlow-BlackItalic.ttf";
        AssetDatabase.ImportAsset(source,ImportAssetOptions.ForceSynchronousImport);
        result=TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(source),90,7,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,false);
        result.name="ShowcaseHeadlineWide SDF";
        string chars="";for(int i=32;i<=255;i++)chars+=(char)i;chars+="—–‘’“”…";
        result.TryAddCharacters(chars);result.atlasPopulationMode=AtlasPopulationMode.Static;
        result.fallbackFontAssetTable=new System.Collections.Generic.List<TMP_FontAsset>{AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset")};
        AssetDatabase.CreateAsset(result,path);AssetDatabase.AddObjectToAsset(result.material,result);
        foreach(var texture in result.atlasTextures){texture.name="ShowcaseHeadline atlas";AssetDatabase.AddObjectToAsset(texture,result);}
        EditorUtility.SetDirty(result);AssetDatabase.SaveAssets();return result;
    }
    static RawImage Paint(RectTransform parent,string name,Texture2D mask,float x,float y,float width,float height)
    {
        var rect=parent.Find(name) as RectTransform;if(rect==null)rect=UIFactory.CreateRect(name,parent);Box(rect,x,y,width,height);
        var image=rect.GetComponent<RawImage>();if(image==null)image=rect.gameObject.AddComponent<RawImage>();image.texture=mask;image.raycastTarget=false;return image;
    }
}
#endif
