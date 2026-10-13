using UnityEngine;

/// <summary>
/// Pixel-perfect destructible terrain (the GunBound "map" layer).
/// Copies the stage sprite into a private runtime texture, keeps a solid/empty mask,
/// and lets shots carve craters into it. All collision in the game queries this mask,
/// so the stage needs no Collider2D (any present are removed at runtime).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class DestructibleTerrain : MonoBehaviour
{
    public static DestructibleTerrain Instance { get; private set; }

    [Tooltip("Pixels with alpha at or above this are solid ground.")]
    [Range(0.05f, 1f)] public float solidAlphaThreshold = 0.5f;

    [Tooltip("Width in pixels of the scorched ring drawn around each crater.")]
    [Range(0, 10)] public int scorchWidth = 3;

    [Tooltip("How much the scorched ring is darkened.")]
    [Range(0f, 1f)] public float scorchDarken = 0.4f;

    SpriteRenderer sr;
    Texture2D tex;
    Sprite runtimeSprite;
    Color32[] pixels;
    bool[] solid;
    int width, height;
    float ppu;
    Vector2 pivot;
    bool dirty;

    public bool IsReady => solid != null;
    public Bounds WorldBounds => sr.bounds;

    void Awake()
    {
        Instance = this;
        sr = GetComponent<SpriteRenderer>();
        sr.sortingOrder = 0;

        // Pixel collision replaces physics colliders on the stage.
        foreach (var c in GetComponents<Collider2D>()) Destroy(c);
        if (TryGetComponent(out Rigidbody2D rb)) Destroy(rb);

        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (runtimeSprite) Destroy(runtimeSprite);
        if (tex) Destroy(tex);
    }

    // Upload once per frame no matter how many craters were carved this frame.
    void LateUpdate()
    {
        if (!dirty) return;
        tex.SetPixels32(pixels);
        tex.Apply(false);
        dirty = false;
    }

    void Build()
    {
        Sprite src = sr.sprite;
        if (src == null)
        {
            Debug.LogError("[DestructibleTerrain] No sprite assigned to " + name, this);
            enabled = false;
            return;
        }

        Rect r = src.rect;
        width = Mathf.RoundToInt(r.width);
        height = Mathf.RoundToInt(r.height);
        ppu = src.pixelsPerUnit;
        pivot = src.pivot;

        pixels = ReadSpritePixels(src.texture, Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), width, height);

        tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "DestructibleTerrain_Runtime",
            filterMode = src.texture.filterMode,
            wrapMode = TextureWrapMode.Clamp
        };
        tex.SetPixels32(pixels);
        tex.Apply(false);

        solid = new bool[width * height];
        byte threshold = (byte)Mathf.RoundToInt(solidAlphaThreshold * 255f);
        for (int i = 0; i < pixels.Length; i++) solid[i] = pixels[i].a >= threshold;

        runtimeSprite = Sprite.Create(tex, new Rect(0, 0, width, height),
            new Vector2(pivot.x / width, pivot.y / height), ppu, 0, SpriteMeshType.FullRect);
        runtimeSprite.name = src.name + "_Destructible";
        sr.sprite = runtimeSprite;
    }

    /// <summary>
    /// Reads the sprite's pixels. Uses CPU read when the texture is readable & uncompressed,
    /// otherwise falls back to a GPU copy so it works with any import settings.
    /// </summary>
    static Color32[] ReadSpritePixels(Texture2D source, int x, int y, int w, int h)
    {
        if (source.isReadable)
        {
            try
            {
                Color[] c = source.GetPixels(x, y, w, h);
                var result = new Color32[c.Length];
                for (int i = 0; i < c.Length; i++) result[i] = c[i];
                return result;
            }
            catch (UnityException) { /* compressed format: use GPU path */ }
        }

        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tmp.ReadPixels(new Rect(x, y, w, h), 0, 0, false);
        tmp.Apply(false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Color32[] px = tmp.GetPixels32();
        Destroy(tmp);
        return px;
    }

    // ---------------------------------------------------------------- coordinate helpers

    public Vector2 WorldToPixel(Vector2 world)
    {
        Vector3 l = transform.InverseTransformPoint(world);
        return new Vector2(l.x * ppu + pivot.x, l.y * ppu + pivot.y);
    }

    public Vector2 PixelToWorld(float px, float py)
    {
        return transform.TransformPoint(new Vector3((px - pivot.x) / ppu, (py - pivot.y) / ppu, 0f));
    }

    bool SolidPixel(int x, int y)
    {
        return x >= 0 && y >= 0 && x < width && y < height && solid[y * width + x];
    }

    // ---------------------------------------------------------------- queries

    public bool IsSolid(Vector2 world)
    {
        if (solid == null) return false;
        Vector2 p = WorldToPixel(world);
        return SolidPixel(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
    }

    /// <summary>
    /// Scans straight down from <paramref name="from"/> and returns the world Y of the first ground surface.
    /// </summary>
    public bool TryGetSurfaceBelow(Vector2 from, out float surfaceY)
    {
        surfaceY = 0f;
        if (solid == null) return false;

        Vector2 p = WorldToPixel(from);
        int x = Mathf.FloorToInt(p.x);
        if (x < 0 || x >= width) return false;

        int y = Mathf.Min(Mathf.FloorToInt(p.y), height - 1);
        for (; y >= 0; y--)
        {
            if (solid[y * width + x])
            {
                surfaceY = PixelToWorld(p.x, y + 1).y;
                return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- destruction

    /// <summary>Carves a circular crater (world-space radius) and scorches its rim.</summary>
    public void Carve(Vector2 worldCenter, float worldRadius)
    {
        Carve(worldCenter, new Vector2(worldRadius, worldRadius));
    }

    /// <summary>Carves an elliptical crater (world-space radii x/y) and scorches its rim.</summary>
    public void Carve(Vector2 worldCenter, Vector2 worldRadii)
    {
        if (solid == null || worldRadii.x <= 0f || worldRadii.y <= 0f) return;

        Vector2 c = WorldToPixel(worldCenter);
        Vector3 s = transform.lossyScale;
        // Radii in pixels per axis (handles non-uniform stage scale).
        float rx = worldRadii.x * ppu / Mathf.Max(0.0001f, Mathf.Abs(s.x));
        float ry = worldRadii.y * ppu / Mathf.Max(0.0001f, Mathf.Abs(s.y));
        float ox = rx + scorchWidth, oy = ry + scorchWidth;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - ox));
        int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(c.x + ox));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(c.y - oy));
        int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(c.y + oy));
        if (x0 > x1 || y0 > y1) return;

        float invRx = 1f / rx, invRy = 1f / ry, invOx = 1f / ox, invOy = 1f / oy;
        float keep = 1f - scorchDarken;
        var clear = new Color32(0, 0, 0, 0);
        bool changed = false;

        for (int y = y0; y <= y1; y++)
        {
            float py = y + 0.5f - c.y;
            float iy = py * invRy, oyN = py * invOy;
            int row = y * width;
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f - c.x;
                float ix = px * invRx;
                int i = row + x;

                if (ix * ix + iy * iy <= 1f)
                {
                    if (pixels[i].a != 0)
                    {
                        pixels[i] = clear;
                        solid[i] = false;
                        changed = true;
                    }
                }
                else if (scorchWidth > 0 && solid[i])
                {
                    float ex = px * invOx;
                    if (ex * ex + oyN * oyN <= 1f)
                    {
                        Color32 p = pixels[i];
                        p.r = (byte)(p.r * keep);
                        p.g = (byte)(p.g * keep);
                        p.b = (byte)(p.b * keep);
                        pixels[i] = p;
                        changed = true;
                    }
                }
            }
        }

        if (changed) dirty = true;
    }
}
