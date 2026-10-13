using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ballistic shell affected by gravity and wind. Sub-stepped so fast shots never tunnel
/// through thin terrain or mobiles. Plays the shell's sprite frames if it has any.
/// </summary>
public class Projectile : MonoBehaviour
{
    /// <summary>Shells currently in flight (read by the HUD for off-screen markers).</summary>
    public static readonly List<Projectile> Active = new List<Projectile>();

    const float MaxStepDistance = 0.04f;
    const float MaxLifetime = 20f;

    public Vector2 Velocity => velocity;
    public ShellDef Shell => shell;

    SpriteRenderer sr;
    Vector2 velocity;
    ShellDef shell;
    WeaponDef weapon;   // null for strike shells (they never spawn more strikes)
    Mobile owner;
    float radius;
    float age;
    float frameClock;
    bool ownerClear;
    bool finished;

    /// <param name="weapon">Weapon whose strike triggers on impact, or null.</param>
    public static Projectile Spawn(Vector2 position, Vector2 velocity, ShellDef shell, WeaponDef weapon, Mobile owner, Material material)
    {
        var go = new GameObject("Shell");
        go.transform.position = new Vector3(position.x, position.y, -2f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 8;
        if (material) sr.sharedMaterial = material;

        bool animated = shell.frames != null && shell.frames.Length > 0 && shell.frames[0];
        if (animated)
        {
            sr.sprite = shell.frames[0];
            Vector3 size = sr.sprite.bounds.size;
            float longest = Mathf.Max(0.0001f, Mathf.Max(size.x, size.y));
            go.transform.localScale = Vector3.one * (shell.spriteSize / longest);
        }
        else
        {
            sr.sprite = SpriteFactory.Circle;
            sr.color = shell.trailColor;
            go.transform.localScale = Vector3.one * Mathf.Max(0.1f, shell.hitSize);
        }

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.minVertexDistance = 0.05f;
        trail.startWidth = Mathf.Max(shell.hitSize, shell.spriteSize * 0.3f) * 0.7f;
        trail.endWidth = 0f;
        trail.sortingOrder = 7;
        trail.startColor = new Color(shell.trailColor.r, shell.trailColor.g, shell.trailColor.b, 0.6f);
        trail.endColor = new Color(shell.trailColor.r, shell.trailColor.g, shell.trailColor.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        if (material) trail.sharedMaterial = material;

        var p = go.AddComponent<Projectile>();
        p.sr = sr;
        p.velocity = velocity;
        p.shell = shell;
        p.weapon = weapon;
        p.owner = owner;
        p.radius = shell.hitSize * 0.5f;
        p.UpdateVisual(0f);
        return p;
    }

    void OnEnable() { Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    void Update()
    {
        if (finished) return;
        var gm = GameManager.Instance;
        var terrain = DestructibleTerrain.Instance;
        if (!gm) { Finish(); return; }

        float dt = Time.deltaTime;
        age += dt;
        Vector2 accel = gm.Gravity * shell.gravityScale;
        if (shell.affectedByWind) accel += gm.WindAcceleration;
        Vector2 pos = transform.position;

        int steps = Mathf.Max(1, Mathf.CeilToInt(velocity.magnitude * dt / MaxStepDistance));
        float sdt = dt / steps;

        for (int i = 0; i < steps; i++)
        {
            velocity += accel * sdt;
            pos += velocity * sdt;

            if (!ownerClear && owner &&
                (pos - owner.Center).sqrMagnitude > (owner.HitRadius + radius) * (owner.HitRadius + radius))
                ownerClear = true;

            Mobile hit = gm.FindMobileAt(pos, radius, ownerClear ? null : owner);
            if (hit || (terrain && terrain.IsSolid(pos)))
            {
                SetPosition(pos);
                gm.OnShellImpact(pos, shell, weapon, owner, hit);
                Finish();
                return;
            }

            if (gm.IsOutOfPlay(pos) || age > MaxLifetime)
            {
                Finish();
                return;
            }
        }

        SetPosition(pos);
        UpdateVisual(dt);
    }

    void UpdateVisual(float dt)
    {
        var frames = shell.frames;
        if (frames != null && frames.Length > 1)
        {
            frameClock += dt * shell.fps;
            Sprite s = frames[(int)frameClock % frames.Length];
            if (s) sr.sprite = s;
        }

        if (shell.alignToVelocity && velocity.sqrMagnitude > 0.0001f)
        {
            float a = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, a);
        }
    }

    void SetPosition(Vector2 pos)
    {
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        if (GameManager.Instance) GameManager.Instance.OnProjectileFinished();
        Destroy(gameObject);
    }
}
