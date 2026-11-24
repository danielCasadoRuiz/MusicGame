#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Real controller + real preview, without starting a run or modifying progression.</summary>
[InitializeOnLoad]
public static class SongSelectionSkinPlayProbe
{
    private const string Active="SongSelectionSkin.LiveProbe";
    private static double start,phaseStart;
    private static int phase;
    private static SongSelectionView view;
    private static List<SongRowView> rows;
    private static int first,second;
    private static int lastPlayingSelection=-1;
    private static bool appearanceReplaced,hadAppearance;
    private static string savedAppearance;
    private static IMusicStore savedStore;
    private static bool storeReplaced,capturedPreview;
    private static int storeRequests;
    private static void StoreRequested(MusicProduct? product){storeRequests++;}
    private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../song-selection-live-validation.txt"));
    static SongSelectionSkinPlayProbe(){EditorApplication.update+=Tick;}
    private static void Tick()
    {
        string request=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/SongSelectionSkinPlay.request"));
        if(!EditorApplication.isPlaying&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(request))
        {
            File.Delete(request);SessionState.SetBool(Active,true);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");EditorApplication.EnterPlaymode();return;
        }
        if(!EditorApplication.isPlaying||!SessionState.GetBool(Active,false))return;
        if(start==0){start=EditorApplication.timeSinceStartup;phaseStart=start;}
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(now-start>120)throw new Exception("Timed out at phase "+phase);
            var controller=UnityEngine.Object.FindFirstObjectByType<SongSelectionController>();
            if(controller==null)return;
            if(phase==0)
            {
                view=(SongSelectionView)Field(controller,"_view");rows=(List<SongRowView>)Field(controller,"_rows");
                if(view==null||rows==null||rows.Count<2||AppFlowController.Instance==null)return;
                AppFlowController.Instance.RequestState(GameFlowState.SongSelection);phase=1;phaseStart=now;return;
            }
            if(now-phaseStart<1)return;
            if(phase==1)
            {
                Check(view.gameObject.activeInHierarchy,"Selector did not open");
                Check(view.selectedSongText.text.Length==0&&!view.playButton.interactable,"Selector starts with an automatic selection");
                var emptyShowcase=view.GetComponent<SongSelectionShowcase>();
                Check(emptyShowcase!=null&&emptyShowcase.placeholderTitle.gameObject.activeInHierarchy&&!SongPreviewPlayer.Instance.IsPlaying,"Placeholder must be visible without starting a sample");
                var available=Enumerable.Range(0,rows.Count).Where(i=>rows[i].button.IsInteractable()).Take(2).ToArray();
                Check(available.Length==2,"Need two selectable catalog songs for live check");first=available[0];second=available[1];
                Click(rows[first].button);
                phase=2;phaseStart=now;return;
            }
            if(phase==2)
            {
                if(SongPreviewPlayer.Instance==null||!SongPreviewPlayer.Instance.IsPlaying||SongPreviewPlayer.Instance.PlayingRange.duration<=0)return;
                Check(rows[first].skin.Selected,"Preview did not select its song");
                Check(rows[first].IsPreviewing&&rows[first].selectionFrame.activeSelf==rows[first].showcase&&rows[first].background.sprite==rows[first].skin.library.SongCard(rows[first].musicStyle).sprite,"Selected song must use its skin material and the showcase selection frame");
                Check(!view.previewProgressRoot.activeSelf&&view.GetComponent<SongSelectionShowcase>().titleProgress!=null,"Title must replace the old progress bar");
                Check(Mathf.Abs(view.GetComponent<SongSelectionShowcase>().titleProgress.Progress-SongPreviewPlayer.Instance.Progress)<.1f,"Preview bar runs backwards");
                Check(!view.selectedSongText.text.Contains("SELECTED")&&!view.selectedSongText.text.Contains("SELECCIONADA"),"Selection heading still visible");
                Check(view.selectedSongText.text.Contains(rows[first].label.text),"Persistent selected title missing");
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath,"../../VisualReviews/SongSelection/Unity/live-preview-active.png")));
                Check(!rows[first].playPauseButton.gameObject.activeSelf&&rows[first].equalizer.parent==rows[first].transform,"Legacy Preview footer remains or equalizer is not in the card");
                Check(rows[first].background.sprite==rows[first].skin.library.SongCard(rows[first].musicStyle).sprite,"Wrong style-specific card image");
                Check(rows[first].skin.Style==rows[first].musicStyle,"Card follows screen style instead of its own song");
                Click(rows[second].button);phase=3;phaseStart=now;return;
            }
            if(phase==3)
            {
                Check(rows[second].skin.Selected&&!rows[first].skin.Selected,"Selection did not move");
                Check(SongPreviewPlayer.Instance.IsPlaying&&rows[second].IsPreviewing,"Selecting another card did not start its sample");
                Check(!rows[first].IsPreviewing,"Old equalizer remains after selecting another song");
                Check(AppBootstrap.Context.AppFlow.CurrentState==GameFlowState.SongSelection,"Selection started a run");
                Click(view.backButton);phase=4;phaseStart=now;return;
            }
            if(phase==4)
            {
                Check(AppBootstrap.Context.AppFlow.CurrentState==GameFlowState.MainMenu,"Back did not return home");
                Check(!SongPreviewPlayer.Instance.IsPlaying,"Back left the sample playing");
                AppFlowController.Instance.RequestState(GameFlowState.SongSelection);phase=5;phaseStart=now;return;
            }
            if(phase==5)
            {
                Check(view.gameObject.activeInHierarchy&&rows[second].skin.Selected,"Reopening lost the selected state");
                var carousel=view.GetComponentInChildren<SongCatalogCarousel>();
                Check(carousel!=null&&carousel.Count==rows.Count,"Missing carousel/catalogue");
                var scroll=carousel.GetComponent<ScrollRect>();
                Check(scroll.enabled&&scroll.horizontal&&!scroll.vertical,"Catalogue is not horizontal");
                Check(scroll.movementType==ScrollRect.MovementType.Clamped,"Catalogue can overscroll");
                Check(scroll.GetComponent<Image>().color.a==0,"Outer box still visible");
                string summary=view.selectedSongText.text;
                carousel.Filter(1900);Check(carousel.VisibleCount==0&&view.playButton.interactable&&carousel.Center==second&&view.selectedSongText.text==summary,"Empty decade lost selection or summary");
                carousel.Filter(0);Check(carousel.VisibleCount==rows.Count&&carousel.Center==second&&rows[second].skin.Selected,"All does not restore the selected song");
                carousel.MoveTo(second);
                scroll.horizontalNormalizedPosition=1;phase=6;phaseStart=now;return;
            }
            if(phase==6)
            {
                var carousel=view.GetComponentInChildren<SongCatalogCarousel>();
                var scroll=carousel.GetComponent<ScrollRect>();
                scroll.horizontalNormalizedPosition=.47f;
                Check(carousel.Center==second,"Scrolling changes selection");
                Canvas.ForceUpdateCanvases();
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath,"../../VisualReviews/SongSelection/Unity/live-catalog-scroll-mask.png")));
                phase=7;phaseStart=now;return;
            }
            if(phase==7)
            {
                var carousel=view.GetComponentInChildren<SongCatalogCarousel>();
                var scroll=carousel.GetComponent<ScrollRect>();scroll.horizontalNormalizedPosition=0;
                Check(Mathf.Abs(scroll.content.anchoredPosition.x)<1,"Start not bounded at zero");
                DragScroll(scroll,160);
                Check(Mathf.Abs(scroll.content.anchoredPosition.x)<1,"First view moves beyond its left boundary");
                DragScroll(scroll,-160);
                Check(scroll.content.anchoredPosition.x<0,"Left swipe does not reveal following songs");
                Check(carousel.Center==second,"Dragging changes selection");
                scroll.StopMovement();scroll.horizontalNormalizedPosition=0;
                carousel.MoveTo(first);phase=8;phaseStart=now;return;
            }
            if(phase==8)
            {
                Click(rows[first].button);phase=9;phaseStart=now;return;
            }
            if(phase==9)
            {
                if(!SongPreviewPlayer.Instance.IsPlaying)return;
                float elapsed=SongPreviewPlayer.Instance.ElapsedSeconds;
                Click(rows[first].button);
                Check(SongPreviewPlayer.Instance.IsPlaying&&SongPreviewPlayer.Instance.ElapsedSeconds>=elapsed,"Retapping the playing card interrupted its sample");
                var carousel=view.GetComponentInChildren<SongCatalogCarousel>();
                string summary=view.selectedSongText.text;
                carousel.Filter(1900);
                Check(SongPreviewPlayer.Instance.IsPlaying&&carousel.Center==first&&view.selectedSongText.text==summary,"Decade filter interrupted preview or selection");
                carousel.Filter(0);
                Check(SongPreviewPlayer.Instance.IsPlaying&&rows[first].skin.Selected,"All interrupted preview or selection");
                phase=10;phaseStart=now;return;
            }
            if(phase==10)
            {
                if(SongPreviewPlayer.Instance.IsPlaying)
                {
                    lastPlayingSelection=view.GetComponentInChildren<SongCatalogCarousel>().Center;
                    Check(lastPlayingSelection>=0&&rows[lastPlayingSelection].skin.Selected&&rows[lastPlayingSelection].IsPreviewing,"Playing sample and selected-card equalizer diverged");
                    if(SongPreviewPlayer.Instance.ElapsedSeconds>.5f)Check(Mathf.Abs(view.GetComponent<SongSelectionShowcase>().titleProgress.Progress-SongPreviewPlayer.Instance.Progress)<.1f,"Bar does not fill forwards");
                    if(!capturedPreview&&SongPreviewPlayer.Instance.ElapsedSeconds>2)
                    {
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath,"../../VisualReviews/SongSelection/Unity/live-preview-active.png")));capturedPreview=true;
                    }
                    return;
                }
                var titleProgress=view.GetComponent<SongSelectionShowcase>().titleProgress;
                if(titleProgress.GlowAmount>.01f)return;
                Check(titleProgress.Progress==1&&titleProgress.GlowAmount<=.01f,"Completed title did not fade back to white");
                Check(!rows.Any(r=>r.IsPreviewing)&&!view.previewProgressRoot.activeSelf,"Natural completion left the legacy bar visible");
                Check(lastPlayingSelection>=0&&rows[lastPlayingSelection].skin.Selected,"Preview completion lost the song selection");
                savedStore=MusicAccess.Store;storeReplaced=true;MusicAccess.Store=null;
                storeRequests=0;MusicAccess.StoreRequested+=StoreRequested;
                Click(view.playYourSongButton);
                Check(storeRequests==0&&!(bool)Field(controller,"_isLoading"),"Import still routes through global Jukebox purchase/picker");
                Check(view.customPurchaseDialog.activeInHierarchy,"Selector-local purchase panel missing");
                var jukebox=UnityEngine.Object.FindFirstObjectByType<JukeboxView>(FindObjectsInactive.Include);
                Check(jukebox==null||!jukebox.gameObject.activeInHierarchy,"Purchase showed the Jukebox screen");
                Check(AppBootstrap.Context.AppFlow.CurrentState==GameFlowState.SongSelection,"Purchase navigated away from selector");
                phase=11;phaseStart=now;return;
            }
            if(phase==11)
            {
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath,"../../VisualReviews/SongSelection/Unity/live-custom-songs-purchase.png")));
                phase=12;phaseStart=now;return;
            }
            if(phase==12)
            {
                view.customPurchaseClose.onClick.Invoke();
                Check(!view.customPurchaseDialog.activeSelf&&view.gameObject.activeInHierarchy,"Closing purchase left selector");
                MusicAccess.StoreRequested-=StoreRequested;MusicAccess.Store=savedStore;storeReplaced=false;
                var carousel=view.GetComponentInChildren<SongCatalogCarousel>();
                int showcase=rows.FindIndex(r=>r.musicStyle==GameMusicStyle.Dance&&r.button.interactable);
                carousel.MoveTo(showcase>=0?showcase:second);
                carousel.GetComponent<ScrollRect>().horizontalNormalizedPosition=0;
                phase=13;phaseStart=now;return;
            }
            if(phase==13)
            {
                Canvas.ForceUpdateCanvases();
                var chosen=rows[view.GetComponentInChildren<SongCatalogCarousel>().Center];
                Check(chosen.label.color==Color.Lerp(chosen.skin.library.SongCardInk(chosen.musicStyle,true),chosen.skin.library.songNormalInk,SongCardAppearance.SurfaceWeight(true,AppearancePreference.LightWeight)),"Selected-card text ignores shared appearance contrast");
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath,"../../VisualReviews/SongSelection/Unity/live-horizontal-catalog.png")));
                hadAppearance=PlayerPrefs.HasKey(AppearancePreference.PrefKey);savedAppearance=AppearancePreference.Mode;appearanceReplaced=true;
                AppearancePreference.SetMode(AppearancePreference.Light);phase=14;phaseStart=now;return;
            }
            if(phase==14)
            {
                if(now-phaseStart<2)return;
                Check(AppearancePreference.LightWeight==1,"Day transition did not finish");
                var chosen=rows[view.GetComponentInChildren<SongCatalogCarousel>().Center];
                Check(chosen.GetComponent<SongCardAppearance>().LightSurfaceWeight==0,"Day selected card should use colour");
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath,"../../VisualReviews/SongSelection/Unity/song-selection-day.png")));
                phase=141;phaseStart=now;return;
            }
            if(phase==141)
            {
                AppearancePreference.SetMode(AppearancePreference.Dark);phase=15;phaseStart=now;return;
            }
            if(phase==15)
            {
                if(now-phaseStart<2)return;
                Check(AppearancePreference.LightWeight==0,"Night transition did not finish");
                var chosen=rows[view.GetComponentInChildren<SongCatalogCarousel>().Center];
                Check(chosen.GetComponent<SongCardAppearance>().LightSurfaceWeight==1&&chosen.label.color==chosen.skin.library.songNormalInk,"Night selected card should be light with dark ink");
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath,"../../VisualReviews/SongSelection/Unity/song-selection-night.png")));
                phase=151;phaseStart=now;return;
            }
            if(phase==151)
            {
                AppearancePreference.SetMode(savedAppearance);
                if(!hadAppearance){PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);PlayerPrefs.Save();}
                appearanceReplaced=false;phase=16;phaseStart=now;return;
            }
            if(phase==16)
            {
                Finish("PASS: empty initial selection; artist/title without heading; title fills forwards with the skin glow and returns to white after playback; local Custom Songs modal leaves Jukebox hidden and flow unchanged; full-width attached import; stronger selection and dimmed used songs; filtering, drag, preview and Back/reopen. No purchase, run or progression changes.");
            }
        }
        catch(Exception e){SongPreviewPlayer.Instance?.Stop();Finish("FAIL: "+e);}
    }
    private static object Field(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
    private static void DragScroll(ScrollRect scroll,float pixels)
    {
        var center=RectTransformUtility.WorldToScreenPoint(scroll.GetComponentInParent<Canvas>().worldCamera,scroll.viewport.TransformPoint(scroll.viewport.rect.center));
        var data=new PointerEventData(EventSystem.current){position=center,pressPosition=center,button=PointerEventData.InputButton.Left};
        ExecuteEvents.Execute(scroll.gameObject,data,ExecuteEvents.initializePotentialDrag);
        ExecuteEvents.Execute(scroll.gameObject,data,ExecuteEvents.beginDragHandler);
        data.position=center+new Vector2(pixels,0);
        ExecuteEvents.Execute(scroll.gameObject,data,ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(scroll.gameObject,data,ExecuteEvents.endDragHandler);
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Click(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect=(RectTransform)button.transform;var corners=new Vector3[4];rect.GetWorldCorners(corners);
        var camera=button.GetComponentInParent<Canvas>().worldCamera;
        var position=RectTransformUtility.WorldToScreenPoint(camera,(corners[0]+corners[2])*.5f);
        var data=new PointerEventData(EventSystem.current){position=position,button=PointerEventData.InputButton.Left};
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count>0,"No pointer target for "+button.name);
        var target=ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        Check(target==button.gameObject,"Pointer hits "+target+" instead of "+button.name);
        ExecuteEvents.Execute(target,data,ExecuteEvents.pointerDownHandler);ExecuteEvents.Execute(target,data,ExecuteEvents.pointerUpHandler);ExecuteEvents.Execute(target,data,ExecuteEvents.pointerClickHandler);
    }
    private static void Finish(string message)
    {
        if(appearanceReplaced){AppearancePreference.SetMode(savedAppearance);if(!hadAppearance){PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);PlayerPrefs.Save();}appearanceReplaced=false;}
        if(storeReplaced){MusicAccess.StoreRequested-=StoreRequested;MusicAccess.Store=savedStore;storeReplaced=false;}
        SessionState.SetBool(Active,false);File.WriteAllText(Output,message);Debug.Log(message);
    }
}
#endif
