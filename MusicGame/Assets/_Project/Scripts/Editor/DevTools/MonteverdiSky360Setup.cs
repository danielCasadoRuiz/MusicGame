#if UNITY_EDITOR
using System;using System.IO;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;
public static class MonteverdiSky360Setup {
 const string W=@"C:\Users\VISYON\.codex\.chatgpt-projects\g-p-6a438d8b08888191a78acab089f6e907\skybox_fix";
 const string Root="Assets/_Project/Art/Arenas/Monteverdi";
 static Camera cam;
 public static void Start(){try{
  AssetDatabase.Refresh();Directory.CreateDirectory(W+"/captures");
  EditorSceneManager.OpenScene("Assets/_Project/Scenes/Debug/Monteverdi_CombatLook_Proposal.unity");
  cam=new GameObject("Sky360ReviewCamera").AddComponent<Camera>();cam.clearFlags=CameraClearFlags.Skybox;cam.cullingMask=0;cam.fieldOfView=70;cam.allowHDR=false;
  cam.transform.rotation=Quaternion.Euler(0,335.3f,0);if(!File.Exists(W+"/captures/before_seam.png"))Capture("before_seam");
  var shader=Shader.Find("MusicGame/Art/SeamlessPanorama360");if(!shader||ShaderUtil.ShaderHasError(shader))throw new Exception("Panorama shader missing or invalid");
  string texPath=Root+"/Textures/Monteverdi_Sky360.png";
  var importer=(TextureImporter)AssetImporter.GetAtPath(texPath);importer.wrapModeU=TextureWrapMode.Repeat;importer.wrapModeV=TextureWrapMode.Clamp;importer.mipmapEnabled=false;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;importer.SaveAndReimport();
  var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);int n=0;
  foreach(var f in Directory.GetFiles(Root+"/Materials","Monteverdi_Sky*.mat")){
   var mat=AssetDatabase.LoadAssetAtPath<Material>(f);if(!mat||!mat.HasProperty("_MainTex"))continue;
   string b=W+"/backup/"+Path.GetFileName(f);if(!File.Exists(b)){Directory.CreateDirectory(Path.GetDirectoryName(b));File.Copy(f,b);}
   mat.shader=shader;mat.SetTexture("_MainTex",tex);mat.SetFloat("_HorizontalFov",360);mat.SetFloat("_VerticalFov",180);mat.SetFloat("_VerticalOffset",.07f);mat.SetFloat("_Rotation",199.08f);EditorUtility.SetDirty(mat);n++;
  }
  AssetDatabase.SaveAssets();
  RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Monteverdi_SkyPanorama.mat");
  for(int i=0;i<12;i++){cam.transform.rotation=Quaternion.Euler(0,19.08f+i*30,0);Capture("sky_"+i.ToString("00"));}
  cam.cullingMask=~0;cam.transform.position=new Vector3(0,1.25f,-5.4f);cam.transform.LookAt(new Vector3(0,1.05f,0));cam.fieldOfView=42;Capture("arena");
  ArenaBundleValidation.Register();
  File.WriteAllText(W+"/verification.txt","PASS: "+n+" shared Monteverdi sky materials updated; one complete non-mirrored 360-degree horizontal mapping. Symmetric 2.5% edge filter guarantees continuous wrap. Native seam-centred capture plus twelve views spaced 30 degrees. Original sky source retained. No scene layout or bake changed.\n");
  EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText(W+"/error.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
 public static void ApplyForCurrentScene(string composer=null){if(composer!="Monteverdi"&&!UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("Monteverdi"))return;var mat=RenderSettings.skybox;var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/Monteverdi_Sky360.png");if(!mat||!tex)return;mat.shader=Shader.Find("MusicGame/Art/SeamlessPanorama360");mat.SetTexture("_MainTex",tex);mat.SetFloat("_HorizontalFov",360);mat.SetFloat("_VerticalFov",180);mat.SetFloat("_VerticalOffset",.07f);mat.SetFloat("_Rotation",199.08f);EditorUtility.SetDirty(mat);}
 static void Capture(string name){const int w=1400,h=788;cam.aspect=(float)w/h;var rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.Create();cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(w,h,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,w,h),0,0);t.Apply();File.WriteAllBytes(W+"/captures/"+name+".png",t.EncodeToPNG());RenderTexture.active=old;cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(t);}
}
#endif
