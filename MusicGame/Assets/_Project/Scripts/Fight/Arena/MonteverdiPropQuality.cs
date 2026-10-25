using UnityEngine;

/// <summary>Selects one authored mesh quality, using the project's existing quality names.</summary>
public sealed class MonteverdiPropQuality : MonoBehaviour
{
    public GameObject[] variants = new GameObject[5]; // Low, Mid, HighMid, High, Ultra
    static readonly string[] Names = { "Low", "Mid", "HighMid", "High", "Ultra" };
    int last = -1;
    void OnEnable() => ApplyCurrent();
    void Update() { if (last != QualitySettings.GetQualityLevel()) ApplyCurrent(); }
    public void ApplyCurrent()
    {
        last = QualitySettings.GetQualityLevel();
        int selected = System.Array.IndexOf(Names, QualitySettings.names[last]);
        if (selected < 0) selected = 1;
        for (int i = 0; i < variants.Length; i++)
            if (variants[i] != null && variants[i].activeSelf != (i == selected)) variants[i].SetActive(i == selected);
    }
}
