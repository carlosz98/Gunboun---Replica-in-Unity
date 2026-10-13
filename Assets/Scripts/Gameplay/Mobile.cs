using UnityEngine;

/// <summary>
/// A GunBound "mobile" (Knight, Dragon...). Walks on the destructible terrain with pixel
/// collision, tilts to the slope, aims, takes damage, falls into holes, and plays
/// idle / walk / attack / hit / defeat frames from its sprite sheet.
/// One script for every player – configure each one in the Inspector.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class Mobile : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Shown in the HUD. Empty = guessed from the sprite sheet name.")]
    public string displayName = "";
    [Tooltip("Tick if the source art faces right. GunBound sheets face left.")]
    public bool artFacesRight = false;

    [Header("Stats")]
    public int maxHealth = 1000;
    [Tooltip("Delay added every turn on top of the shot's delay (DragonBound: Knight 550, Dragon 550).")]
    public int baseDelay = 550;
    [Tooltip("Walk speed in world units per second.")]
    public float moveSpeed = 1.1f;
    public float maxFuel = 100f;
    [Tooltip("Fuel spent per world unit walked.")]
    public float fuelPerUnit = 30f;
    [Tooltip("Max height climbable per 0.02 units walked (controls the steepest walkable slope).")]
    public float maxClimbPerStep = 0.05f;
    [Range(0f, 60f)] public float maxTilt = 40f;
    [Tooltip("0 = auto from sprite size.")]
    public float hitRadius = 0f;
    [Tooltip("Pushes the sprite down so transparent padding under the art doesn't make it float.")]
    public float feetSink = 0.03f;

    [Header("Aim")]
    public float minAngle = 0f;
    public float maxAngle = 90f;
    [Tooltip("Barrel pivot offset from the body centre (local).")]
    public Vector2 muzzlePivot = new Vector2(0f, 0.08f);
    public float muzzleDistance = 0.35f;
    [Tooltip("Projectile speed at 100 power.")]
    public float maxShotSpeed = 12f;

    [Header("Weapons (1 / 2 / 3)")]
    public WeaponDef[] weapons = WeaponDef.Defaults();

    [Header("Animation frames (sprite sheet). Override any Animator when set.")]
    public Sprite[] idleFrames;
    public Sprite[] walkFrames;
    public Sprite[] attackFrames;
    public Sprite[] hitFrames;
    public Sprite[] defeatFrames;
    public float animFps = 13f;

    // ------------------------------------------------------------------ runtime state
    public int Health { get; private set; }
    public bool IsAlive { get; private set; } = true;
    public float Fuel { get; private set; }
    public float Angle { get; private set; }
    public int Facing { get; private set; } = -1;
    public bool IsGrounded => grounded;
    public bool IsSettled => !IsAlive || grounded;
    public float Tilt => tilt;
    public Color TeamColor { get; set; } = new Color(0.35f, 0.65f, 1f);

    public float Delay { get; set; }
    public float LastPower { get; set; } = -1f;
    public float LastAngle { get; set; } = -1f;
    public bool SuperShotUsed { get; set; }
    public int SelectedWeapon { get; set; }

    public WeaponDef CurrentWeapon => weapons[Mathf.Clamp(SelectedWeapon, 0, weapons.Length - 1)];
    public float HitRadius => hitRadius;

    /// <summary>Firing angle relative to the horizon (what the HUD shows).</summary>
    public float WorldAngle => Angle + tilt * Facing;

    public Vector2 AimDirection
    {
        get
        {
            float a = WorldAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a) * Facing, Mathf.Sin(a));
        }
    }

    /// <summary>Middle of the body (used for hits, tags and the barrel).</summary>
    public Vector2 Center => transform.position + transform.up * centerOffset;

    public Vector2 MuzzlePosition
    {
        get
        {
            Vector2 pivotWorld = Center + (Vector2)(transform.rotation * (Vector3)muzzlePivot);
            return pivotWorld + AimDirection * muzzleDistance;
        }
    }

    enum AnimState { Idle, Walk, Attack, Hit, Defeat }

    const float WalkStep = 0.02f;
    const float LedgeDrop = 0.08f;
    const float Gravity = 9f;
    const float MaxFallSpeed = 12f;

    SpriteRenderer sr;
    Animator animator;
    LineRenderer aimLine;
    float feetX, feetY, feetOffset, centerOffset, halfWidth, z;
    float tilt, vy;
    bool grounded;
    float flashTimer;
    bool aimVisible;

    AnimState anim;
    Sprite[] animFrames;
    float animClock;
    int animFrame;
    float walkTimer;

    bool HasFrames(Sprite[] f) => f != null && f.Length > 0 && f[0];

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        animator = GetComponent<Animator>();
        if (animator && (HasFrames(idleFrames) || animator.runtimeAnimatorController == null))
        {
            animator.enabled = false; // sprite-sheet frames drive the sprite instead
            animator = null;
        }
        if (HasFrames(idleFrames)) sr.sprite = idleFrames[0];

        // Movement is pixel-based on the terrain mask: physics bodies would fight it.
        foreach (var c in GetComponents<Collider2D>()) Destroy(c);
        if (TryGetComponent(out Rigidbody2D rb)) Destroy(rb);

        // Normalise mirrored scale into flipX so rotation/tilt behave.
        Vector3 s = transform.localScale;
        bool mirrored = s.x < 0f;
        s.x = Mathf.Abs(s.x);
        s.y = Mathf.Abs(s.y);
        transform.localScale = s;
        transform.rotation = Quaternion.identity;
        Facing = (mirrored ^ artFacesRight) ? 1 : -1;

        sr.sortingOrder = 5;
        Bounds b = sr.bounds;
        feetOffset = transform.position.y - b.min.y - feetSink;
        centerOffset = b.center.y - transform.position.y;
        halfWidth = b.extents.x * 0.5f;
        if (hitRadius <= 0f) hitRadius = Mathf.Min(b.extents.x, b.extents.y) * 0.85f;

        z = transform.position.z;
        feetX = transform.position.x;
        feetY = transform.position.y - feetOffset;

        if (weapons == null || weapons.Length == 0) weapons = WeaponDef.Defaults();
        if (string.IsNullOrEmpty(displayName)) displayName = GuessName();

        Health = maxHealth;
        Fuel = maxFuel;
        Angle = Mathf.Round((minAngle + maxAngle) * 0.5f);

        CreateAimLine();
        ApplyFacing();
        Play(AnimState.Idle, true);
    }

    void Start()
    {
        // Drop onto the top surface of the map at our spawn X.
        var t = DestructibleTerrain.Instance;
        if (t && t.TryGetSurfaceBelow(new Vector2(feetX, t.WorldBounds.max.y + 0.5f), out float sy))
        {
            feetY = sy;
            grounded = true;
        }
        ApplyTransform();
    }

    string GuessName()
    {
        string n = (sr.sprite ? sr.sprite.texture.name : name).ToLowerInvariant();
        if (n.StartsWith("knight")) return "Knight";
        if (n.StartsWith("dragon")) return "Dragon";
        if (n.StartsWith("jd")) return "J.D.";
        if (n.StartsWith("ice")) return "Ice";
        return name;
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        float dt = Time.deltaTime;
        if (IsAlive)
        {
            UpdatePhysics(dt);
            UpdateTilt(dt);
            ApplyTransform();

            if (walkTimer > 0f) walkTimer -= dt;
            if (anim == AnimState.Idle || anim == AnimState.Walk)
                Play(walkTimer > 0f && HasFrames(walkFrames) ? AnimState.Walk : AnimState.Idle);
        }
        UpdateAnimation(dt);
        UpdateFlash(dt);
    }

    void LateUpdate()
    {
        if (!aimVisible) return;
        Vector2 from = MuzzlePosition - AimDirection * (muzzleDistance * 0.6f);
        Vector2 to = from + AimDirection * 0.9f;
        aimLine.SetPosition(0, new Vector3(from.x, from.y, z - 0.5f));
        aimLine.SetPosition(1, new Vector3(to.x, to.y, z - 0.5f));
    }

    void UpdatePhysics(float dt)
    {
        var t = DestructibleTerrain.Instance;
        if (!t) return;

        if (grounded)
        {
            // Ground may have been blown away under us.
            if (t.TryGetSurfaceBelow(new Vector2(feetX, feetY + 0.02f), out float sy) && sy >= feetY - 0.02f)
                feetY = sy;
            else
            {
                grounded = false;
                vy = 0f;
            }
        }

        if (!grounded)
        {
            vy = Mathf.Max(vy - Gravity * dt, -MaxFallSpeed);
            float ny = feetY + vy * dt;
            if (t.TryGetSurfaceBelow(new Vector2(feetX, feetY + 0.02f), out float sy) && sy >= ny)
            {
                feetY = sy;
                vy = 0f;
                grounded = true;
            }
            else
            {
                feetY = ny;
            }

            if (feetY < t.WorldBounds.min.y - 1.5f) Die(true);
        }
    }

    void UpdateTilt(float dt)
    {
        var t = DestructibleTerrain.Instance;
        float target = 0f;
        if (t && grounded &&
            t.TryGetSurfaceBelow(new Vector2(feetX - halfWidth, feetY + 0.25f), out float yl) &&
            t.TryGetSurfaceBelow(new Vector2(feetX + halfWidth, feetY + 0.25f), out float yr))
        {
            target = Mathf.Atan2(yr - yl, halfWidth * 2f) * Mathf.Rad2Deg;
            target = Mathf.Clamp(target, -maxTilt, maxTilt);
        }
        tilt = Mathf.Lerp(tilt, target, 1f - Mathf.Exp(-12f * dt));
    }

    void ApplyTransform()
    {
        Quaternion rot = Quaternion.Euler(0f, 0f, tilt);
        Vector3 pos = new Vector3(feetX, feetY, z) + rot * new Vector3(0f, feetOffset, 0f);
        transform.SetPositionAndRotation(pos, rot);
    }

    void UpdateFlash(float dt)
    {
        if (flashTimer <= 0f) return;
        flashTimer -= dt;
        Color dead = HasFrames(defeatFrames) ? Color.white : new Color(0.35f, 0.35f, 0.35f);
        sr.color = flashTimer > 0f
            ? Color.Lerp(Color.white, new Color(1f, 0.35f, 0.35f), flashTimer / 0.25f)
            : (IsAlive ? Color.white : dead);
    }

    // ------------------------------------------------------------------ sprite-sheet animation

    Sprite[] FramesFor(AnimState s)
    {
        switch (s)
        {
            case AnimState.Walk: return HasFrames(walkFrames) ? walkFrames : idleFrames;
            case AnimState.Attack: return attackFrames;
            case AnimState.Hit: return hitFrames;
            case AnimState.Defeat: return defeatFrames;
            default: return idleFrames;
        }
    }

    void Play(AnimState s, bool restart = false)
    {
        Sprite[] f = FramesFor(s);
        if (!HasFrames(f)) return;
        if (anim == s && !restart && animFrames == f) return;
        anim = s;
        animFrames = f;
        animClock = 0f;
        animFrame = 0;
        sr.sprite = f[0];
    }

    void UpdateAnimation(float dt)
    {
        if (animator || !HasFrames(animFrames)) return;
        animClock += dt * animFps;
        if (animClock < 1f) return;

        int adv = (int)animClock;
        animClock -= adv;
        animFrame += adv;

        if (animFrame >= animFrames.Length)
        {
            switch (anim)
            {
                case AnimState.Defeat:
                    animFrame = animFrames.Length - 1; // hold last frame
                    break;
                case AnimState.Attack:
                case AnimState.Hit:
                    Play(IsAlive ? AnimState.Idle : AnimState.Defeat, true);
                    return;
                default:
                    animFrame %= animFrames.Length;
                    break;
            }
        }

        Sprite next = animFrames[animFrame];
        if (next) sr.sprite = next;
    }

    // ------------------------------------------------------------------ commands

    /// <summary>Walks in <paramref name="dir"/> (-1/1). Returns world distance actually moved.</summary>
    public float Move(int dir, float dt)
    {
        if (!IsAlive || dir == 0) return 0f;
        if (dir != Facing) SetFacing(dir);
        if (!grounded || Fuel <= 0f) return 0f;

        var t = DestructibleTerrain.Instance;
        if (!t) return 0f;

        float want = Mathf.Min(moveSpeed * dt, Fuel / fuelPerUnit);
        Bounds b = t.WorldBounds;
        float minX = b.min.x + halfWidth, maxX = b.max.x - halfWidth;
        float moved = 0f;

        while (moved < want - 0.0001f)
        {
            float step = Mathf.Min(WalkStep, want - moved);
            float nx = Mathf.Clamp(feetX + dir * step, minX, maxX);
            if (Mathf.Abs(nx - feetX) < 0.00001f) break; // map edge

            Vector2 probe = new Vector2(nx, feetY + maxClimbPerStep * (step / WalkStep));
            if (t.IsSolid(probe)) break; // too steep / wall

            feetX = nx;
            moved += step;

            if (t.TryGetSurfaceBelow(probe, out float sy) && sy >= feetY - LedgeDrop)
            {
                feetY = sy;
            }
            else
            {
                grounded = false; // walked off a ledge / into a hole
                vy = 0f;
                break;
            }
        }

        if (moved > 0f) walkTimer = 0.12f;
        Fuel = Mathf.Max(0f, Fuel - moved * fuelPerUnit);
        return moved;
    }

    public void AddAngle(float delta)
    {
        Angle = Mathf.Clamp(Angle + delta, minAngle, maxAngle);
    }

    public void SetFacing(int dir)
    {
        Facing = dir >= 0 ? 1 : -1;
        ApplyFacing();
    }

    void ApplyFacing()
    {
        sr.flipX = (Facing == 1) ^ artFacesRight;
    }

    public void BeginTurn()
    {
        Fuel = maxFuel;
        ShowAim(IsAlive);
    }

    public void EndTurn()
    {
        ShowAim(false);
    }

    public void PlayAttack()
    {
        if (IsAlive) Play(AnimState.Attack, true);
    }

    public int TakeDamage(int amount)
    {
        if (!IsAlive || amount <= 0) return 0;
        int dealt = Mathf.Min(amount, Health);
        Health -= dealt;
        flashTimer = 0.25f;
        if (Health <= 0) Die(false);
        else Play(AnimState.Hit, true);
        return dealt;
    }

    void Die(bool fellOff)
    {
        if (!IsAlive) return;
        IsAlive = false;
        Health = 0;
        ShowAim(false);
        if (animator) animator.enabled = false;

        if (fellOff)
        {
            sr.enabled = false;
            var t = DestructibleTerrain.Instance;
            if (GameHUD.Instance)
                GameHUD.Instance.AddPopup(new Vector3(feetX, t ? t.WorldBounds.min.y + 0.5f : feetY, 0f), "FELL!", Color.white);
        }
        else if (HasFrames(defeatFrames))
        {
            Play(AnimState.Defeat, true);
        }
        else
        {
            sr.color = new Color(0.35f, 0.35f, 0.35f);
        }
    }

    // ------------------------------------------------------------------ aim line

    void CreateAimLine()
    {
        var go = new GameObject("AimLine");
        go.transform.SetParent(transform, false);
        aimLine = go.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.positionCount = 2;
        aimLine.startWidth = 0.04f;
        aimLine.endWidth = 0.012f;
        aimLine.numCapVertices = 2;
        aimLine.sharedMaterial = sr.sharedMaterial;
        aimLine.sortingOrder = 6;
        aimLine.startColor = new Color(1f, 1f, 1f, 0.95f);
        aimLine.endColor = new Color(1f, 1f, 1f, 0.15f);
        aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimLine.receiveShadows = false;
        aimLine.enabled = false;
    }

    void ShowAim(bool show)
    {
        aimVisible = show;
        if (aimLine) aimLine.enabled = show;
    }
}
