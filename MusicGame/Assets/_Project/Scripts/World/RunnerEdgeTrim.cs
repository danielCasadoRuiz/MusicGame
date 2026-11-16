using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Two narrow, bevelled visual ribbons outside the existing ground. No collider or playable-width changes.</summary>
public sealed class RunnerEdgeTrim : System.IDisposable
{
    private GameObject root;
    private Mesh mesh;
    private MeshRenderer renderer;
    private Vector3[] vertices,normals,surface;
    private Vector2[] uv;
    private Color32[] colors;
    private int[] triangles;
    private bool enabled;
    private float width,height;
    private MusicRunnerLevelConfig.EdgeTrimFinish finish;
    private Material material;
    private const int Profile=5;
    private static readonly float[] Across={0,.18f,.82f,1,1};
    private static readonly float[] Rise={0,1,1,0,-1};

    private static Material Resolve(MusicRunnerLevelConfig config,Material track)
    {
        if(config.edgeTrimFinish==MusicRunnerLevelConfig.EdgeTrimFinish.TrackLowestLayer)return track;
        if(config.edgeTrimFinish==MusicRunnerLevelConfig.EdgeTrimFinish.Custom && config.edgeTrimCustomMaterial!=null)return config.edgeTrimCustomMaterial;
        return config.edgeTrimBlackMaterial;
    }

    public bool NeedsUpdate(MusicRunnerLevelConfig config,Material track) => enabled!=config.edgeTrimEnabled || width!=config.edgeTrimWidth
        || height!=config.edgeTrimHeight || finish!=config.edgeTrimFinish || material!=Resolve(config,track);

    public void Rebuild(Transform parent,Vector3[] ground,Vector3[] groundNormals,Vector3[] groundSurface,Color32[] groundColors,
        int rows,int columns,MusicRunnerLevelConfig config,Material track,MaterialPropertyBlock properties)
    {
        enabled=config.edgeTrimEnabled;width=config.edgeTrimWidth;height=config.edgeTrimHeight;finish=config.edgeTrimFinish;material=Resolve(config,track);
        if(!enabled || material==null || rows<2 || columns<2){if(root!=null)root.SetActive(false);return;}
        if(root==null)
        {
            root=new GameObject("Runner visual edge trim",typeof(MeshFilter),typeof(MeshRenderer));root.transform.SetParent(parent,false);
            mesh=new Mesh{name="Runner bevelled edge trim",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            root.GetComponent<MeshFilter>().sharedMesh=mesh;renderer=root.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
        }
        root.SetActive(true);renderer.sharedMaterial=material;renderer.SetPropertyBlock(properties);
        int count=rows*Profile*2;bool resized=vertices==null || vertices.Length!=count;
        if(resized)
        {
            vertices=new Vector3[count];normals=new Vector3[count];surface=new Vector3[count];uv=new Vector2[count];colors=new Color32[count];
            triangles=new int[(rows-1)*(Profile-1)*2*6];int index=0;
            for(int side=0;side<2;side++)for(int row=0;row<rows-1;row++)for(int p=0;p<Profile-1;p++)
            {
                int a=(side*rows+row)*Profile+p,b=a+1,c=a+Profile,d=c+1;
                if(side==1){triangles[index++]=a;triangles[index++]=c;triangles[index++]=b;triangles[index++]=b;triangles[index++]=c;triangles[index++]=d;}
                else{triangles[index++]=a;triangles[index++]=b;triangles[index++]=c;triangles[index++]=b;triangles[index++]=d;triangles[index++]=c;}
            }
        }
        for(int side=0;side<2;side++)for(int row=0;row<rows;row++)
        {
            int edge=row*columns+(side==0?0:columns-1),inside=edge+(side==0?1:-1);
            Vector3 up=groundNormals[edge].normalized;
            Vector3 outward=Vector3.ProjectOnPlane(ground[edge]-ground[inside],up).normalized;
            for(int p=0;p<Profile;p++)
            {
                int v=(side*rows+row)*Profile+p;
                vertices[v]=ground[edge]+outward*(Across[p]*width)+up*(Rise[p]*height);
                int lo=Mathf.Max(0,p-1),hi=Mathf.Min(Profile-1,p+1);
                normals[v]=(up*((Across[hi]-Across[lo])*width)-outward*((Rise[hi]-Rise[lo])*height)).normalized;
                uv[v]=new Vector2(side,(float)row/(rows-1));
                surface[v]=groundSurface[edge];surface[v].x+=(side==0?-1:1)*Across[p]*width;
                surface[v].z=0; // the floor shader's lowest texture layer
                colors[v]=groundColors[edge];
            }
        }
        if(resized)mesh.Clear();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetUVs(1,surface);mesh.SetColors(colors);
        if(resized)mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
    }

    public void Dispose(){Destroy(root);Destroy(mesh);root=null;mesh=null;}
    private static void Destroy(Object value){if(value==null)return;if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
}
