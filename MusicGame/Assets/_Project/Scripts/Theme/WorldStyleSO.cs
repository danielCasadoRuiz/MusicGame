using UnityEngine;

/// <summary>
/// World content for one Theme layer. Deliberately just wraps the EXISTING, already-tuned
/// HorizonConfig (this project's real world/environment/sky/water/mountain system) rather than
/// reinventing generic horizon/fog/lighting fields — the current Horizon World becomes BaseTheme's
/// World content as-is (see BaseTheme's own asset), and a future World (city, space, desert...)
/// is just a different HorizonConfig-compatible asset assigned here, or this field's type growing
/// into a small interface if a genuinely different World representation is ever needed.
///
/// Not yet consumed by HorizonWorld/GameplayManager (that wiring is a later phase) — this is
/// purely the DATA model for now.
/// </summary>
[CreateAssetMenu(fileName = "WorldStyle", menuName = "MusicGame/Theme/World Style")]
public class WorldStyleSO : ScriptableObject
{
    public HorizonConfig horizon;
}
