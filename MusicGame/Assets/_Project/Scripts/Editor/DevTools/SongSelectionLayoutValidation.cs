#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class SongSelectionLayoutValidation
{
    [MenuItem("Tools/MusicGame/UI/Validate Song Selection Layout")]
    public static void Verify()
    {
        var view=AssetDatabase.LoadAssetAtPath<SongSelectionView>(SongSelectionSkinSetup.ScreenPath);
        if(view.selectedCard==null||view.selectedCard.rectTransform.anchoredPosition.y<=-250)
            throw new Exception("Selected-song nameplate must be below the title and above the catalog.");
        if(view.previewProgressRoot.GetComponent<RectTransform>().rect.height>2)
            throw new Exception("Preview progress must remain a hairline.");
        foreach(var item in view.GetComponentInChildren<SongCatalogCarousel>(true).decadeButtons)
            if(item.GetComponent<RectangularStyleButton>()==null||!item.GetComponent<RectangularButtonMesh>().diagonal)
                throw new Exception("Decade filters must retain the active skin texture and diagonal shape.");
        Debug.Log("Song selection layout validated.");
    }
}
#endif
