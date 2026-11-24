#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SongMetadataSetup
{
    public const string Folder="Assets/_Project/Configs/Song/Metadata";
    [Serializable] private class Seed { public SongMetadataRecord[] songs; }
    [Serializable] private class SongMetadataRecord
    {
        public string songId,title,artist,referenceTitle,referenceArtist,metadataSource;
        public int releaseYear;
    }
    [MenuItem("Tools/MusicGame/Songs/Sync Song Metadata")]
    public static void Sync()
    {
        var catalog=Resources.Load<AppConfigSO>("AppConfig")?.songCatalog;if(catalog==null)return;
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/_Project/Configs/Song","Metadata");
        var seed=JsonUtility.FromJson<Seed>(File.ReadAllText("Assets/_Project/Configs/Song/SongMetadataSeed.json"));
        var index=AssetDatabase.LoadAssetAtPath<SongCatalogIndexSO>(SongContentKeys.IndexAssetPath);
        foreach(var entry in catalog.songs)
        {
            string path=Folder+"/"+entry.songId+".asset";
            var metadata=AssetDatabase.LoadAssetAtPath<SongMetadataSO>(path);
            var published=index?.songs.Find(s=>s.songId==entry.songId);
            if(metadata==null)
            {
                metadata=ScriptableObject.CreateInstance<SongMetadataSO>();metadata.songId=entry.songId;
                var record=seed?.songs?.FirstOrDefault(s=>s.songId==entry.songId);
                if(record!=null)
                {
                    metadata.title=record.title;metadata.artist=record.artist;metadata.releaseYear=record.releaseYear;
                    metadata.referenceTitle=record.referenceTitle;metadata.referenceArtist=record.referenceArtist;metadata.metadataSource=record.metadataSource;
                }
                else metadata.title=entry.address;
                AssetDatabase.CreateAsset(metadata,path);
            }
            metadata.audio=AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(entry.editorGuid));
            if(published!=null)metadata.style=published.gameStyle;
            entry.title=metadata.title;entry.artist=metadata.artist;entry.releaseYear=metadata.releaseYear;
            if(published!=null)
            {
                published.title=metadata.title;published.artist=metadata.artist;published.releaseYear=metadata.releaseYear;
            }
            EditorUtility.SetDirty(metadata);
        }
        EditorUtility.SetDirty(catalog);
        if(index!=null){index.contentRevision=SongCatalogBake.ContentRevision(index.songs);EditorUtility.SetDirty(index);}
        AssetDatabase.SaveAssets();
    }
}
#endif
