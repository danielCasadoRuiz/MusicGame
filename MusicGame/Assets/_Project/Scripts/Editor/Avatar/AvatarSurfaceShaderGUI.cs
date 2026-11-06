#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>Keep URP's material/keyword inspector, with the unified surface's iris controls above it.</summary>
public sealed class AvatarSurfaceShaderGUI : ShaderGUI
{
    private ShaderGUI _lit;
    public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
    {
        EditorGUILayout.LabelField("Avatar — iris gradient", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("One shared skin/face/eyes atlas. Tint affects only the two iris UV circles. " +
            "FaceProfile can override the three colors per character.", MessageType.Info);
        foreach (string name in new[] { "_IrisEnabled", "_IrisInnerColor", "_IrisMainColor", "_IrisOuterColor",
                     "_IrisInnerRadius", "_IrisOuterRadius", "_IrisDetailGain", "_EyeSmoothness" })
        {
            var property = FindProperty(name, properties, false);
            if (property != null) editor.ShaderProperty(property, property.displayName);
        }
        if (_lit == null)
        {
            var type = Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader, Unity.RenderPipelines.Universal.Editor");
            if (type != null) _lit = Activator.CreateInstance(type, true) as ShaderGUI;
        }
        if (_lit != null) _lit.OnGUI(editor, properties);
        else editor.PropertiesDefaultGUI(properties);
    }

    public override void ValidateMaterial(Material material)
    {
        // The original surface remains opaque URP Lit; the delegated inspector validates its keywords.
        _lit?.ValidateMaterial(material);
    }
}
#endif
