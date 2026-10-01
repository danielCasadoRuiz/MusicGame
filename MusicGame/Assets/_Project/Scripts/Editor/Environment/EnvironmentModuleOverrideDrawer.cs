using UnityEditor;
using UnityEngine;

/// <summary>
/// One line per module slot: [Label] [INHERIT / OVERRIDE / DISABLED] [Prefab] — the prefab field is
/// only editable in Override mode and shows what the other two modes mean instead.
/// </summary>
[CustomPropertyDrawer(typeof(EnvironmentModuleOverride))]
public class EnvironmentModuleOverrideDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var mode = property.FindPropertyRelative("mode");
        var prefab = property.FindPropertyRelative("prefab");

        EditorGUI.BeginProperty(position, label, property);
        var content = EditorGUI.PrefixLabel(position, label);
        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        var modeRect = new Rect(content.x, content.y, Mathf.Min(95f, content.width * 0.35f), content.height);
        var prefabRect = new Rect(modeRect.xMax + 4f, content.y, content.width - modeRect.width - 4f, content.height);

        var current = (EnvironmentOverrideMode)mode.enumValueIndex;
        var color = GUI.color;
        GUI.color = current switch
        {
            EnvironmentOverrideMode.Override => new Color(0.7f, 1f, 0.7f),
            EnvironmentOverrideMode.Disabled => new Color(1f, 0.7f, 0.7f),
            _                                => color,
        };
        mode.enumValueIndex = (int)(EnvironmentOverrideMode)EditorGUI.EnumPopup(modeRect, current);
        GUI.color = color;

        if (current == EnvironmentOverrideMode.Override)
        {
            EditorGUI.PropertyField(prefabRect, prefab, GUIContent.none);
            if (prefab.objectReferenceValue == null)
                EditorGUI.LabelField(new Rect(prefabRect.x, prefabRect.y, prefabRect.width - 20f, prefabRect.height),
                                     new GUIContent("", "No prefab — the base module will be used (with a warning)."));
        }
        else
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.LabelField(prefabRect, current == EnvironmentOverrideMode.Inherit ? "→ base environment's module" : "→ nothing is instantiated");
        }

        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }
}
