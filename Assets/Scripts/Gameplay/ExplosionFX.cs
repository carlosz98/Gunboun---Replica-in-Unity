using UnityEngine;

/// <summary>
/// Explosion effect. Plays the weapon's explosion sprite frames once, or falls back
/// to a procedural expanding flash when the weapon has none.
/// </summary>
public class ExplosionFX : MonoBehaviour
{
    SpriteRenderer sr;
    Sprite[] frames;
    float fps;
    Color tint;
    float duration;
    float maxScale;
    float t;

    public static void Spawn(Vector2 position, ShellDef weapon, Material material)
    {
        float size = weapon.explosionSize > 0f ? weapon.explosionSize : weapon.blastRadius * 2.4f;

        if (weapon.explosionFrames != null && weapon.explosionFrames.Length > 0 && weapon.explosionFrames[0])
        {
            var go = new GameObject("Explosion");
            go.transform.position = new Vector3(position.x, position.y, -3f);
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingOrder = 9;
            if (material) r.sharedMaterial = material;
            r.sprite = weapon.explosionFrames[0];
            Vector3 b = r.sprite.bounds.size;
            go.transform.localScale = Vector3.one * (size / Mathf.Max(0.0001f, Mathf.Max(b.x, b.y)));

            var fx = go.AddComponent<ExplosionFX>();
            fx.sr = r;
            fx.frames = weapon.explosionFrames;
            fx.fps = Mathf.Max(1f, weapon.explosionFps);
            fx.duration = weapon.explosionFrames.Length / fx.fps;
            Create(position, size * 0.45f, Color.white, 0.15f, 10, material); // quick hot flash
            return;
        }

        Create(position, size, weapon.trailColor, 0.45f, 9, material);
        Create(position, size * 0.55f, Color.white, 0.2f, 10, material);
    }

    static void Create(Vector2 position, float size, Color tint, float duration, int order, Material material)
    {
        var go = new GameObject("Explosion");
        go.transform.position = new Vector3(position.x, position.y, -3f);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = SpriteFactory.Circle;
        r.sortingOrder = order;
        if (material) r.sharedMaterial = material;
        var fx = go.AddComponent<ExplosionFX>();
        fx.sr = r;
        fx.tint = tint;
        fx.duration = duration;
        fx.maxScale = size;
        fx.Tick(0f);
    }

    void Update()
    {
        Tick(Time.deltaTime);
    }

    void Tick(float dt)
    {
        t += dt;
        if (t >= duration)
        {
            Destroy(gameObject);
            return;
        }

        if (frames != null)
        {
            int i = Mathf.Min(frames.Length - 1, (int)(t * fps));
            if (frames[i]) sr.sprite = frames[i];
            return;
        }

        float k = t / duration;
        float ease = 1f - (1f - k) * (1f - k);
        transform.localScale = Vector3.one * Mathf.Lerp(maxScale * 0.25f, maxScale, ease);
        Color c = Color.Lerp(new Color(1f, 0.95f, 0.75f), tint, k);
        c.a = 1f - k * k;
        sr.color = c;
    }
}
