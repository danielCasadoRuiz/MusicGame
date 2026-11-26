#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
public static class SongSelectionPolishSetup
{
    public static void ConfigureScreen(GameObject root)
    {
        var view=root.GetComponent<SongSelectionView>();var lib=root.GetComponent<FrontendStyleBackground>().library;
        view.titleText.GetComponent<TitleText>().songTextureInk=true;
        var back=(RectTransform)view.backButton.transform;var play=(RectTransform)view.playButton.transform;var custom=(RectTransform)view.playYourSongButton.transform;
        const float gap=32,left=-860,right=860,largeHeight=104;
        float ratio=StyleSkinLibrarySO.ButtonBody.width/StyleSkinLibrarySO.ButtonBody.height;
        float largeWidth=largeHeight*ratio,backWidth=right-left-2*largeWidth-2*gap;
        Box(back,left+backWidth/2,48,backWidth,backWidth/ratio);
        Box(custom,left+backWidth+gap+largeWidth/2,48,largeWidth,largeHeight);
        Box(play,right-largeWidth/2,48,largeWidth,largeHeight);
        view.playYourSongLabel.enableAutoSizing=true;view.playYourSongLabel.fontSizeMax=36;view.playYourSongLabel.fontSizeMin=24;view.playYourSongLabel.fontSize=36;
        var group=view.backButton.GetComponent<CanvasGroup>();if(group!=null)group.alpha=1;
        if(view.customSurface!=null)view.customSurface.gameObject.SetActive(false);
        var old=view.playYourSongButton.GetComponent<RectangularStyleButton>();if(old!=null)UnityEngine.Object.DestroyImmediate(old);
        view.playYourSongButton.image.color=Color.clear;view.playYourSongButton.transition=Selectable.Transition.None;
        var blend=view.playYourSongButton.GetComponent<StyleBlendButton>();if(blend==null)blend=view.playYourSongButton.gameObject.AddComponent<StyleBlendButton>();
        blend.library=lib;blend.label=view.playYourSongLabel;blend.stretchMiddle=true;blend.accentFace=true;
        blend.classicLayer=Layer(custom,"Classic extended art");blend.styleLayer=Layer(custom,"Style extended art");
        blend.classicLayer.transform.SetAsFirstSibling();blend.styleLayer.transform.SetSiblingIndex(1);view.playYourSongLabel.transform.SetAsLastSibling();blend.FitLayers();blend.SetBlend(StyledTitleView.MenuStyle(lib),1);
    }
    static Image Layer(RectTransform parent,string name)
    {
        var child=parent.Find(name);if(child==null){var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));go.transform.SetParent(parent,false);child=go.transform;}
        var image=child.GetComponent<Image>();image.raycastTarget=false;image.preserveAspect=false;
        if(child.GetComponent<StyleButtonStretchMesh>()==null)child.gameObject.AddComponent<StyleButtonStretchMesh>();return image;
    }
    static void Box(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,0);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);}
    public static void Install()
    {
        var library=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(AppearanceSetup.LibraryPath);library.songHeadingInks.Clear();
        foreach(var card in library.songCards)
        {
            var night=Sample(library.Button(card.style)?.selected,new Rect(.46f,.40f,.08f,.20f),false);
            if(card.style==GameMusicStyle.Groove)night=new Color32(251,185,22,255); // Warm gold sampled from x800..1000/y100..230 of the selected button.
            var day=Sample(card.stageArtwork!=null?card.stageArtwork:card.sprite,new Rect(.38f,.36f,.22f,.28f),true);
            library.songHeadingInks.Add(new StyleSkinLibrarySO.SongHeadingInk{style=card.style,day=day,night=night});
        }
        const string materialPath="Assets/_Project/Configs/UI/SongCustomAccent.mat";
        AssetDatabase.ImportAsset("Assets/_Project/Shaders/SongCustomAccent.shader",ImportAssetOptions.ForceSynchronousImport);
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(material==null){material=new Material(Shader.Find("MusicGame/UI/SongCustomAccent"));AssetDatabase.CreateAsset(material,materialPath);}
        library.customSongAccentMaterial=material;
        EditorUtility.SetDirty(library);
        Patch(SongSelectionSkinSetup.ScreenPath,ConfigureScreen);
        Patch(SongSelectionSkinSetup.RowPath,go=>{var row=go.GetComponent<SongRowView>();row.cardSlant=72;row.selectedScale=1.08f;});
        Patch("Assets/_Project/Prefabs/UI/PauseMenu.prefab",go=>{foreach(var title in go.GetComponent<PauseView>().overlayRoot.GetComponentsInChildren<TitleText>(true))title.gameplayStyle=true;});
        Patch("Assets/_Project/Prefabs/UI/FightHud.prefab",go=>{foreach(var title in go.GetComponent<FightHudView>().pausePanel.GetComponentsInChildren<TitleText>(true))title.gameplayStyle=true;});
        AssetDatabase.SaveAssets();SongSelectionPolishReview.Run();
    }
    static Color Sample(Sprite sprite,Rect region,bool dark)
    {
        if(sprite==null)return dark?new Color(.08f,.06f,.12f):Color.white;
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
        try
        {
            texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
            var samples=new System.Collections.Generic.List<Color>();
            for(int y=0;y<15;y++)for(int x=0;x<25;x++)
            {
                var c=texture.GetPixelBilinear(region.x+region.width*x/24,region.y+region.height*y/14);if(c.a>.8f)samples.Add(c);
            }
            samples=samples.OrderBy(c=>c.grayscale).ToList();if(samples.Count==0)return Color.white;
            int a=Mathf.FloorToInt(samples.Count*(dark?.12f:.4f)),b=Mathf.CeilToInt(samples.Count*(dark?.48f:.8f));
            Color result=Color.black;for(int i=a;i<b;i++)result+=samples[i];result/=(b-a);result.a=1;
            // Keep a dark heading dark even if the artwork contains pale scratches through the centre.
            if(dark&&result.grayscale>.23f)result*=.23f/result.grayscale;
            if(!dark){Color.RGBToHSV(result,out var hue,out var saturation,out var value);if(value<.85f)result=Color.HSVToRGB(hue,Mathf.Min(saturation,.85f),.95f);}
            result.a=1;return result;
        }
        finally{UnityEngine.Object.DestroyImmediate(texture);}
    }
    static void Patch(string path,Action<GameObject> action){var go=PrefabUtility.LoadPrefabContents(path);try{action(go);PrefabUtility.SaveAsPrefabAsset(go,path);}finally{PrefabUtility.UnloadPrefabContents(go);}}
}
#endif
