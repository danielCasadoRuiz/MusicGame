using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Results block (runner end screen, match result, Progression detail): prefab
/// Prefabs/UI/Components/EvaluationPanel.prefab (background + VerticalLayoutGroup + ContentSizeFitter, its
/// default anchoring) and one prefab per ROW KIND (EvaluationRow_Header / Line / Note / Highlight /
/// Warning / Stars / StarsSmall — font size, colour, spacing and star size live there). This class only
/// instantiates rows and fills texts/stars; Unity's layout sizes them (a wrapped line makes its row taller),
/// so building it while the screen is still hidden is safe.
/// </summary>
public class EvaluationPanel : MonoBehaviour
{
    [SerializeField] private EvaluationRowView headerRow;
    [SerializeField] private EvaluationRowView lineRow;
    [SerializeField] private EvaluationRowView noteRow;
    [SerializeField] private EvaluationRowView highlightRow;
    [SerializeField] private EvaluationRowView warningRow;
    [SerializeField] private EvaluationRowView starsRow;
    [SerializeField] private EvaluationRowView starsSmallRow;

    private readonly List<GameObject> _rows = new();

    public RectTransform Rect => (RectTransform)transform;

    /// <summary>Instantiates the library's EvaluationPanel prefab under `parent` (its anchoring comes from the prefab).</summary>
    public static EvaluationPanel Spawn(Transform parent, string name)
    {
        var panel = UIPrefabLibrarySO.Spawn(UIPrefabLibrarySO.Instance != null ? UIPrefabLibrarySO.Instance.evaluationPanel : null, parent, "EvaluationPanel");
        if (panel != null) panel.name = name;
        return panel;
    }

    public void Clear()
    {
        foreach (var r in _rows)
            if (r != null) { r.SetActive(false); Destroy(r); } // inactive at once: the layout ignores it this frame
        _rows.Clear();
    }

    public void AddHeader(string text) => Add(headerRow, "Header", text, -2);
    public void AddLine(string text) => Add(lineRow, "Line", text, -2);
    /// <summary>Small secondary text (metric breakdown, explanations).</summary>
    public void AddNote(string text) => Add(noteRow, "Note", text, -2);
    /// <summary>Emphasised line (new record, mastered, level completed…).</summary>
    public void AddHighlight(string text) => Add(highlightRow, "Highlight", text, -2);
    public void AddWarning(string text) => Add(warningRow, "Warning", text, -2);
    /// <summary>A line with stars (halfStars -1 = pending).</summary>
    public void AddStars(string text, int halfStars) => Add(starsRow, "Stars", text, halfStars);
    public void AddStarsSmall(string text, int halfStars) => Add(starsSmallRow, "StarsSmall", text, halfStars);

    private void Add(EvaluationRowView prefab, string kind, string text, int halfStars)
    {
        if (string.IsNullOrEmpty(text) && halfStars < -1) return;
        if (prefab == null) { Debug.LogError($"[EvaluationPanel] '{name}': row prefab '{kind}' not assigned."); return; }
        var row = Instantiate(prefab, transform, false);
        row.Set(text, halfStars);
        _rows.Add(row.gameObject);
    }

    public void BindRows(EvaluationRowView header, EvaluationRowView line, EvaluationRowView note, EvaluationRowView highlight,
                         EvaluationRowView warning, EvaluationRowView stars, EvaluationRowView starsSmall)
    {
        headerRow = header; lineRow = line; noteRow = note; highlightRow = highlight; warningRow = warning; starsRow = stars; starsSmallRow = starsSmall;
    }

    // ── Shared text helpers ──────────────────────────────────────────────────────

    public static string Points(int score, int max) => $"{score:N0} / {max:N0}";

    // Number only: the stars themselves are drawn by StarRatingView (the UI font has no ★ glyph).
    public static string Stars(int halfStars) => halfStars < 0 ? Loc.Get("Eval.Pending") : StarMath.Label(halfStars);

    /// <summary>Short metric breakdown for a phase record ("Recollides 48/50 · Precisió 92% · Caigudes 1").</summary>
    public static string Breakdown(List<MetricData> metrics)
    {
        if (metrics == null || metrics.Count == 0) return "";
        var parts = new List<string>();
        foreach (var m in metrics)
        {
            switch (m.key)
            {
                case "Collected": parts.Add(Loc.Get("Eval.Collected", $"{(int)m.value}/{(int)m.max}")); break;
                case "Timing": parts.Add(Loc.Get("Eval.Timing", Mathf.RoundToInt(m.value * 100f).ToString())); break;
                case "Falls": parts.Add(Loc.Get("Eval.Falls", ((int)m.value).ToString())); break;
                case "HealthKept": parts.Add(Loc.Get("Eval.HealthKept", Mathf.RoundToInt(m.value * 100f).ToString())); break;
                case "Accuracy": parts.Add(Loc.Get("Eval.Accuracy", Mathf.RoundToInt(m.value * 100f).ToString())); break;
                case "Hits": parts.Add(Loc.Get("Eval.Hits", $"{(int)m.value}/{(int)m.max}")); break;
            }
        }
        return string.Join("  ·  ", parts);
    }
}
