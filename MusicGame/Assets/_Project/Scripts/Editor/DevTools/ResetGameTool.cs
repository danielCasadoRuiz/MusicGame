using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ResetGameTool
{
    const string Pending="MusicGame.ResetGame.Pending";
    static ResetGameTool(){if(SessionState.GetBool(Pending,false))EditorApplication.update+=FinishWhenStopped;}

    [MenuItem("Tools/Reset Game…",false,2000)]
    public static void ResetGame()
    {
        if(!EditorUtility.DisplayDialog("Reset Game", "Esborrar el progrés i començar de zero?\n\nEs perdran la campanya, les cartes, les vides, l’historial i la partida pendent amb la cançó i el rival assignats.\n\nEs conserven les cançons, les anàlisis, les descàrregues i les preferències. Si el joc està en marxa, s’aturarà.\n\n"+PlayerProgressStore.FilePath,"Esborra el progrés","Cancel·la"))return;
        SessionState.SetBool(Pending,true);
        // Prevent shutdown callbacks from writing the old in-memory profile back to disk.
        if(EditorApplication.isPlaying){PlayerProgressStore.SuspendWrites=true;EditorApplication.isPlaying=false;}
        EditorApplication.update-=FinishWhenStopped;EditorApplication.update+=FinishWhenStopped;
    }
    [MenuItem("Tools/Reset Game…",true)]
    static bool CanReset()=>!EditorApplication.isCompiling && (!EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying);

    static void FinishWhenStopped()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)return;
        EditorApplication.update-=FinishWhenStopped;SessionState.SetBool(Pending,false);
        try
        {
            int count=DeleteProgressFiles(PlayerProgressStore.FilePath);
            PlayerProgressStore.SuspendWrites=false;
            Debug.Log($"[Reset Game] Progrés esborrat ({count} fitxers). La propera arrencada començarà de zero. {PlayerProgressStore.FilePath}");
            EditorUtility.DisplayDialog("Reset Game","Progrés esborrat. Prem Play per començar de zero.","D’acord");
        }
        catch(Exception e){PlayerProgressStore.SuspendWrites=false;Debug.LogException(e);EditorUtility.DisplayDialog("Reset Game","No s’ha pogut completar el reset:\n"+e.Message,"D’acord");}
    }

    // Only the four known save files. Never delete the persistent-data directory or caches.
    internal static int DeleteProgressFiles(string savePath)
    {
        string full=Path.GetFullPath(savePath);
        if(!string.Equals(Path.GetFileName(full),"player_progress.json",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Unexpected progress filename");
        int count=0;
        foreach(string suffix in new[]{"",".bak",".tmp",".corrupt"})
        {
            string file=full+suffix;if(!File.Exists(file))continue;File.Delete(file);count++;
        }
        return count;
    }

    public static void Verify()
    {
        string folder=Path.GetFullPath("Temp/ResetGameToolCheck_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string save=Path.Combine(folder,"player_progress.json"),cache=Path.Combine(folder,"song-cache-sentinel.txt");
        try
        {
            foreach(string suffix in new[]{"",".bak",".tmp",".corrupt"})File.WriteAllText(save+suffix,"test");
            File.WriteAllText(cache,"keep");
            if(DeleteProgressFiles(save)!=4 || DeleteProgressFiles(save)!=0 || !File.Exists(cache))throw new Exception("Reset file scope / idempotence failed");
            bool rejected=false;try{DeleteProgressFiles(cache);}catch(ArgumentException){rejected=true;}
            if(!rejected)throw new Exception("Unexpected filename accepted");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/ResetGameToolCheck.txt","PASS: deletes only the four known progress files, preserves unrelated cache, repeated reset is safe, unexpected paths rejected. Real player save untouched.");
            Debug.Log(File.ReadAllText("Logs/ResetGameToolCheck.txt"));
        }
        finally{if(File.Exists(cache))File.Delete(cache);foreach(string suffix in new[]{"",".bak",".tmp",".corrupt"})if(File.Exists(save+suffix))File.Delete(save+suffix);Directory.Delete(folder);}
    }
}
