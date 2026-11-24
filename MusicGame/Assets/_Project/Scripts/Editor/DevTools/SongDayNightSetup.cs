#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SongDayNightSetup
{
    static SongDayNightSetup(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        string request="Temp/SongDayNight.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorApplication.isPlaying){EditorApplication.ExitPlaymode();return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        try
        {
            AppearanceSetup.InstallBackgrounds();SongSelectionSkinSetup.Install();
            SongHorizontalCatalogValidation.Verify();Verify();AssetDatabase.SaveAssets();
            File.WriteAllText("../song-day-night-validation.txt","PASS: 16 distinct day/night background pairs, luminance and Dark smoky-day exception; all card states reverse between day and night; preference restored.");
        }
        catch(Exception e){File.WriteAllText("../song-day-night-validation.txt","FAIL: "+e);Debug.LogException(e);}
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Verify()
    {
        var lib=AssetDatabase.LoadAssetAtPath<StyleSkinLibrarySO>(AppearanceSetup.LibraryPath);
        var audit=new StringBuilder("Style\tNight luminance\tDay luminance\tDay asset\n");
        Check(lib.menuBackgrounds.Count==16,"Expected sixteen backgrounds");
        foreach(var entry in lib.menuBackgrounds)
        {
            Check(entry.texture!=null&&entry.dayTexture!=null&&entry.texture!=entry.dayTexture,"Missing distinct background pair "+entry.style);
            float night=Luminance(entry.texture),day=Luminance(entry.dayTexture);
            Check(day>night+.15f,"Day background is still as dark as night "+entry.style);
            Check(entry.style==GameMusicStyle.Dark?day>.2f&&day<.65f:day>.45f,"Unexpected day brightness "+entry.style);
            Check(entry.dayKeepsLightInk==(entry.style==GameMusicStyle.Dark),"Wrong daylight ink exception "+entry.style);
            audit.AppendLine($"{entry.style}\t{night:F3}\t{day:F3}\t{AssetDatabase.GetAssetPath(entry.dayTexture)}");
        }
        File.WriteAllText("../VisualReviews/Appearance/background-audit-after.tsv",audit.ToString());
        bool had=PlayerPrefs.HasKey(AppearancePreference.PrefKey);string saved=AppearancePreference.Mode;
        var row=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SongRowView>(SongSelectionSkinSetup.RowPath));
        try
        {
            foreach(string mode in new[]{AppearancePreference.Light,AppearancePreference.Dark})
            {
                AppearancePreference.SetMode(mode);AppearancePreference.Tick(10);
                foreach(var card in lib.songCards)foreach(bool selected in new[]{false,true})
                {
                    row.SetSelection(selected,card.style);
                    var appearance=row.GetComponent<SongCardAppearance>();
                    float expected=(mode==AppearancePreference.Light)!=selected?1:0;
                    Check(appearance.LightSurfaceWeight==expected,"Surface not inverted "+card.style+" "+mode);
                    Check(Mathf.Approximately(row.background.material.GetFloat("_LightSurface"),expected),"Stale shader mode "+card.style);
                    Check(row.label.color==Color.Lerp(lib.SongCardInk(card.style,true),lib.songNormalInk,expected),"Incorrect contrast "+card.style);
                }
            }
            Check(SongCardAppearance.SurfaceWeight(true,.5f)==.5f&&SongCardAppearance.SurfaceWeight(false,.5f)==.5f,"Mid-transition surfaces jump");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(row.gameObject);AppearancePreference.SetMode(saved);AppearancePreference.Tick(10);
            if(!had){PlayerPrefs.DeleteKey(AppearancePreference.PrefKey);PlayerPrefs.Save();}
        }
    }
    static float Luminance(Texture2D source)
    {
        var copy=new Texture2D(2,2,TextureFormat.RGB24,false);
        try
        {
            copy.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));float sum=0;
            for(int y=0;y<18;y++)for(int x=0;x<32;x++){var c=copy.GetPixelBilinear((x+.5f)/32,(y+.5f)/18);sum+=c.r*.2126f+c.g*.7152f+c.b*.0722f;}
            return sum/(32*18);
        }
        finally{UnityEngine.Object.DestroyImmediate(copy);}
    }
}
#endif
