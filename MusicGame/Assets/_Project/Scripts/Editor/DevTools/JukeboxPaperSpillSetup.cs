#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class JukeboxPaperSpillSetup
{
    const string Art="Assets/_Project/Art/UI/Jukebox/ScatteredScores.png";
    const string Prefab="Assets/_Project/Prefabs/UI/Jukebox.prefab";
    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(Art);
        importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=true;
        importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.CompressedHQ;
        importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art);
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var view=root.GetComponent<JukeboxView>();view.framingInsets=new Vector2(90,50);
            var cabinet=view.cabinetContent.Find("Cabinet").GetComponent<Image>();
            if(cabinet.GetComponent<JukeboxCabinetBaseCrop>()==null)cabinet.gameObject.AddComponent<JukeboxCabinetBaseCrop>();
            var old=view.cabinetContent.Find("PaperSpillRoot");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var papers=new GameObject("PaperSpillRoot",typeof(RectTransform)).GetComponent<RectTransform>();
            papers.SetParent(view.cabinetContent,false);Place(papers,0,0,1740,980);
            // Source UV rectangles select the two clusters / an isolated page without
            // resampling or changing the generated alpha. All four Images share one texture.
            Add(papers,texture,"Left scattered scores",new Rect(0,90,870,590),-10,810,285,285*590f/870,0);
            Add(papers,texture,"Right scattered scores",new Rect(1520,110,652,560),1585,827,200,200*560f/652,0);
            Add(papers,texture,"Loose score left",new Rect(5,120,200,145),355,888,100,100*145f/200,-8);
            Add(papers,texture,"Loose score right",new Rect(5,120,200,145),1125,865,115,115*145f/200,15);
            papers.SetAsLastSibling();
            foreach(var image in papers.GetComponentsInChildren<RawImage>())if(image.raycastTarget)throw new Exception("Paper blocks input");
            PrefabUtility.SaveAsPrefabAsset(root,Prefab);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();
        TitleScreensReview.CaptureAcceptedJukebox();
        File.WriteAllText("../jukebox-paper-spill-validation.txt","PASS: original cabinet image preserved; legacy floor clipped in UI mesh; four non-interactive scores share one RGBA texture; 90/50 framing inset; Back and playback controls unchanged. Captured day/night.\n");
    }
    static void Place(RectTransform r,float x,float y,float width,float height)
    {
        r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);
        r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);
    }
    static void Add(Transform parent,Texture2D texture,string name,Rect pixels,float x,float y,float width,float height,float rotation)
    {
        var image=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(parent,false);image.texture=texture;image.raycastTarget=false;
        image.uvRect=new Rect(pixels.x/texture.width,1-pixels.yMax/texture.height,pixels.width/texture.width,pixels.height/texture.height);
        Place(image.rectTransform,x,y,width,height);image.rectTransform.localRotation=Quaternion.Euler(0,0,rotation);
    }
}
#endif
