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
        if(FindProperty("_SkinTone",properties,false)!=null)
        {
            EditorGUILayout.LabelField("Avatar — skin tone",EditorStyles.boldLabel);
            foreach(var name in new[]{"_SkinToneEnabled","_SkinTone","_SkinUndertone","_SkinSoftness","_SkinWrap","_SkinScatterColor"})
            {var p=FindProperty(name,properties,false);if(p!=null)editor.ShaderProperty(p,p.displayName);}
        }
        if(FindProperty("_NailColor0",properties,false)!=null)
        {
            EditorGUILayout.LabelField("Nails",EditorStyles.boldLabel);
            var mode=FindProperty("_NailMode",properties);editor.ShaderProperty(mode,mode.displayName);
            if(mode.floatValue>.5f && mode.floatValue<1.5f)
                for(int i=0;i<4;i++)
                {
                    var enabled=FindProperty("_NailGroup"+i+"Enabled",properties);editor.ShaderProperty(enabled,enabled.displayName);
                    if(enabled.floatValue>.5f){var color=FindProperty("_NailGroupColor"+i,properties);editor.ShaderProperty(color,color.displayName);}
                }
            else if(mode.floatValue>=1.5f)
                for(int i=0;i<20;i++){var color=FindProperty("_NailColor"+i,properties);editor.ShaderProperty(color,color.displayName);}
        }
        EditorGUILayout.LabelField("Avatar — iris gradient", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("One shared skin/face/eyes atlas. Tint affects only the two iris UV circles. " +
            "FaceProfile can override the three colors per character.", MessageType.Info);
        foreach (string name in new[] { "_IrisEnabled", "_IrisInnerColor", "_IrisMainColor", "_IrisOuterColor",
                     "_IrisInnerRadius", "_IrisOuterRadius", "_IrisDetailGain", "_EyeSmoothness", "_IrisPupilRadius", "_IrisEdgeFeather" })
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
