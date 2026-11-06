using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports ONE quality of a garment with occlusion masks (schemaVersion 4) — the replacement of the cuts import:
///
///   1. the RIGGED model (Output/&lt;Piece&gt;/Rigged/&lt;Q&gt;/*.fbx, as it is) → `modelPath` (Read/Write, blendshapes, Generic),
///   2. its mask (Output/&lt;Piece&gt;/Rigged/Masks/&lt;stem&gt;.gmask: cover + skin maps in body-UV space, from
///      08_GENERAR_MASCARES.bat) + per-vertex anchors computed here against the canonical body: for every
///      imported vertex, the body-UV texel of the nearest skin point and its height above it
///      → `&lt;prefab&gt;_occlusion.bytes` (GarmentOcclusionData format),
///   3. the variant prefab: AvatarVisualPart, and (unless `plainPrefab`, e.g. hair) SegmentedGarmentPart v4 with
///      the final material and the occlusion asset.
///
/// Both models share the avatar's rest pose and world space (the garments are rigged on the canonical skeleton),
/// so anchors come from the models' own rest transforms. The import checks itself: the garment must lie on its
/// own cover (most of its vertices anchored where its own mask says it is), else the result is an error.
/// </summary>
public static class GarmentMaskImporter
{
    public const float MaxAnchorDistance = 0.22f;

    public sealed class Body
    {
        public Vector3[] Positions, Normals;
        public Vector2[] Uvs;
        public int[] Triangles;
        public TriangleGrid Grid;
        public string Name;

        public static Body From(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            var toWorld = RestMatrix(renderer.transform);
            var body = new Body
            {
                Name = mesh.name,
                Positions = mesh.vertices.Select(v => toWorld.MultiplyPoint3x4(v)).ToArray(),
                Normals = mesh.normals.Select(n => toWorld.MultiplyVector(n).normalized).ToArray(),
                Uvs = mesh.uv,
                Triangles = mesh.triangles,
            };
            body.Grid = new TriangleGrid(body.Positions, body.Triangles, 0.03f);
            return body;
        }
    }

    public sealed class Result
    {
        public GameObject Prefab;
        public TextAsset Occlusion;
        public string Summary;
    }

    /// <summary>World matrix of `t` relative to its outermost parent (the model / prefab root at the origin).</summary>
    public static Matrix4x4 RestMatrix(Transform t)
    {
        var root = t;
        while (root.parent != null) root = root.parent;
        return root.worldToLocalMatrix * t.localToWorldMatrix;
    }

    // ── .gmask (3D pipeline) ─────────────────────────────────────────────────────

