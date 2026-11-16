using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;

public static class ContentFastModeCrashCheck
{
    public static void Run()
    {
        var settings=AddressableAssetSettingsDefaultObject.Settings;
        var type=typeof(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings).Assembly.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsLocator",true);
        var locator=(IResourceLocator)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{settings},null);
        Addressables.AddResourceLocator(locator);
        var host=new GameObject("FastModeDownloadRegression");host.SetActive(false);var manager=host.AddComponent<ContentDownloadManager>();
        int checks=0;
        try
        {
            var keys=new System.Collections.Generic.List<string>{ContentKeys.Core,ContentKeys.Category(ContentCategory.Styles),ContentKeys.Category(ContentCategory.Enemies),ContentKeys.Style("Classical"),"missing-test-key"};
            foreach(var quality in QualitySettings.names)keys.Add(QualityAssetResolver.LabelFor(quality));
            for(int pass=0;pass<20;pass++)foreach(var key in keys)
            {
                var size=manager.GetDownloadSizeAsync(key);
                if(!size.IsCompleted || size.GetAwaiter().GetResult()!=0)throw new Exception("Local Fast Mode content should need no download: "+key);
                var ready=manager.EnsureContentAvailableAsync(key);
                if(!ready.IsCompleted || !ready.GetAwaiter().GetResult())throw new Exception("Local Fast Mode content unavailable: "+key);
                var clear=manager.ClearCacheAsync(key);
                if(!clear.IsCompleted)throw new Exception("Local Fast Mode should have no bundle cache to clear");
                clear.GetAwaiter().GetResult();checks+=3;
            }
            if(manager.InFlightCount!=0)throw new Exception("Synchronous downloads retained as in flight");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/ContentFastModeCrashCheck.txt","PASS: "+checks+" download/size/cache checks with the real Fast Mode locator; quality labels included, no native asset enumeration, no stuck tasks. "+DateTime.UtcNow.ToString("O"));
            Debug.Log(File.ReadAllText("Logs/ContentFastModeCrashCheck.txt"));
        }
        finally{Addressables.RemoveResourceLocator(locator);UnityEngine.Object.DestroyImmediate(host);}
    }
}
