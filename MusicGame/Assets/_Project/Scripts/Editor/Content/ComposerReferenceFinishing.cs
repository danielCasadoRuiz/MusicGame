#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
public static class ComposerReferenceFinishing
{

 public static void EnablePropInstancing()
 {
  foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/_Project/Art/Arenas"})){
   string path=AssetDatabase.GUIDToAssetPath(guid);if(path.Contains("/Wagner/")||!path.Contains("/Props/"))continue;
   var material=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(material&&material.shader&&(material.shader.name=="Universal Render Pipeline/Lit"||material.shader.name=="MusicGame/Art/GardenFoliage")&&!material.enableInstancing){material.enableInstancing=true;EditorUtility.SetDirty(material);}
  }
 }
 public static void ApplyMonteverdi(GameObject arena,ComposerSceneryAssembly.SceneSpec spec)
 {
  var floor=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Arenas/Monteverdi/Materials/MonteverdiMarble_Floor.mat");
  if(floor){floor.shader=Shader.Find("MusicGame/Art/ScenarioPolishedFloor");floor.SetFloat("_EnvironmentReflectionStrength",.32f);floor.SetTextureScale("_BaseMap",Vector2.one);floor.SetTextureOffset("_BaseMap",Vector2.zero);floor.SetColor("_BaseColor",new Color(.94f,.88f,.76f));floor.SetFloat("_Smoothness",.9f);floor.SetFloat("_BumpScale",.025f);EditorUtility.SetDirty(floor);}
  foreach(var renderer in arena.GetComponentsInChildren<Renderer>(true))if(renderer.name=="FarPlaza")renderer.gameObject.SetActive(false);
  var scenery=arena.transform.Find("MonteverdiScenery");
  if(scenery&&!scenery.Find("East/East_PaintingSupport")){
   var support=GameObject.CreatePrimitive(PrimitiveType.Cube);support.name="East_PaintingSupport";Object.DestroyImmediate(support.GetComponent<Collider>());support.transform.SetParent(scenery.Find("East"),false);support.transform.localPosition=new Vector3(9.15f,4.2f,0);support.transform.localScale=new Vector3(.75f,8.4f,3.85f);support.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Arenas/Monteverdi/Materials/Monteverdi_GalleryPier.mat");
  }
  var env=arena.GetComponentInChildren<ArenaEnvironmentSettings>(true);
  env.overrideAmbient=true;env.ambientMode=AmbientMode.Trilight;env.ambientColor=new Color(.38f,.32f,.25f);env.ambientEquatorColor=new Color(.24f,.19f,.14f);env.ambientGroundColor=new Color(.08f,.06f,.04f);
  env.overrideMainLight=true;env.mainLightColor=new Color(1,.76f,.48f);env.mainLightIntensity=1.5f;env.mainLightEuler=new Vector3(19,-55,0);
  if(env.skybox){var sky=env.skybox;var texture=sky.GetTexture("_MainTex");sky.shader=Shader.Find("MusicGame/Art/SceneryBackdrop");sky.SetTexture("_MainTex",texture);sky.SetFloat("_HorizontalFov",100);sky.SetFloat("_VerticalFov",50);sky.SetFloat("_VerticalOffset",.07f);sky.SetFloat("_Exposure",.85f);EditorUtility.SetDirty(sky);}
  foreach(var probe in arena.GetComponentsInChildren<ReflectionProbe>(true)){probe.resolution=256;probe.boxProjection=true;}
 }
}
#endif
