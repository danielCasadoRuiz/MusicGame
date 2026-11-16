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
    static FightOpponentPoolCheck() { EditorApplication.delayCall += () => { if (File.Exists("Logs/FightOpponentPoolCheck.request")) Run(); }; }
    [MenuItem("MusicGame/Runner/Check Normal Opponent Pool")]
    public static void Run()
    {
        const string request = "Logs/FightOpponentPoolCheck.request";
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(request)) File.Delete(request);
        GameObject holder = null;
        try
        {
            var app = Resources.Load<AppConfigSO>("AppConfig");
            if (!string.IsNullOrWhiteSpace(app.testForceOpponentId) || app.testForceOpponentLevel != 0)
                throw new Exception("Temporary opponent override is still enabled");
            var roster = app.opponentRoster.opponents.Where(o => o != null).ToArray();
            var expected = new HashSet<string>(roster.Select(o => o.id));
            if (roster.Length != 12 || expected.Count != 12) throw new Exception("Expected twelve unique campaign rivals");
            holder = new GameObject("OpponentPoolCheck"); holder.SetActive(false);
            var session = holder.AddComponent<GameSession>();
            var method = typeof(GameSession).GetMethod("ApplyTestForce", BindingFlags.Instance | BindingFlags.NonPublic);
            var picks = new List<string>();
            var bag = new OpponentShuffleBag(); var rng = new System.Random(62491);
            for (int i = 0; i < roster.Length * 10; i++)
            {
                var source = bag.Next(roster, 2, rng);
                var config = source.GetConfigForTier(1);
                if (config == null) throw new Exception("Missing rival config: " + source.id);
                object[] args = { source, config, 1 };
                method.Invoke(session, args);
                if (!ReferenceEquals(args[0],source) || !ReferenceEquals(args[1],config) || (int)args[2]!=1)
                    throw new Exception("Normal assignment was replaced by test logic");
                picks.Add(source.id);
                if (i % roster.Length == roster.Length-1 && !expected.SetEquals(picks.Skip(i-roster.Length+1).Take(roster.Length)))
                    throw new Exception("Full roster was not cycled");
                if (i>0 && picks[i]==picks[i-1]) throw new Exception("Immediate repeat");
            }
            // Every authored version keeps its exact assignment; no forced version 1.
            foreach (var source in roster) foreach (var config in source.levels)
            {
                if(config==null) continue;
                object[] args={source,config,config.level}; method.Invoke(session,args);
                if(!ReferenceEquals(args[0],source)||!ReferenceEquals(args[1],config)||(int)args[2]!=config.level)
                    throw new Exception("Assigned rival version was replaced");
            }
            object[] clear = { null, null, 0 }; method.Invoke(session, clear);
            if (clear[0] != null) throw new Exception("Clearing assignment picked an opponent");
            File.WriteAllText("Logs/FightOpponentPoolCheck.txt", "PASS: test overrides disabled; 120 draws cover all twelve rivals without immediate repeats; every authored rival/version assignment remains unchanged; clearing remains empty.\n" + string.Join(", ", picks));
        }
        catch (Exception e) { File.WriteAllText("Logs/FightOpponentPoolCheck.txt", e.ToString()); Debug.LogException(e); }
        finally { if (holder != null) UnityEngine.Object.DestroyImmediate(holder); }
    }
}
