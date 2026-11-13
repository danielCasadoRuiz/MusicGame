using System;
using UnityEngine;

/// <summary>Measured UV annuli of source iris images, including transparent pupils.</summary>
public sealed class AvatarIrisTextureLibrary : ScriptableObject
{
    [Serializable] public struct Entry { public Texture2D texture; public Vector4 outer; public Vector4 pupil; }
    public Entry[] entries = Array.Empty<Entry>();
    private static AvatarIrisTextureLibrary _library;
    public static void Get(Texture2D texture, out Vector4 outer, out Vector4 pupil)
    {
        outer=new Vector4(.5f,.5f,.44f,0);pupil=new Vector4(.5f,.5f,.14f,0);
        if (_library==null) _library=Resources.Load<AvatarIrisTextureLibrary>("Avatar/IrisCalibration");
        if (_library==null) return;
        foreach(var entry in _library.entries)
            if(entry.texture==texture){outer=entry.outer;pupil=entry.pupil;return;}
    }
}
