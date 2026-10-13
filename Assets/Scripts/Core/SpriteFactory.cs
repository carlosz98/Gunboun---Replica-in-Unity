using UnityEngine;

/// <summary>
/// Procedurally generated sprites/textures shared by the whole game (projectiles, explosions, HUD).
/// Created lazily once and cached, so nothing needs to be assigned in the Inspector.
/// </summary>
public static class SpriteFactory
{
    static Sprite circle;
    static Texture2D white;
    static Texture2D arrow;

    /// <summary>Soft-edged white circle, 1 world unit in diameter at scale 1.</summary>
    public static Sprite Circle
    {
        get
        {
            if (circle == null) circle = CreateCircle(64);
            return circle;
        }
    }

    /// <summary>1x1 white texture for HUD bars and boxes.</summary>
    public static Texture2D White
    {
        get
        {
            if (white == null)
            {
                white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "HUD_White", hideFlags = HideFlags.DontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply(false, true);
            }
            return white;
        }
    }

    /// <summary>White arrow pointing right (+X), used for wind and markers.</summary>
    public static Texture2D Arrow
    {
        get
        {
            if (arrow == null) arrow = CreateArrow(32);
            return arrow;
        }
    }

    static Sprite CreateCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        var px = new Color32[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        s.name = "Circle";
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    static Texture2D CreateArrow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Arrow",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        var px = new Color32[size * size];
        float mid = size * 0.5f;
        float headStart = size * 0.55f;
        float shaftHalf = size * 0.09f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = x + 0.5f, cy = y + 0.5f;
                bool shaft = cx >= size * 0.08f && cx <= headStart + 1f && Mathf.Abs(cy - mid) <= shaftHalf;
                bool head = cx >= headStart && cx <= size * 0.95f &&
                            Mathf.Abs(cy - mid) <= (size * 0.95f - cx) * 0.85f;
                px[y * size + x] = (shaft || head) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}
