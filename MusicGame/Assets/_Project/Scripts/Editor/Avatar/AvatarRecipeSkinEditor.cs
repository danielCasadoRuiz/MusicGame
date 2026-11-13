using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AvatarRecipeSO)), CanEditMultipleObjects]
public class AvatarRecipeSkinEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // DrawDefaultInspector supports undo, multi-edit and normal asset persistence.
        DrawDefaultInspector();
        foreach (AvatarRecipeSO recipe in targets)
            foreach (var controller in Object.FindObjectsByType<AvatarSkinToneController>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if (controller.SourceRecipe != recipe) continue;
                // Apply only changed authored values, so per-instance sliders stay independent.
                int key = controller.GetInstanceID();
                var current = new Vector2(recipe.skinTone,recipe.skinUndertone);
                if (!_authored.TryGetValue(key,out var previous)) { _authored[key]=current; continue; }
                string nailState=JsonUtility.ToJson(recipe.nails);
                if(!_nails.TryGetValue(key,out var oldNails))_nails[key]=nailState;
                if(oldNails!=null && oldNails!=nailState)
                {
                    controller.nails=recipe.nails?.Copy() ?? new AvatarNailSettings();
                    if(controller.Instance?.Recipe!=null)controller.Instance.Recipe.Nails=controller.nails.Copy();
                    controller.Apply();_nails[key]=nailState;
                }
                if (current == previous) continue;
                _authored[key]=current;
                if (controller.Instance != null)
                {
                    controller.Instance.SetSkinTone(current.x);
                    controller.Instance.SetSkinUndertone(current.y);
                }
                else { controller.SetTone(current.x); controller.SetUndertone(current.y); }
            }
    }
    readonly System.Collections.Generic.Dictionary<int,string> _nails = new();
    readonly System.Collections.Generic.Dictionary<int,Vector2> _authored = new();
}
