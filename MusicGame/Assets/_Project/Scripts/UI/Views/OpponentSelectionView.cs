using TMPro;
using UnityEngine;

/// <summary>
/// Plain reference holder for the Opponent Selection PREFAB (Assets/_Project/Prefabs/UI/
/// OpponentSelection.prefab, built via Tools > MusicGame > Build UI Prefabs). Only the STATIC
/// chrome (title, empty grid container) is baked — the rival cells themselves stay dynamic
/// runtime population (added into gridRoot by OpponentSelectionController, one per
/// OpponentRosterSO.opponents entry), same "bake the shell, populate the list at runtime" split as
/// SongSelectionView/SongSelectionController.
/// </summary>
public class OpponentSelectionView : MonoBehaviour
{
    [Header("Root")]
    public GameObject      root;
    public TextMeshProUGUI titleText;

    [Header("Grid (cells added dynamically at runtime, one per roster entry)")]
    public RectTransform gridRoot;
}
