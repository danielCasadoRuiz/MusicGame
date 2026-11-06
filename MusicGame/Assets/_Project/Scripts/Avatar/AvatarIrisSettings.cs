using UnityEngine;
[System.Serializable]
public class AvatarIrisSettings
{
    [Tooltip("Standalone square iris textures, centered pupil. Choose any number of variants.")]
    public Texture2D[] baseTextures = System.Array.Empty<Texture2D>();
    [Min(0)] public int textureIndex;
    [Tooltip("Pick a texture and natural three-color palette once per spawned avatar. Disable for authored colors.")]
    public bool randomizeOnSpawn = true;
    public bool useGradient;
    public Color defaultColor = new Color(.28f,.49f,.64f,1);
    public Color inner = new Color(.42f,.32f,.14f,1), middle = new Color(.28f,.49f,.64f,1), outer = new Color(.08f,.16f,.21f,1);
    [Range(.38f,.85f)] public float innerExtent = .55f;
    [Range(.65f,.98f)] public float outerStart = .87f;
    public Vector2 offset;
    [Range(0,.4f)] public float distortion = .1f;
    public int seed = 1;
}
