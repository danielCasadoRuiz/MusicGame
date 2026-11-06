#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;
public static class VariantInventory {
public static void Run(){try{var rows=new System.Collections.Generic.List<string>();foreach(var c in new[]{"Bach","Beethoven","Brahms","Chopin","Handel","Haydn","Monteverdi","Mozart","Pachelbel","Tchaikovsky","Vivaldi","Wagner"}){var r=PrefabUtility.LoadPrefabContents("Assets/_Project/Art/Arenas/"+c+"/"+c+"_Arena_01.prefab");try{foreach(var t in r.GetComponentsInChildren<Transform>(true).Where(t=>t.parent&& (new[]{"North","South","East","West"}.Contains(t.parent.name)||t.parent==r.transform)))rows.Add(c+"|"+t.parent.name+"|"+t.name+"|"+t.position.ToString("F2")+"|"+t.eulerAngles.ToString("F1"));}finally{PrefabUtility.UnloadPrefabContents(r);}}File.WriteAllLines(@"C:\Users\VISYON\.codex\.chatgpt-projects\g-p-6a438d8b08888191a78acab089f6e907\scenario_proposals\inventory.txt",rows);EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
#endif
