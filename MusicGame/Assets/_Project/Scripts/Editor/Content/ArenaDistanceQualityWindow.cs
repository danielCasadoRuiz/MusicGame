#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class ArenaDistanceQualityWindow : EditorWindow
{
    const string Config="Assets/_Project/Configs/Editor/arena_distance_quality.json";
    static readonly string[] Qualities={"Low","Mid","HighMid","High","Ultra"};
    ArenaDistanceQuality.Policy policy;
    int selected;
    Vector2 scroll;
    bool exceptions;
    [MenuItem("Tools/MusicGame/Content/Arenas/Distance Quality Settings")]
    public static void Open()=>GetWindow<ArenaDistanceQualityWindow>("Qualitat dels escenaris");
    void OnEnable(){Reload();SceneView.duringSceneGui+=Draw;}
    void OnDisable(){SceneView.duringSceneGui-=Draw;}
    void Reload(){if(File.Exists(Config))policy=JsonUtility.FromJson<ArenaDistanceQuality.Policy>(File.ReadAllText(Config));}
    void OnGUI()
    {
        if(policy?.scenes==null){EditorGUILayout.HelpBox("No s'ha trobat la configuració dels escenaris.",MessageType.Error);return;}
        scroll=EditorGUILayout.BeginScrollView(scroll);
        selected=EditorGUILayout.Popup("Escenari",Mathf.Clamp(selected,0,policy.scenes.Length-1),policy.scenes.Select(s=>s.composer).ToArray());
        var rule=policy.scenes[selected];
        EditorGUILayout.HelpBox("Les franges es calculen des del perímetre de la zona d'acció. Només s'apliquen quan es generen les escenes.",MessageType.Info);
        rule.center=EditorGUILayout.Vector3Field("Centre de l'acció",rule.center);
        rule.halfWidth=EditorGUILayout.FloatField("Mitja amplada del ring",rule.halfWidth);
        rule.halfDepth=EditorGUILayout.FloatField("Mitja profunditat del ring",rule.halfDepth);
        rule.near=EditorGUILayout.FloatField("Final de la franja 0 (m)",rule.near);
        rule.middle=EditorGUILayout.FloatField("Final de la franja 1 (m)",rule.middle);
        rule.far=EditorGUILayout.FloatField("Final de la franja 2 (m)",rule.far);
        EditorGUILayout.LabelField("Franja 3: més enllà de l'últim límit.");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Textures dels objectes per nivell",EditorStyles.boldLabel);
        if(policy.textureSizes!=null)for(int i=0;i<Mathf.Min(5,policy.textureSizes.Length);i++)policy.textureSizes[i]=EditorGUILayout.IntPopup(Qualities[i],policy.textureSizes[i],new[]{"256","512","1024","2048","4096"},new[]{256,512,1024,2048,4096});
        if(policy.platformTextureCaps!=null)foreach(var cap in policy.platformTextureCaps)cap.maximum=EditorGUILayout.IntPopup("Límit "+cap.platform,cap.maximum,new[]{"512","1024","2048","4096"},new[]{512,1024,2048,4096});
        exceptions=EditorGUILayout.Foldout(exceptions,"Excepcions per objecte",true);
        if(exceptions)
        {
            var list=(rule.exceptions??Array.Empty<ArenaDistanceQuality.ExceptionRule>()).ToList();
            int remove=-1;
            for(int i=0;i<list.Count;i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                list[i].objectName=EditorGUILayout.TextField("Nom de l'objecte",list[i].objectName);
                list[i].prefabName=EditorGUILayout.TextField("O nom del prefab",list[i].prefabName);
                list[i].forcedBand=EditorGUILayout.IntSlider("Franja (-1 = automàtica)",list[i].forcedBand,-1,3);
                list[i].minimumQuality=EditorGUILayout.Popup("Qualitat mínima",list[i].minimumQuality,Qualities);
                if(GUILayout.Button("Elimina aquesta excepció"))remove=i;
                EditorGUILayout.EndVertical();
            }
            if(remove>=0)list.RemoveAt(remove);
            if(GUILayout.Button("Afegeix una excepció"))list.Add(new ArenaDistanceQuality.ExceptionRule());
            rule.exceptions=list.ToArray();
        }
        EditorGUILayout.Space();
        if(GUILayout.Button("Desa les regles"))Save();
        if(GUILayout.Button("Audita sense modificar escenes")){Save();ArenaDistanceQuality.Audit();}
        if(GUILayout.Button("Desa i genera aquest escenari"))
        {
            Save();var before=EditorSceneManager.GetSceneManagerSetup();
            try{ComposerSceneryAssembly.BuildOneForReview(rule.composer);}
            finally{EditorSceneManager.RestoreSceneManagerSetup(before);}
        }
        if(GUILayout.Button("Recarrega la configuració desada"))Reload();
        EditorGUILayout.EndScrollView();if(GUI.changed)SceneView.RepaintAll();
    }
    void Save()
    {
        foreach(var rule in policy.scenes)
        {
            if(rule.halfWidth<=0||rule.halfDepth<=0||rule.near<0||rule.middle<=rule.near||rule.far<=rule.middle)throw new InvalidOperationException("Revisa els límits de "+rule.composer);
            if((rule.exceptions??Array.Empty<ArenaDistanceQuality.ExceptionRule>()).Any(e=>string.IsNullOrWhiteSpace(e.objectName)==string.IsNullOrWhiteSpace(e.prefabName)))throw new InvalidOperationException("Indica un objecte o un prefab per a cada excepció.");
            if((rule.exceptions??Array.Empty<ArenaDistanceQuality.ExceptionRule>()).GroupBy(e=>string.IsNullOrEmpty(e.objectName)?"prefab/"+e.prefabName:"object/"+e.objectName).Any(g=>g.Count()>1))throw new InvalidOperationException("Hi ha excepcions repetides a "+rule.composer);
        }
        ArenaDistanceQuality.Backup(policy.scenes[selected].composer);
        File.WriteAllText(Config,JsonUtility.ToJson(policy,true));AssetDatabase.ImportAsset(Config);
    }
    void Draw(SceneView view)
    {
        if(policy?.scenes==null||selected>=policy.scenes.Length)return;
        var rule=policy.scenes[selected];
        var previous=Handles.color;
        Handles.color=Color.white;Handles.DrawWireCube(rule.center+Vector3.up*.04f,new Vector3(rule.halfWidth*2,0,rule.halfDepth*2));
        float[] distances={rule.near,rule.middle,rule.far};Color[] colors={Color.green,Color.yellow,new Color(1,.45f,.2f)};
        for(int band=0;band<3;band++)
        {
            Handles.color=colors[band];var points=new List<Vector3>();
            for(int corner=0;corner<4;corner++)
            {
                float cx=corner==0||corner==3?rule.halfWidth:-rule.halfWidth;
                float cz=corner<2?rule.halfDepth:-rule.halfDepth;
                for(int step=0;step<=12;step++)
                {
                    float angle=(corner*90+step*7.5f)*Mathf.Deg2Rad;
                    points.Add(rule.center+new Vector3(cx+Mathf.Cos(angle)*distances[band],.06f,cz+Mathf.Sin(angle)*distances[band]));
                }
            }
            points.Add(points[0]);Handles.DrawAAPolyLine(2,points.ToArray());
            Handles.Label(rule.center+new Vector3(0,.08f,rule.halfDepth+distances[band]),"Final franja "+band+" · "+distances[band]+" m");
        }
        Handles.color=previous;
    }
}
#endif
