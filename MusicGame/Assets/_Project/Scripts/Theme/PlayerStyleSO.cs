using UnityEngine;

/// <summary>
/// Player VISUAL content for one Theme layer — never gameplay logic (CharacterController/
/// PlayerController stay exactly as they are). PlayerController currently builds a hardcoded
/// procedural capsule as its own visual (see PlayerController.BuildVisual) — swapping that for a
/// PlayerVisualAnchor + swappable prefab is real refactor work reserved for the "Player Theme"
/// phase; this is purely the DATA field that work will consume.
/// </summary>
[CreateAssetMenu(fileName = "PlayerStyle", menuName = "MusicGame/Theme/Player Style")]
public class PlayerStyleSO : ScriptableObject
{
    [Tooltip("Null = keep PlayerController's current built-in placeholder visual until the Player " +
             "Theme phase actually wires this up.")]
    public GameObject playerVisualPrefab;
}
