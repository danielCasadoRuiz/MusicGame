using TMPro;
using UnityEngine;

/// <summary>
/// Plain reference holder for the Countdown screen PREFAB (Assets/_Project/Prefabs/UI/
/// CountdownScreen.prefab, built via Tools > MusicGame > Build UI Prefabs). CountdownController only
/// READS this reference and drives the "3, 2, 1, GO" sequence — it never creates or lays out this UI
/// itself when a prefab instance is wired.
/// </summary>
public class CountdownScreenView : MonoBehaviour
{
    public GameObject root;
    public TextMeshProUGUI numberText;
}
