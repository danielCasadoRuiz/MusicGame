using UnityEditor;
using UnityEngine;
[CustomPropertyDrawer(typeof(AvatarNailSettings))]
public class AvatarNailSettingsDrawer : PropertyDrawer
{
    static readonly string[] Groups={"Left hand","Right hand","Left foot","Right foot"};
    static readonly string[] Fingers={"Thumb","Index","Middle","Ring","Little"};
    static readonly string[] Toes={"Big toe","Second toe","Third toe","Fourth toe","Little toe"};
    public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
    {
        if(!property.isExpanded)return EditorGUIUtility.singleLineHeight;
        int mode=property.FindPropertyRelative("mode").enumValueIndex;
        return (mode==0?3:mode==1?9:27)*(EditorGUIUtility.singleLineHeight+2);
    }
    public override void OnGUI(Rect position,SerializedProperty property,GUIContent label)
    {
        float step=EditorGUIUtility.singleLineHeight+2;var row=new Rect(position.x,position.y,position.width,EditorGUIUtility.singleLineHeight);
        property.isExpanded=EditorGUI.Foldout(row,property.isExpanded,label,true);if(!property.isExpanded)return;
        row.y+=step;var mode=property.FindPropertyRelative("mode");EditorGUI.PropertyField(row,mode,new GUIContent("Nail mode"));
        if(mode.enumValueIndex==0){row.y+=step;EditorGUI.LabelField(row,"Natural nails: no nail coloring is applied.");return;}
        if(mode.enumValueIndex==1)
        {
            var groups=property.FindPropertyRelative("groups");if(groups.arraySize!=4)groups.arraySize=4;
            for(int i=0;i<4;i++)
            {
                row.y+=step;var group=groups.GetArrayElementAtIndex(i);var painted=group.FindPropertyRelative("painted");
                var toggle=row;toggle.width*=.47f;painted.boolValue=EditorGUI.ToggleLeft(toggle,Groups[i],painted.boolValue);
                var picker=row;picker.x+=toggle.width;picker.width-=toggle.width;
                if(painted.boolValue){var c=group.FindPropertyRelative("color");var color=EditorGUI.ColorField(picker,GUIContent.none,c.colorValue,true,false,false);color.a=1;c.colorValue=color;}
                else EditorGUI.LabelField(picker,"Natural");
            }
            row.y+=step;_all=EditorGUI.ColorField(row,new GUIContent("Color for all groups"),_all,true,false,false);_all.a=1;
            row.y+=step;if(GUI.Button(row,"Paint all four groups"))for(int i=0;i<4;i++){var g=groups.GetArrayElementAtIndex(i);g.FindPropertyRelative("painted").boolValue=true;g.FindPropertyRelative("color").colorValue=_all;}
            return;
        }
        var colors=property.FindPropertyRelative("colors");if(colors.arraySize!=20)colors.arraySize=20;
        for(int group=0;group<4;group++)
        {
            row.y+=step;EditorGUI.LabelField(row,Groups[group],EditorStyles.boldLabel);
            for(int i=0;i<5;i++)
            {
                row.y+=step;var c=colors.GetArrayElementAtIndex(group*5+i);var toggle=row;toggle.width*=.47f;
                bool enabled=EditorGUI.ToggleLeft(toggle,group<2?Fingers[i]:Toes[i],c.colorValue.a>0);
                var picker=row;picker.x+=toggle.width;picker.width-=toggle.width;
                if(enabled){var color=c.colorValue;if(color.a==0)color=Color.white;color=EditorGUI.ColorField(picker,GUIContent.none,color,true,false,false);color.a=1;c.colorValue=color;}
                else {var color=c.colorValue;color.a=0;c.colorValue=color;EditorGUI.LabelField(picker,"Natural");}
            }
        }
    }
    Color _all=new Color(.6f,.02f,.04f,1);
}
