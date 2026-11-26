#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
public static class SettingsOpenValidation
{
    public static void Verify()
    {
        const string key="TempoFury.MenuSkin";bool had=PlayerPrefs.HasKey(key);int original=PlayerPrefs.GetInt(key,-1);
        var field=typeof(StyledTitleView).GetField("_launchStyle",BindingFlags.NonPublic|BindingFlags.Static);var previous=field.GetValue(null);
        var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<MainMenuView>("Assets/_Project/Prefabs/UI/MainMenu.prefab"));
        try
        {
            var library=root.logo.library;root.settingsPanel.SetActive(true);
            Check(root.settingsPanel.GetComponentsInChildren<SettingsPanelSurface>(true).Length==0,"Old background remains");
            Check(root.settingsPanel.GetComponent<SettingsSkinPanel>().values.Length==0,"Percentages remain");
            Check(root.settingsPanel.GetComponentInChildren<AppearanceSelectorView>(true).automatic.gameObject.activeSelf==false,"Automatic appearance is still exposed");
            Check(root.settingsPanel.transform.Find("SkinChooser/Style button art")==null,"Skin still has a button background");
            string chosenLanguage=null;
            root.languageSelector.Build(new[]{"auto","en","ca","fr","de"},m=>m,m=>chosenLanguage=m);
            root.languageSelector.SetSelected("de");root.languageSelector.Step(1);Check(chosenLanguage=="auto","Language forward wrap failed");
            root.languageSelector.Step(-1);Check(chosenLanguage=="de"&&root.languageSelector.OptionCount==5,"Language carousel does not support additional languages");
            var chooser=root.settingsPanel.GetComponentInChildren<SettingsSkinSelector>();Check(chooser!=null,"Skin arrows missing");
            var first=library.titles.Find(t=>t.style!=GameMusicStyle.Classical&&library.Button(t.style)!=null).style;
            Check(StyledTitleView.SetMenuSkin(library,first),"Could not choose skin");root.logo.Show(first);
            var next=chooser.Adjacent(1);chooser.Step(1);Check(StyledTitleView.MenuStyle(library)==next,"Next skin failed");
            chooser.Step(-1);Check(StyledTitleView.MenuStyle(library)==first,"Previous skin failed");
            field.SetValue(null,null);Check(StyledTitleView.MenuStyle(library)==first,"Skin lost after launch-cache reset");
            for(int i=0;i<library.titles.Count*2;i++)chooser.Step(1);
            Check(StyledTitleView.MenuStyle(library)!=GameMusicStyle.Classical,"Classical exposed as detector skin");
            foreach(var slider in new[]{root.volumeSlider,root.musicVolumeSlider,root.sfxVolumeSlider,root.voiceVolumeSlider})
            {
                foreach(float value in new[]{0f,.5f,1f}){slider.value=value;Check(Mathf.Approximately(slider.value,value),"Slider range broken");}
            }
            File.WriteAllText("../settings-open-behaviour.txt","PASS: no panel or percentage labels; next/previous skin; full wrap; choice survives launch cache reset; classical excluded; four sliders support mute, midpoint and maximum. Original skin preference restored.\n");
        }
        finally{UnityEngine.Object.DestroyImmediate(root.gameObject);if(had)PlayerPrefs.SetInt(key,original);else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();field.SetValue(null,previous);}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
#endif
