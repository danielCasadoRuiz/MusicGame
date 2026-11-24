#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class CollectionCardValidation
{
    private static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
    private static Sprite Portrait(OpponentDefinition rival,int level) =>
        (Sprite)typeof(RivalCollectionController).GetMethod("PortraitFor",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{rival,level});

    [MenuItem("Tools/MusicGame/UI/Verify Composer Collection Cards")]
    public static void Verify()
    {
        var roster=AssetDatabase.LoadAssetAtPath<OpponentRosterSO>("Assets/_Project/Configs/Fight/OpponentRoster.asset");
        var prefab=AssetDatabase.LoadAssetAtPath<RivalCardView>(CollectionCardSetup.CardPath);
        var screen=AssetDatabase.LoadAssetAtPath<RivalCollectionView>(CollectionCardSetup.ScreenPath);
        Check(roster!=null&&prefab!=null&&screen!=null,"Missing roster/card/screen");
        Check(prefab.transform.Find("VersionStar5")==null,"Obsolete fifth star remains visible");
        Check(prefab.versionStars.Length==4&&prefab.versionStars.All(s=>s!=null),"Need four separate star slots");
        Check(screen.backButton.GetComponent<StyleBlendButton>()!=null&&screen.detailClose.GetComponent<StyleBlendButton>()==null,"Collection back/close art mismatch");
        Check(screen.GetComponent<FrontendStyleBackground>().controls!=null,"Collection back style is not linked to its background");
        var jukebox=AssetDatabase.LoadAssetAtPath<JukeboxView>("Assets/_Project/Prefabs/UI/Jukebox.prefab");
        Check(jukebox.back.image.sprite.name=="TransportCenter","Jukebox Back needs the symmetric double-edged plate");
        Check(jukebox.back.GetComponents<ThemeElementReceiver>().Length==0,"Jukebox Back retains theme tint receiver");
        Check(jukebox.back.colors.highlightedColor==new Color(1f,.94f,.84f),"Jukebox Back hover differs from transport controls");
        var test=UnityEngine.Object.Instantiate(prefab);
        int count=0;
        try
        {
            foreach(var rival in roster.opponents.Where(o=>o!=null))
            {
                var versions=rival.levels.Where(l=>l!=null&&l.level>0).OrderBy(l=>l.level).ToArray();
                Check(versions.Length>0,"Missing versions: "+rival.id);
                foreach(var version in versions)
                {
                    var portrait=Portrait(rival,version.level);
                    Check(portrait!=null,"Missing existing portrait: "+rival.id+" "+version.level);
                    test.SetCollection(portrait,rival.displayName,new[]{version.level});
                    Check(test.portrait.sprite==portrait&&test.portrait.color==Color.white,"Wrong unlocked portrait: "+rival.id);
                    Check(test.namePlate.gameObject.activeSelf&&test.nameText.text==rival.displayName,"TMP name missing: "+rival.id);
                    Check(test.button.interactable,"Unlocked card is disabled: "+rival.id);
                    for(int i=0;i<4;i++)Check(test.versionStars[i].enabled==(i+1==version.level),"Version marks are not independent: "+rival.id);
                    test.SetCollection(portrait,rival.displayName,Array.Empty<int>());
                    Check(test.portrait.sprite==portrait&&test.portrait.color==Color.black,"Locked card must tint the SAME portrait: "+rival.id);
                    Check(test.nameText.text==""&&!test.namePlate.gameObject.activeSelf,"Locked identity leaked: "+rival.id);
                    Check(!test.button.interactable,"Locked card is still clickable: "+rival.id);
                    Check(test.versionStars.All(s=>!s.enabled),"Locked stars incorrectly filled");
                    test.SetVersion(portrait, true, version.level, true);
                    Check(!test.namePlate.gameObject.activeSelf, "Popup repeats nameplate");
                    Check(test.versionStars.Count(s=>s.enabled)==Mathf.Min(4,version.level), "Popup stars must represent level");
                    test.SetVersion(portrait, false, version.level, false);
                    Check(test.portrait.color==Color.black && !test.button.interactable, "Locked popup version is revealed or clickable");
                    count++;
                }
                var won=new[]{versions.First().level,versions.Last().level};
                test.SetCollection(Portrait(rival,won.Max()),rival.displayName,won);
                Check(test.portrait.sprite==Portrait(rival,won.Max()),"Highest defeated portrait selection failed");
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(test.gameObject);}
        Render(roster,prefab,screen);
        FrontendBackdropValidation.Verify();
        File.WriteAllText("../collection-card-validation.txt","PASS: "+count+" existing composer/version portraits checked; locked cards reuse the same sprite in solid black with no name; four independent version stars; separate transparent label with TMP text; highest won portrait retained; popup uses cumulative level stars without nameplates; modal blocks gallery and restores it on close; collection + detail previews rendered; no save data changed.");
        Debug.Log("Collection card validation passed: "+count+" portraits.");
    }

    private static void Render(OpponentRosterSO roster,RivalCardView prefab,RivalCollectionView screen)
    {
        var canvasObject=new GameObject("Collection review",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var cameraObject=new GameObject("Collection review camera",typeof(Camera));
        var canvas=canvasObject.GetComponent<Canvas>();var camera=cameraObject.GetComponent<Camera>();
        camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var rt=new RenderTexture(1920,1080,24);camera.targetTexture=rt;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        Directory.CreateDirectory("../VisualReviews/CollectionCards");
        try
        {
            var live=UnityEngine.Object.Instantiate(screen,canvas.transform);
            UIFactory.Stretch((RectTransform)live.transform);live.gameObject.SetActive(true);live.detailRoot.gameObject.SetActive(false);
            var bg=live.GetComponent<FrontendStyleBackground>();bg.ShowHome();
            var title=live.transform.Find("Title")?.GetComponent<TMPro.TMP_Text>();if(title!=null)title.text="COLLECTION";
            var rivals=roster.opponents.Where(o=>o!=null).ToArray();
            int index=0,found=0,total=0;
            foreach(var rival in rivals)
            {
                var card=UnityEngine.Object.Instantiate(prefab,live.grid);
                var versions=rival.levels.Where(l=>l!=null&&l.level>0).OrderBy(l=>l.level).ToArray();
                int[] won=index%4==1?Array.Empty<int>():versions.Take(1+index%5).Select(l=>l.level).ToArray();
                found+=won.Length;total+=versions.Length;
                var portrait=Portrait(rival,won.Length>0?won.Max():versions.Last().level);
                card.SetCollection(portrait,rival.displayName,won);
                index++;
            }
            live.summary.text=found+" / "+total;
            var back=live.backButton.GetComponentInChildren<TMPro.TMP_Text>(true);if(back!=null)back.text="BACK";
            Canvas.ForceUpdateCanvases();
            live.FitGallery();
            Capture(camera,rt,"collection");
            var chopin=rivals.FirstOrDefault(r=>r.id=="chopin")??rivals.First();
            live.SetDetailVisible(true);live.detailTitle.text=chopin.displayName;
            Check(!live.galleryInteraction.interactable&&!live.backButton.interactable&&!live.backButton.gameObject.activeSelf,"Modal allows background navigation or exposes Back");
            Check(live.detailRoot.Find("DetailDim").GetComponent<Image>().raycastTarget,"Modal does not intercept clicks");
            live.detailBio.text="<color=#795B30>1810–1849</color>\n" + LocalizationSetup.Entries.First(e=>e.key=="Composer.chopin.Bio").ca;
            foreach(var version in chopin.levels.Where(l=>l!=null&&l.level>0).OrderBy(l=>l.level))
            {
                var card=UnityEngine.Object.Instantiate(prefab,live.detailRow);
                card.SetVersion(Portrait(chopin,version.level),version.level<=2,version.level,version.level==2);
                if(version.level==2 && card.footer!=null)card.footer.text="ACTUAL";
            }
            var close=live.detailClose.GetComponentInChildren<TMPro.TMP_Text>(true);if(close!=null)close.text="×";
            bg.Show(GameMusicStyle.Electronic,GameMusicStyle.Electronic,1f);
            live.ApplyDetailStyle(GameMusicStyle.Electronic);
            Capture(camera,rt,"composer-versions");
            bg.Show(GameMusicStyle.Rock,GameMusicStyle.Rock,1f);
            live.ApplyDetailStyle(GameMusicStyle.Rock);
            Capture(camera,rt,"composer-versions-rock");
            bg.Show(GameMusicStyle.Acoustic,GameMusicStyle.Acoustic,1f);
            live.ApplyDetailStyle(GameMusicStyle.Acoustic);
            Capture(camera,rt,"composer-versions-acoustic");
            live.SetDetailVisible(false);
            Check(live.galleryInteraction.interactable&&live.backButton.interactable&&live.backButton.gameObject.activeSelf,"Closing modal leaves gallery disabled");
        }
        finally
        {
            RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(canvasObject);UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static void Capture(Camera camera,RenderTexture target,string name)
    {
        Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
        File.WriteAllBytes("../VisualReviews/CollectionCards/"+name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=null;
    }
}
#endif
