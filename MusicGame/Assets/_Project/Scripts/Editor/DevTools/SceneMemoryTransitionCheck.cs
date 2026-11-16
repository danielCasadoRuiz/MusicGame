using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling;

/// <summary>Batch-only scene lifecycle check. No Core, session, saves, screenshots or player input.</summary>
[InitializeOnLoad]
public static class SceneMemoryTransitionCheck
{
    const string Active="SceneMemoryTransitionCheck.Active";
    static IEnumerator<bool> routine;
    static SceneFlowController flow;
    static double deadline;
    static bool operationDone;
    static string outgoingHeavy;
    static string failure;
    static int observed;
    static readonly List<string> log=new();
    static SceneMemoryTransitionCheck(){if(SessionState.GetBool(Active,false))EditorApplication.update+=Tick;}
    public static void Run()
    {
        if(!Application.isBatchMode)throw new Exception("Run this check in batch mode only");
        Directory.CreateDirectory("Assets/_Project/Scenes/Debug");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene,"Assets/_Project/Scenes/Debug/SceneMemoryTransitionCheck.unity");
        SessionState.SetBool(Active,true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying)return;
        try
        {
            if(routine==null){deadline=EditorApplication.timeSinceStartup+240;routine=Body();}
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Scene memory check timed out");
            if(!routine.MoveNext())Finish(0);
        }
        catch(Exception e){log.Add("FAIL: "+e);Finish(1);}
    }
    static IEnumerator<bool> Body()
    {
        PlayerProgressStore.SuspendWrites=true;
        flow=new GameObject("Isolated scene flow").AddComponent<SceneFlowController>();
        SceneManager.sceneLoaded+=OnLoaded;
        for(int cycle=0;cycle<2;cycle++)
        {
            foreach(var mode in new[]{GameMode.Runner,GameMode.Fight,GameMode.Frontend})
            {
                var previous=flow.CurrentMode;
                outgoingHeavy=previous==GameMode.Runner || previous==GameMode.Fight?previous.Value.ToString():null;
                operationDone=false;flow.StartCoroutine(Transition(mode));
                while(!operationDone){if(failure!=null)throw new Exception(failure);yield return true;}
                if(failure!=null)throw new Exception(failure);
                // Let async avatar construction finish before leaving the loaded mode.
                double until=EditorApplication.timeSinceStartup+60;
                if(mode==GameMode.Runner)
                {
                    while(UnityEngine.Object.FindFirstObjectByType<PlayerController>() is PlayerController p && !p.IsVisualReady)
                    {if(EditorApplication.timeSinceStartup>until)throw new Exception("Runner avatar timeout");yield return true;}
                }
                if(mode==GameMode.Fight)
                {
                    while(FightSceneBootstrap.Instance!=null && !FightSceneBootstrap.Instance.StageReady)
                    {if(EditorApplication.timeSinceStartup>until)throw new Exception("Fight stage timeout");yield return true;}
                }
                log.Add($"PASS cycle {cycle+1} {previous}->{mode}: allocated {Profiler.GetTotalAllocatedMemoryLong()/1048576} MB, reserved {Profiler.GetTotalReservedMemoryLong()/1048576} MB, meshes {Resources.FindObjectsOfTypeAll<Mesh>().Length}");
            }
        }
        if(observed<4)throw new Exception("Not enough heavy scene boundaries observed");
        log.Add("PASS: two Runner→Fight→Frontend cycles; outgoing heavy scene absent before destination sceneLoaded; actual avatar readiness awaited. No player save loaded or written.");
    }
    static IEnumerator Transition(GameMode mode){yield return flow.LoadMode(mode);operationDone=true;}
    static void OnLoaded(Scene scene,LoadSceneMode mode)
    {
        if(outgoingHeavy==null || (scene.name!="Runner" && scene.name!="Fight" && scene.name!="Frontend"))return;
        if(SceneManager.GetSceneByName(outgoingHeavy).isLoaded)failure="Heavy scene "+outgoingHeavy+" still loaded when "+scene.name+" activated";
        else observed++;
    }
    static void Finish(int code)
    {
        SceneManager.sceneLoaded-=OnLoaded;SessionState.SetBool(Active,false);EditorApplication.update-=Tick;
        Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/SceneMemoryTransitionCheck.txt",log);Debug.Log(string.Join("\n",log));
        EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(code);
    }
}
