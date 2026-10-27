using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FightOpponentPoolCheck
{
    static FightOpponentPoolCheck() { EditorApplication.delayCall += Run; }
    static void Run()
    {
        const string request = "Logs/FightOpponentPoolCheck.request";
        if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        GameObject holder = null;
        try
        {
            var app = Resources.Load<AppConfigSO>("AppConfig");
            var expected = new HashSet<string>(new[] { "monteverdi", "wagner", "beethoven", "bach" });
            if (!expected.SetEquals(app.testForceOpponentId.Split(','))) throw new Exception("Wrong configured pool");
            var source = app.opponentRoster.opponents.First(o => o != null);
            holder = new GameObject("OpponentPoolCheck"); holder.SetActive(false);
            var session = holder.AddComponent<GameSession>();
            var method = typeof(GameSession).GetMethod("ApplyTestForce", BindingFlags.Instance | BindingFlags.NonPublic);
            var picks = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                object[] args = { source, source.GetConfigForTier(1), 1 };
                method.Invoke(session, args);
                var picked = (OpponentDefinition)args[0];
                if (!expected.Contains(picked.id) || args[1] == null) throw new Exception("Invalid opponent/config");
                picks.Add(picked.id);
                if (i % 4 == 3 && !expected.SetEquals(picks.Skip(i - 3).Take(4))) throw new Exception("Pool did not cycle all four rivals");
                if (i > 0 && picks[i] == picks[i - 1]) throw new Exception("Immediate repeat");
            }
            object[] clear = { null, null, 0 }; method.Invoke(session, clear);
            if (clear[0] != null) throw new Exception("Clearing assignment picked an opponent");
            File.WriteAllText("Logs/FightOpponentPoolCheck.txt", "PASS: 40 assignments use only Monteverdi, Wagner, Beethoven and Bach; every group of four contains all four; no immediate repeats; valid level configs; clearing remains empty.\n" + string.Join(", ", picks));
        }
        catch (Exception e) { File.WriteAllText("Logs/FightOpponentPoolCheck.txt", e.ToString()); Debug.LogException(e); }
        finally { if (holder != null) UnityEngine.Object.DestroyImmediate(holder); }
    }
}