    /// <summary>Version 1: cover + skin. Version 2 adds `keep` (255 = this garment's vertices anchored there are
    /// never hidden: the trouser legs). `keep` is null for version 1.</summary>
    public static (int res, byte[] cover, byte[] skin, byte[] keep, JObject meta) ReadGmask(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "GMSK") throw new InvalidDataException(path + " is not a .gmask file");
        int version = BitConverter.ToUInt16(bytes, 4);
        if (version != 1 && version != 2) throw new InvalidDataException($"{path}: gmask version {version} (expected 1 or 2)");
        int res = BitConverter.ToUInt16(bytes, 6);
        int metaLength = (int)BitConverter.ToUInt32(bytes, 8);
        var meta = JObject.Parse(Encoding.UTF8.GetString(bytes, 12, metaLength));
        using var input = new MemoryStream(bytes, 12 + metaLength, bytes.Length - 12 - metaLength);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new BinaryReader(deflate);
        var cover = reader.ReadBytes(res * res);
        var skin = reader.ReadBytes(res * res);
        var keep = version >= 2 ? reader.ReadBytes(res * res) : null;
        if (cover.Length != res * res || skin.Length != res * res || (keep != null && keep.Length != res * res)) throw new InvalidDataException(path + " is truncated");
        return (res, cover, skin, keep, meta);
    }

    // ── One quality ──────────────────────────────────────────────────────────────

    public static Result ImportQuality(string riggedFbx, string gmaskPath, string modelPath, string prefabPath,
                                       Material finalMaterial, Body body, bool plainPrefab, StringBuilder log)
    {
        var (res, cover, skin, keep, meta) = ReadGmask(gmaskPath);
        bool known = AssetImporter.GetAtPath(modelPath) is ModelImporter;
        File.Copy(riggedFbx, modelPath, overwrite: true);
        // Settings first when the model already exists (e.g. the old cut model): one import with materials off,
        // instead of one with every embedded texture and a second one.
        if (!known || !ConfigureModelImporter(modelPath))
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        if (!known) ConfigureModelImporter(modelPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) ?? throw new InvalidOperationException(modelPath + " did not import as a model");
        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException(modelPath + " has no SkinnedMeshRenderer");

        var anchors = new List<(ushort[] x, ushort[] y, byte[] code)>();
        int total = 0, far = 0, selfCovered = 0, near = 0;
        foreach (var r in renderers)
        {
            var mesh = r.sharedMesh;
            if (mesh == null || !mesh.isReadable) throw new InvalidOperationException($"{modelPath}/{r.name}: mesh missing or not readable");
            var toWorld = RestMatrix(r.transform);
            var verts = mesh.vertices;
            var xs = new ushort[verts.Length];
            var ys = new ushort[verts.Length];
            var codes = new byte[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var p = toWorld.MultiplyPoint3x4(verts[i]);
                if (!body.Grid.Nearest(p, MaxAnchorDistance, out int tri, out var closest, out var bary))
                {
                    codes[i] = GarmentOcclusionData.FarCode;
                    far++;
                    continue;
                }
                int a = body.Triangles[tri * 3], b = body.Triangles[tri * 3 + 1], c = body.Triangles[tri * 3 + 2];
                var uv = body.Uvs[a] * bary.x + body.Uvs[b] * bary.y + body.Uvs[c] * bary.z;
                var n = (body.Normals[a] * bary.x + body.Normals[b] * bary.y + body.Normals[c] * bary.z).normalized;
                float d = Vector3.Dot(p - closest, n);
                int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * res), 0, res - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * res), 0, res - 1);
                xs[i] = (ushort)x; ys[i] = (ushort)y;
                codes[i] = (byte)Mathf.Clamp(1 + Mathf.RoundToInt((d + 0.03f) * 1000f), 1, 254);
                if (keep != null && keep[y * res + x] != 0) { codes[i] = GarmentOcclusionData.FarCode; continue; }
                int own = cover[y * res + x];
                if (Mathf.Abs(d) < 0.05f)
                {
                    near++;
                    if (own != 0 && Mathf.Abs(own - codes[i]) <= 30) selfCovered++;
                }
            }
            total += verts.Length;
            anchors.Add((xs, ys, codes));
        }

        float selfRatio = near > 0 ? selfCovered / (float)near : 0f;
        // Plain prefabs (hair) use no masks: hair stands off the scalp, so the check does not apply to them.
        if (!plainPrefab && near > total / 4 && selfRatio < 0.6f)
            throw new InvalidOperationException($"{Path.GetFileName(riggedFbx)}: only {selfRatio:P0} of its close vertices lie on its own mask — " +
                                                "the model and the mask do not match (different avatar / UV / coordinates?)");

        string bytesPath = Path.ChangeExtension(prefabPath, null) + "_occlusion.bytes";
        File.WriteAllBytes(bytesPath, GarmentOcclusionData.Encode(res, cover, skin, anchors));
        AssetDatabase.ImportAsset(bytesPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var occlusion = AssetDatabase.LoadAssetAtPath<TextAsset>(bytesPath);

        var prefab = SavePrefab(model, prefabPath, finalMaterial, plainPrefab ? null : occlusion);
        var stats = meta["stats"];
        return new Result
        {
            Prefab = prefab,
            Occlusion = occlusion,
            Summary = $"{total} verts ({far} far from the body), on own mask {selfRatio:P0}, " +
                      $"cover {stats?["coveredTexels"]} / skin {stats?["skinHiddenTexels"]} texels, {new FileInfo(bytesPath).Length / 1024} KB",
        };
    }

    /// <summary>Applies the garment model settings; true when they changed (the model was re-imported).</summary>
    public static bool ConfigureModelImporter(string modelPath)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        bool dirty = false;
        void Set<T>(T current, T wanted, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(wanted); dirty = true; } }
        Set(importer.animationType, ModelImporterAnimationType.Generic, v => importer.animationType = v);
        Set(importer.avatarSetup, ModelImporterAvatarSetup.NoAvatar, v => importer.avatarSetup = v);
        Set(importer.importAnimation, false, v => importer.importAnimation = v);
        Set(importer.isReadable, true, v => importer.isReadable = v);
        Set(importer.importBlendShapes, true, v => importer.importBlendShapes = v);
        Set(importer.importBlendShapeNormals, ModelImporterNormals.Calculate, v => importer.importBlendShapeNormals = v);
        Set(importer.importNormals, ModelImporterNormals.Import, v => importer.importNormals = v);
        Set(importer.importTangents, ModelImporterTangents.CalculateMikk, v => importer.importTangents = v);
        Set(importer.weldVertices, true, v => importer.weldVertices = v);
        Set(importer.meshCompression, ModelImporterMeshCompression.Off, v => importer.meshCompression = v);
        Set(importer.indexFormat, ModelImporterIndexFormat.Auto, v => importer.indexFormat = v);
        Set(importer.materialImportMode, ModelImporterMaterialImportMode.None, v => importer.materialImportMode = v);
        Set(importer.importCameras, false, v => importer.importCameras = v);
        Set(importer.importLights, false, v => importer.importLights = v);
        Set(importer.useFileScale, true, v => importer.useFileScale = v);
        Set(importer.globalScale, 1f, v => importer.globalScale = v);
        if (dirty) importer.SaveAndReimport();
        return dirty;
    }

    /// <summary>Builds (or refreshes) the variant prefab; an existing one whose renderer paths still exist is
    /// refreshed in place, keeping components added by hand. `occlusion` null = plain prefab (no SegmentedGarmentPart).</summary>
    private static GameObject SavePrefab(GameObject model, string prefabPath, Material final, TextAsset occlusion)
    {
        var modelRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var paths = modelRenderers.Select(r => AnimationUtility.CalculateTransformPath(r.transform, model.transform)).ToList();
        GameObject root = null;
        bool refresh = false;
        if (File.Exists(prefabPath))
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            refresh = paths.All(p => Find(root.transform, p)?.GetComponent<SkinnedMeshRenderer>() != null) &&
                      root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == paths.Count;
            if (!refresh) { PrefabUtility.UnloadPrefabContents(root); root = null; }
        }
        if (root == null)
        {
            root = (GameObject)UnityEngine.Object.Instantiate(model);
            root.name = Path.GetFileNameWithoutExtension(prefabPath);
            foreach (var animator in root.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
        }
        try
        {
            var renderers = new List<SkinnedMeshRenderer>();
            for (int i = 0; i < paths.Count; i++)
            {
                var smr = Find(root.transform, paths[i]).GetComponent<SkinnedMeshRenderer>();
                var source = modelRenderers[i];
                if (refresh)
                {
                    smr.sharedMesh = source.sharedMesh;
                    smr.bones = source.bones.Select(b => b != null ? Find(root.transform, AnimationUtility.CalculateTransformPath(b, model.transform)) : null).ToArray();
                    smr.rootBone = source.rootBone != null ? Find(root.transform, AnimationUtility.CalculateTransformPath(source.rootBone, model.transform)) : null;
                }
                smr.sharedMaterials = Enumerable.Repeat(final, smr.sharedMesh.subMeshCount).ToArray();
                renderers.Add(smr);
            }
            var visual = root.GetComponent<AvatarVisualPart>() ?? root.AddComponent<AvatarVisualPart>();
            visual.skinnedRenderers = renderers.ToArray();
            visual.rootBone = renderers[0].rootBone;

            var part = root.GetComponent<SegmentedGarmentPart>();
            if (occlusion == null)
            {
                if (part != null) UnityEngine.Object.DestroyImmediate(part);
            }
            else
            {
                part ??= root.AddComponent<SegmentedGarmentPart>();
                part.schemaVersion = SegmentedGarmentPart.SupportedSchemaVersion;
                part.finalMaterial = final;
                part.occlusion = occlusion;
                part.renderers = renderers.Select(smr => new SegmentedGarmentPart.RendererLayout
                {
                    renderer = smr,
                    sourceObject = smr.name,
                    vertexCount = smr.sharedMesh.vertexCount,
                }).ToList();
                if (!part.TryValidate(out string reason)) throw new InvalidOperationException($"{prefabPath}: the built prefab does not validate: {reason}");
            }
            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
            if (!ok) throw new InvalidOperationException("Could not save " + prefabPath);
            return saved;
        }
        finally
        {
            if (refresh) PrefabUtility.UnloadPrefabContents(root);
            else UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static Transform Find(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);

    // ── Nearest triangle ────────────────────────────────────────────────────────

    /// <summary>Uniform grid over triangles for nearest-point queries (editor import only).</summary>
    public sealed class TriangleGrid
    {
        private readonly Vector3[] _p;
        private readonly int[] _t;
        private readonly float _cell;
        private readonly Vector3 _min;
        private readonly int _nx, _ny, _nz;
        private readonly Dictionary<int, List<int>> _cells = new();
        private readonly int[] _stamp;
        private int _query;

        public TriangleGrid(Vector3[] positions, int[] triangles, float cell)
        {
            _p = positions; _t = triangles; _cell = cell;
            var b = new Bounds(positions[0], Vector3.zero);
            foreach (var v in positions) b.Encapsulate(v);
            _min = b.min - Vector3.one * cell;
            _nx = Mathf.CeilToInt(b.size.x / cell) + 3; _ny = Mathf.CeilToInt(b.size.y / cell) + 3; _nz = Mathf.CeilToInt(b.size.z / cell) + 3;
            _stamp = new int[triangles.Length / 3];
            for (int t = 0; t < triangles.Length / 3; t++)
            {
                var tb = new Bounds(positions[triangles[t * 3]], Vector3.zero);
                tb.Encapsulate(positions[triangles[t * 3 + 1]]);
                tb.Encapsulate(positions[triangles[t * 3 + 2]]);
                var lo = Cell(tb.min); var hi = Cell(tb.max);
                for (int x = lo.x; x <= hi.x; x++)
                    for (int y = lo.y; y <= hi.y; y++)
                        for (int z = lo.z; z <= hi.z; z++)
                        {
                            int key = Key(x, y, z);
                            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
                            list.Add(t);
                        }
            }
        }

        private Vector3Int Cell(Vector3 p) => new(
            Mathf.Clamp(Mathf.FloorToInt((p.x - _min.x) / _cell), 0, _nx - 1),
            Mathf.Clamp(Mathf.FloorToInt((p.y - _min.y) / _cell), 0, _ny - 1),
            Mathf.Clamp(Mathf.FloorToInt((p.z - _min.z) / _cell), 0, _nz - 1));

        private int Key(int x, int y, int z) => (x * _ny + y) * _nz + z;

        public bool Nearest(Vector3 p, float maxDistance, out int triangle, out Vector3 closest, out Vector3 bary)
        {
            _query++;
            triangle = -1; closest = default; bary = default;
            float best = maxDistance * maxDistance;
            var c = new Vector3Int(Mathf.FloorToInt((p.x - _min.x) / _cell), Mathf.FloorToInt((p.y - _min.y) / _cell), Mathf.FloorToInt((p.z - _min.z) / _cell));
            int rings = Mathf.CeilToInt(maxDistance / _cell) + 1;
            for (int r = 0; r <= rings; r++)
            {
                if (triangle >= 0 && (r - 1) * _cell > Mathf.Sqrt(best)) break;
                for (int x = c.x - r; x <= c.x + r; x++)
                    for (int y = c.y - r; y <= c.y + r; y++)
                        for (int z = c.z - r; z <= c.z + r; z++)
                        {
                            if (Mathf.Max(Mathf.Abs(x - c.x), Mathf.Max(Mathf.Abs(y - c.y), Mathf.Abs(z - c.z))) != r) continue;
                            if (x < 0 || y < 0 || z < 0 || x >= _nx || y >= _ny || z >= _nz) continue;
                            if (!_cells.TryGetValue(Key(x, y, z), out var list)) continue;
                            foreach (int t in list)
                            {
                                if (_stamp[t] == _query) continue;
                                _stamp[t] = _query;
                                var q = ClosestOnTriangle(p, _p[_t[t * 3]], _p[_t[t * 3 + 1]], _p[_t[t * 3 + 2]], out var w);
                                float d = (q - p).sqrMagnitude;
                                if (d < best) { best = d; triangle = t; closest = q; bary = w; }
                            }
                        }
            }
            return triangle >= 0;
        }

        /// <summary>Closest point of triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5) + barycentrics.</summary>
        public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 w)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) { w = new Vector3(1, 0, 0); return a; }
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) { w = new Vector3(0, 1, 0); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); w = new Vector3(1 - v, v, 0); return a + v * ab; }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) { w = new Vector3(0, 0, 1); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { float v = d2 / (d2 - d6); w = new Vector3(1 - v, 0, v); return a + v * ac; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                float v = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                w = new Vector3(0, 1 - v, v);
                return b + v * (c - b);
            }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            w = new Vector3(1 - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }
    }
}
