using UnityEngine;
using UnityEngine.UI;

/// <summary>Draws animated light on a cabinet seam. Geometry is independent of the cabinet artwork.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class JukeboxNeon : MaskableGraphic
{
    [SerializeField] private float radius;
    [SerializeField] private bool underline;
    [SerializeField] private float phase;
    [SerializeField] private float leftShear, rightShear;

    public void Configure(float cornerRadius, bool isUnderline, float offset, float left = 0, float right = 0)
    {
        radius = cornerRadius;
        underline = isUnderline;
        phase = offset;
        leftShear = left; rightShear = right;
        raycastTarget = false;
        SetVerticesDirty();
    }

    private void Update() => SetVerticesDirty();

    public static Color Tone(float phase) =>
        Color.HSVToRGB(Mathf.Repeat(.65f + .17f * Mathf.Sin(Time.unscaledTime * .75f + phase), 1), .9f, 1);

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect bounds = rectTransform.rect;
        if (bounds.width < 1 || bounds.height < 1) return;
        float time = Time.unscaledTime;
        float offset = phase + (underline ? transform.parent.GetSiblingIndex() * .79f : 0);
        int segments = underline ? 36 : 128;
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments, b = (float)(i + 1) / segments;
            Vector2 start = Shear(bounds, Point(bounds, a)), end = Shear(bounds, Point(bounds, b));
            Vector2 direction = (end - start).normalized;
            Vector2 normal = new(-direction.y, direction.x);
            float peakA = Peak(a, time, offset, underline) * color.a, peakB = Peak(b, time, offset, underline) * color.a;
            if (Mathf.Max(peakA, peakB) < .025f) continue;
            Color tintA = Tone(offset + a * 11), tintB = Tone(offset + b * 11);
            Strip(mesh, start, end, normal, 7, tintA, tintB, peakA * .22f, peakB * .22f);
            Strip(mesh, start, end, normal, 3, tintA, tintB, peakA * .52f, peakB * .52f);
            Strip(mesh, start, end, normal, 1.2f, Color.Lerp(tintA, Color.white, peakA * .8f),
                Color.Lerp(tintB, Color.white, peakB * .8f), peakA, peakB);
        }
    }

    private static float Peak(float t, float time, float phase, bool underline)
    {
        float pulse = .5f + .5f * Mathf.Cos((t - time * .095f) * (underline ? 6.283185f : 18.84956f) + phase);
        float taper = underline ? Mathf.Clamp01(Mathf.Min(t, 1 - t) * 22) : 1;
        return taper * ((underline ? .07f : 0f) + .86f * Mathf.Pow(pulse, 17));
    }

    private Vector2 Shear(Rect rect, Vector2 point)
    {
        float widthFraction = (point.x - rect.xMin) / rect.width;
        float heightFraction = (point.y - rect.yMin) / rect.height;
        point.x += (heightFraction - .5f) * Mathf.Lerp(leftShear, rightShear, widthFraction);
        return point;
    }

    private Vector2 Point(Rect rect, float t)
    {
        if (underline) return new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, t), rect.center.y);
        float r = Mathf.Min(radius, rect.width * .5f, rect.height * .5f);
        float straightX = rect.width - 2 * r, straightY = rect.height - 2 * r;
        float arc = Mathf.PI * r * .5f;
        float distance = (2 * (straightX + straightY) + 4 * arc) * t;
        if (distance <= straightX) return new Vector2(rect.xMin + r + distance, rect.yMax);
        distance -= straightX;
        if (distance <= arc) return Corner(rect.xMax - r, rect.yMax - r, r, 90, distance / arc);
        distance -= arc;
        if (distance <= straightY) return new Vector2(rect.xMax, rect.yMax - r - distance);
        distance -= straightY;
        if (distance <= arc) return Corner(rect.xMax - r, rect.yMin + r, r, 0, distance / arc);
        distance -= arc;
        if (distance <= straightX) return new Vector2(rect.xMax - r - distance, rect.yMin);
        distance -= straightX;
        if (distance <= arc) return Corner(rect.xMin + r, rect.yMin + r, r, -90, distance / arc);
        distance -= arc;
        if (distance <= straightY) return new Vector2(rect.xMin, rect.yMin + r + distance);
        return Corner(rect.xMin + r, rect.yMax - r, r, 180, (distance - straightY) / arc);
    }

    private static Vector2 Corner(float x, float y, float radius, float from, float t)
    {
        float angle = (from - 90 * t) * Mathf.Deg2Rad;
        return new Vector2(x + Mathf.Cos(angle) * radius, y + Mathf.Sin(angle) * radius);
    }

    private static void Strip(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 normal, float width,
        Color tintA, Color tintB, float alphaA, float alphaB)
    {
        int index = mesh.currentVertCount;
        tintA.a = tintB.a = 0;
        mesh.AddVert(a - normal * width, tintA, Vector2.zero);
        mesh.AddVert(b - normal * width, tintB, Vector2.zero);
        tintA.a = alphaA;
        tintB.a = alphaB;
        mesh.AddVert(a, tintA, Vector2.zero);
        mesh.AddVert(b, tintB, Vector2.zero);
        tintA.a = tintB.a = 0;
        mesh.AddVert(a + normal * width, tintA, Vector2.zero);
        mesh.AddVert(b + normal * width, tintB, Vector2.zero);
        mesh.AddTriangle(index, index + 2, index + 3);
        mesh.AddTriangle(index, index + 3, index + 1);
        mesh.AddTriangle(index + 2, index + 4, index + 5);
        mesh.AddTriangle(index + 2, index + 5, index + 3);
    }
}
