using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Provisional, functional results block: a vertical list of rows, each a text line with an optional
/// StarRatingView on its right. Rebuilt per Show (Clear + Add*) — used by the runner end screen, the
/// match result screen and the Progression detail. Text goes through Loc where it is UI text.
///
/// LAYOUT is Unity's own (VerticalLayoutGroup + ContentSizeFitter on the panel, HorizontalLayoutGroup per
/// row): a line that wraps simply makes its row taller, and the panel grows with its rows. Nothing is
/// measured by hand, so building the panel while its screen is still hidden is fine — Unity lays it out
/// when it is shown.
/// </summary>
public class EvaluationPanel : MonoBehaviour
{
    private RectTransform _root;
    private readonly List<GameObject> _rows = new();
    private const float RowMinHeight = 30f;
    private static readonly Vector2 StarsSize = new(22f * 5f + 3f * 4f, 22f);

    public static EvaluationPanel Create(RectTransform parent, string name, float width)
    {
        var rt = UIFactory.CreateRect(name, parent);
        rt.sizeDelta = new Vector2(width, 0f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;
        var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // the width stays the given one
        var panel = rt.gameObject.AddComponent<EvaluationPanel>();
        panel._root = rt;
        return panel;
    }

    public RectTransform Rect => _root;

    public void Clear()
    {
        foreach (var r in _rows)
            if (r != null) { r.SetActive(false); Destroy(r); } // inactive at once: the layout ignores it this frame
        _rows.Clear();
    }

    public void AddHeader(string text, Color? color = null) => AddRow(text, -2, 20, color ?? new Color(1f, 0.82f, 0.35f), FontStyles.Bold);

    public void AddLine(string text, int fontSize = 16, Color? color = null) => AddRow(text, -2, fontSize, color ?? Color.white, FontStyles.Normal);

    /// <summary>A line with stars (halfStars -1 = pending).</summary>
    public void AddStars(string text, int halfStars, int fontSize = 16) => AddRow(text, halfStars, fontSize, Color.white, FontStyles.Normal);

    private void AddRow(string text, int halfStars, int fontSize, Color color, FontStyles style)
    {
        var row = UIFactory.CreateRect("Row", _root);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        row.gameObject.AddComponent<LayoutElement>().minHeight = Mathf.Max(RowMinHeight, fontSize + 10f);

        var label = UIFactory.CreateText("Label", row, text, fontSize, color, TextAlignmentOptions.MidlineLeft, style);
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f; // takes the rest of the row and wraps inside it

        if (halfStars >= -1)
        {
            var view = StarRatingView.Create(row, 22f, 3f);
            var le = view.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = StarsSize.x;
            le.minHeight = le.preferredHeight = StarsSize.y;
            view.SetHalfStars(halfStars);
        }
        _rows.Add(row.gameObject);
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
