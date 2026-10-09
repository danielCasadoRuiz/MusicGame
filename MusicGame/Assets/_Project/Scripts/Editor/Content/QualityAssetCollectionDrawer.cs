#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for QualityAssetCollection: one row per Unity quality level (names straight from
/// Project Settings → Quality, never typed by hand):
///     Low      [ Prefab Reference ]
///     Mid      [ Prefab Reference ]
///     HighMid  …
/// Missing rows are created on demand; entries with an unknown quality name or a duplicate name are
/// listed below with a warning (and can be removed with one click).
/// </summary>
[CustomPropertyDrawer(typeof(QualityAssetCollection))]
public class QualityAssetCollectionDrawer : PropertyDrawer
{
    private const float Pad = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight + Pad;
        if (!property.isExpanded) return line;
        var problems = Problems(property.FindPropertyRelative("variants"));
        return line * (1 + QualitySettings.names.Length + problems.Count) + (problems.Count > 0 ? line : 0f);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var variants = property.FindPropertyRelative("variants");
        var names = QualitySettings.names;
        float h = EditorGUIUtility.singleLineHeight;
        var row = new Rect(position.x, position.y, position.width, h);

        int assigned = 0;
        foreach (var n in names) { var e = Find(variants, n); if (e != null && HasRef(e)) assigned++; }
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, $"{label.text}  ({assigned}/{names.Length} quality variants)", true);
        if (!property.isExpanded) return;

        EditorGUI.indentLevel++;
        foreach (var quality in names)
        {
            row.y += h + Pad;
            var entry = Find(variants, quality) ?? Add(variants, quality);
            EditorGUI.PropertyField(row, entry.FindPropertyRelative("prefab"), new GUIContent(quality));
        }

        var problems = Problems(variants);
        if (problems.Count > 0)
        {
            row.y += h + Pad;
            EditorGUI.HelpBox(row, "Entries not matching a configured quality level (or duplicated):", MessageType.Warning);
            foreach (var (index, why) in problems)
            {
                row.y += h + Pad;
                var r = row; r.width -= 70f;
                EditorGUI.LabelField(r, why);
                if (GUI.Button(new Rect(r.xMax + 4f, row.y, 66f, h), "Remove"))
                {
                    variants.DeleteArrayElementAtIndex(index);
                    break;
                }
            }
        }
        EditorGUI.indentLevel--;
    }

    private static SerializedProperty Find(SerializedProperty variants, string quality)
    {
        for (int i = 0; i < variants.arraySize; i++)
        {
            var e = variants.GetArrayElementAtIndex(i);
            if (string.Equals(e.FindPropertyRelative("qualityName").stringValue, quality, System.StringComparison.OrdinalIgnoreCase)) return e;
        }
        return null;
    }

    private static SerializedProperty Add(SerializedProperty variants, string quality)
    {
        int i = variants.arraySize;
        variants.InsertArrayElementAtIndex(i);
        var e = variants.GetArrayElementAtIndex(i);
        e.FindPropertyRelative("qualityName").stringValue = quality;
        var guid = e.FindPropertyRelative("prefab.m_AssetGUID");
        if (guid != null) guid.stringValue = "";
        return e;
    }

    private static bool HasRef(SerializedProperty entry)
    {
        var guid = entry.FindPropertyRelative("prefab.m_AssetGUID");
        return guid != null && !string.IsNullOrEmpty(guid.stringValue);
    }

    /// <summary>(array index, description) of entries with an unknown or duplicated quality name.</summary>
    public static List<(int, string)> Problems(SerializedProperty variants)
    {
        var list = new List<(int, string)>();
        var names = QualitySettings.names;
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < variants.arraySize; i++)
        {
            string q = variants.GetArrayElementAtIndex(i).FindPropertyRelative("qualityName").stringValue;
            if (QualityAssetResolver.IndexOf(names, q) < 0) list.Add((i, $"'{q}': unknown quality level"));
            else if (!seen.Add(q)) list.Add((i, $"'{q}': duplicate entry (the first one is used)"));
        }
        return list;
    }
}
#endif
