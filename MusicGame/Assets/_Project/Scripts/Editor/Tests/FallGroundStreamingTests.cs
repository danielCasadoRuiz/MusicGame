#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class FallGroundStreamingTests
{
    [Test]
    public void DeathKeepsGroundAtTheBodyUntilExplicitRespawnOrRestart()
    {
        var host = new GameObject("FallGroundStreamingTest");
        host.SetActive(false);
        var world = host.AddComponent<MusicWorldManager>();
        try
        {
            typeof(MusicWorldManager).GetProperty("Path").SetValue(world, new MusicPath(new[]
            {
                new MusicPath.Sample(Vector3.zero, Vector3.forward, 0, 8),
                new MusicPath.Sample(Vector3.forward * 1000, Vector3.forward, 1000, 8),
            }));
            // No mesh/grid needed: exercise the real streaming decision and fall/respawn paths.
            world.RebuildNow(100);
            Call(world, "HoldGroundForFall", new PlayerFellEvent());
            Call(world, "StreamGround", 116f); // first normal 15 m recycling threshold
            Call(world, "StreamGround", 240f); // music can keep advancing throughout the shot
            Assert.AreEqual(100f, Center(world), "Music must not discard the death shot's ground.");
            Call(world, "HoldGroundForFall", new PlayerFellEvent());
            Assert.AreEqual(100f, Center(world), "Repeated penalties must not move the held window.");

            world.RebuildNow(103f); // exact fall-time respawn
            Assert.AreEqual(103f, Center(world));
            Call(world, "StreamGround", 120f);
            Assert.AreEqual(120f, Center(world), "Normal streaming must resume after respawn.");

            Call(world, "HoldGroundForFall", new PlayerFellEvent());
            world.RebuildNow(0f); // Pause -> Restart Song
            Call(world, "StreamGround", 20f);
            Assert.AreEqual(20f, Center(world), "A restart must also release the death window.");
        }
        finally { Object.DestroyImmediate(host); }
    }

    private static float Center(MusicWorldManager world) => (float)typeof(MusicWorldManager)
        .GetField("_groundWindowCenter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(world);

    private static void Call(MusicWorldManager world, string name, object argument) => typeof(MusicWorldManager)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(world, new[] { argument });

    [InitializeOnLoadMethod]
    private static void RunRequestedCheck()
    {
        EditorApplication.delayCall += () =>
        {
            const string request = "Logs/FallGroundStreamingCheck.request";
            if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            new FallGroundStreamingTests().DeathKeepsGroundAtTheBodyUntilExplicitRespawnOrRestart();
            File.WriteAllText("Logs/FallGroundStreamingCheck.txt", "PASS: ground held across advancing music and repeated falls; exact respawn and full restart both restore streaming.");
            File.Delete(request);
            Debug.Log(File.ReadAllText("Logs/FallGroundStreamingCheck.txt"));
        };
    }
}
#endif
