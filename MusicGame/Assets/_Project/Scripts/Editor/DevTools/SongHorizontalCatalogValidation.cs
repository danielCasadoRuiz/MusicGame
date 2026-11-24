#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class SongHorizontalCatalogValidation
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Verify()
    {
        if(AssetDatabase.LoadAssetAtPath<SongSelectionView>(SongSelectionSkinSetup.ScreenPath).GetComponent<SongSelectionShowcase>()!=null)
        {SongShowcaseReview.Run();return;}
        bool hadMode=PlayerPrefs.HasKey(AppearancePreference.PrefKey);
        string savedMode=AppearancePreference.Mode;
        var holder=new GameObject("Catalogue validation",typeof(RectTransform),typeof(Canvas));
        var canvas=holder.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        try
        {
            AppearancePreference.SetMode(AppearancePreference.Light);AppearancePreference.Tick(10);
            var screen=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SongSelectionView>(SongSelectionSkinSetup.ScreenPath),holder.transform);
            UIFactory.Stretch((RectTransform)screen.transform);screen.gameObject.SetActive(true);screen.FitContent();Canvas.ForceUpdateCanvases();
            var carousel=screen.GetComponentInChildren<SongCatalogCarousel>();
            var prefab=AssetDatabase.LoadAssetAtPath<SongRowView>(SongSelectionSkinSetup.RowPath);
            var rows=new List<SongRowView>();var decades=new List<int>();
            for(int i=0;i<14;i++){var row=UnityEngine.Object.Instantiate(prefab,screen.songListRoot);row.label.text="Track "+i;rows.Add(row);decades.Add(i<4?1970:1990);}
            int selected=-1;carousel.Setup(rows,index=>selected=index,decades);Canvas.ForceUpdateCanvases();
            Check(carousel.VisibleCount==14,"Catalogue lost entries");
            Check(((RectTransform)rows[0].transform).anchoredPosition.x==0,"First entry not at start");
            Check(((RectTransform)rows[1].transform).anchoredPosition.y<0,"Second row missing");
            Check(((RectTransform)rows[2].transform).anchoredPosition.x>0,"Next column not to right");
            carousel.Filter(1970);Check(carousel.VisibleCount==4&&selected==-1,"1970 filter auto-selected a song");
            carousel.MoveTo(0);Check(selected==0,"Explicit selection failed");
            Check(!rows[4].gameObject.activeSelf,"Other decade remains visible");
            carousel.Filter(1990);Check(carousel.VisibleCount==10&&selected==0&&carousel.Center==0,"Other decade lost the selected song");
            carousel.Filter(2020);Check(carousel.VisibleCount==0&&selected==0&&carousel.Center==0,"Empty filter lost the selected song");
            carousel.Filter(0);Check(carousel.VisibleCount==14&&selected==0&&carousel.Center==0,"All filter lost the selected song");
            var scroll=carousel.GetComponent<ScrollRect>();
            var mask=scroll.viewport.GetComponent<RectMask2D>();
            Check(mask!=null&&mask.padding==Vector4.zero&&mask.softness==Vector2Int.zero,"Catalog must clip exactly at viewport edges without mask expansion");
            Check(scroll.horizontal&&!scroll.vertical&&scroll.movementType==ScrollRect.MovementType.Clamped,"Incorrect scroll configuration");
            Check(scroll.GetComponent<Image>().color.a==0,"Exterior panel visible");
            scroll.horizontalNormalizedPosition=1;Check(Mathf.Abs(scroll.content.anchoredPosition.x+carousel.MaxOffset)<1,"Right bound wrong");
            scroll.horizontalNormalizedPosition=0;Check(Mathf.Abs(scroll.content.anchoredPosition.x)<1,"Left bound wrong");
            var lib=rows[0].skin.library;
            Check(lib.songCards.Count==16,"Expected sixteen independently authored card textures");
            Check(screen.selectedSongText!=null&&screen.previewProgressFill!=null,"Missing persistent selection/preview details");
            Check(screen.customSurface!=null&&!screen.customSurface.diagonal&&screen.playYourSongButton.image.sprite==null,"Custom Songs should use a neutral style gradient, not stretched button art");
            foreach(var tab in carousel.decadeButtons)
            {
                Check(tab.GetComponent<RectangularButtonMesh>().diagonal&&tab.GetComponent<RectangularStyleButton>()!=null,"Decade tab must use diagonal current-style button texture");
                var caption=tab.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                Check((caption.fontStyle&TMPro.FontStyles.Bold)!=0&&caption.fontWeight==TMPro.FontWeight.Bold&&!caption.text.Contains("("),"Filter caption must be bold and have no count");
            }
            var lastTab=(RectTransform)carousel.decadeButtons[6].transform;
            Check(Mathf.Abs(lastTab.anchoredPosition.x+lastTab.rect.width-1540)<1,"Filters do not span the catalog width");
            Check(!screen.spotifyButton.transform.parent.gameObject.activeSelf,"Coming-soon services must be removed from the screen");
            var back=(RectTransform)screen.backButton.transform;var play=(RectTransform)screen.playButton.transform;
            float gap=play.anchoredPosition.x-play.rect.width*.5f-(back.anchoredPosition.x+back.rect.width*.5f);
            Check(gap>=24&&gap<=60&&play.rect.width>back.rect.width,"Bottom actions must be grouped with Play dominant");
            Check(screen.selectedAppearance!=null&&lib.songCardAppearanceMaterial!=null&&lib.songCardAppearanceMaterial.shader.isSupported&&screen.selectedSongText.fontSizeMax>=60,"Selected song must share the day/night card renderer");
            Check(Mathf.Abs(screen.selectedCard.rectTransform.rect.width-screen.catalogSupport.rectTransform.rect.width)<1,"Selected nameplate must match the catalog panel width");
            Check(Mathf.Abs(screen.selectedCard.rectTransform.rect.width-1540)<1,"Nameplate and cards must share edges without horizontal panel padding");
            Check(screen.customPurchaseDialog!=null&&screen.customPurchaseDialog.transform.IsChildOf(screen.transform),"Purchase dialog is not local to the selector");
            Check(screen.customPurchaseDialog.GetComponentInParent<JukeboxView>()==null,"Purchase dialog belongs to Jukebox");
            var importRect=(RectTransform)screen.playYourSongButton.transform;var panelRect=(RectTransform)scroll.transform;
            Check(Mathf.Abs(importRect.rect.width-panelRect.rect.width)<1,"Custom Songs button not full width");
            Check(Mathf.Abs(importRect.anchoredPosition.y-(panelRect.anchoredPosition.y-panelRect.rect.height-38))<1,"Custom Songs needs breathing room below the catalog");
            var support=screen.catalogSupport.rectTransform;
            Check(Mathf.Abs(importRect.anchoredPosition.y-importRect.rect.height-(support.anchoredPosition.y-support.rect.height))<1,"Custom Songs must align with the panel bottom");
            Check(screen.catalogSupport.color.a==0&&!screen.catalogSupport.GetComponent<Outline>().enabled,"Catalog support must be fully transparent");
            Check(play.anchoredPosition.y==40&&Mathf.Abs(back.anchoredPosition.y+back.rect.height*.5f-play.anchoredPosition.y-play.rect.height*.5f)<1,"Actions should sit lower on the same centre line");
            var track=(RectTransform)carousel.positionTrack.transform;
            Check(Mathf.Abs(track.rect.width-panelRect.rect.width)<1&&track.anchoredPosition.y==-8&&track.rect.height==4,"Catalogue indicator must be full width and four pixels high above Custom Songs");
            var preview=(RectTransform)screen.previewProgressRoot.transform;
            Check(Mathf.Abs(preview.rect.width-screen.selectedCard.rectTransform.rect.width)<1&&preview.rect.height<=2,"Preview progress must span the nameplate edge to edge");
            Check(screen.selectedSongText.fontSizeMax>=40&&preview.anchoredPosition.y<screen.selectedSongText.rectTransform.anchoredPosition.y&&screen.selectedCard.rectTransform.anchoredPosition.y>panelRect.anchoredPosition.y,"Selected nameplate must appear above the catalog with progress below its text");
            rows[0].SetSelection(true,GameMusicStyle.Electronic);Check(!rows[0].selectionFrame.activeSelf&&rows[0].background.sprite==lib.SongCard(GameMusicStyle.Electronic).sprite,"Selection must switch the complete background without an overlay frame");
            rows[0].button.interactable=false;rows[0].playPauseButton.interactable=false;rows[0].SetPlayed(true);rows[0].SetSelection(false,GameMusicStyle.Electronic);
            Check(rows[0].GetComponent<CanvasGroup>().alpha>.6f&&!rows[0].playedBadge.gameObject.activeSelf&&rows[0].completedLabel.gameObject.activeSelf,"Completed song is unreadable or still shows a history check");
            rows[0].SetPreview(true);Check(rows[0].equalizer.gameObject.activeSelf&&rows[0].equalizerBars.Length==4,"Active Preview is missing its equalizer");
            Check(!rows[0].playPauseButton.gameObject.activeSelf&&rows[0].equalizer.parent==rows[0].transform,"Preview footer must be removed and equalizer attached to card");
            Check(rows[0].equalizer.anchorMin==new Vector2(1,0)&&rows[0].equalizer.anchoredPosition.x<0,"Equalizer must be at the bottom right");
            rows[0].SetPreview(false);Check(!rows[0].equalizer.gameObject.activeSelf,"Equalizer remains after Preview");
            Check(rows[0].selectionRivet!=null&&rows[0].selectionRivet.simple&&screen.catalogSupport!=null,"Simple tinted selection frame or subtle catalog support missing");
            rows[0].button.interactable=true;rows[0].playPauseButton.interactable=true;
            foreach(var skin in lib.menuBackgrounds)
            {
                rows[0].SetSelection(false,skin.style);rows[1].SetSelection(true,skin.style);
                var card=lib.SongCard(skin.style);
                Check(card!=null&&card.sprite!=null&&card.normal!=null&&card.normal!=card.sprite,"Missing distinct normal and selected images "+skin.style);
                Check(rows[0].GetComponent<SongCardAppearance>().LightSurfaceWeight==1&&rows[1].GetComponent<SongCardAppearance>().LightSurfaceWeight==0,"Wrong daytime card states "+skin.style);
                Check(rows[0].GetComponent<SongCardSurfaceMesh>()!=null&&rows[0].background.type==Image.Type.Simple,"Card must fit its texture without stretching "+skin.style);
                Check(rows[0].label.color.r<.1f,"Normal card must have dark legible text "+skin.style);
                Check(!rows[1].genreBadge.enabled&&!rows[1].styleWatermark.gameObject.activeSelf,"Generic overlays remain");
                Check((rows[1].label.fontStyle&TMPro.FontStyles.Bold)!=0,"Title is not bold");
                Check(card.darkInk?rows[1].label.color.r<.1f:rows[1].label.color.r>.9f,"Wrong text contrast "+skin.style);
                Check(rows[1].label.color==lib.SongCardInk(skin.style,true),"Selected-card ink must follow the shared style configuration "+skin.style);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(holder);
            AppearancePreference.SetMode(savedMode);AppearancePreference.Tick(10);
            if(!hadMode){PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);PlayerPrefs.Save();}
        }
    }
}
#endif
