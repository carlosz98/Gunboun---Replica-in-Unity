using UnityEngine;

/// <summary>
/// One projectile type: damage, crater, ballistics and visuals.
/// Used both for the shells you fire and for "strike" shells that drop from the sky
/// on the impact point (Knight's swords, Dragon's SS rain).
/// </summary>
[System.Serializable]
public class ShellDef
{
    [Tooltip("Damage on a direct hit (falls off toward the edge of the blast). 0 = this shell doesn't explode; it only triggers the weapon's strike.")]
    public int damage = 250;
    [Tooltip("Crater radii (x = half width, y = half height) in world units. DragonBound's pala_bunge / 100.")]
    public Vector2 crater = new Vector2(0.4f, 0.3f);
    [Tooltip("Radius that deals damage, in world units.")]
    public float blastRadius = 0.5f;
    public float gravityScale = 1f;
    public bool affectedByWind = true;
    [Tooltip("Collision diameter of the shell.")]
    public float hitSize = 0.16f;

    [Header("Visuals")]
    public Color trailColor = new Color(1f, 0.9f, 0.5f);
    [Tooltip("Animated shell sprites. Empty = procedural glowing circle.")]
    public Sprite[] frames;
    public float fps = 18f;
    [Tooltip("World size of the sprite's longest side.")]
    public float spriteSize = 0.45f;
    [Tooltip("Rotate the sprite to its flight direction (for art drawn pointing right).")]
    public bool alignToVelocity;

    [Tooltip("Animated explosion sprites. Empty = procedural flash.")]
    public Sprite[] explosionFrames;
    public float explosionFps = 24f;
    [Tooltip("World size of the explosion. 0 = based on blast radius.")]
    public float explosionSize = 0f;

    public ShellDef Clone() => (ShellDef)MemberwiseClone();
}

/// <summary>One shell in a volley: when it launches and how it deviates from your aim.</summary>
[System.Serializable]
public class VolleyShot
{
    [Tooltip("Seconds after firing.")]
    public float launchDelay;
    [Tooltip("Degrees added to your angle.")]
    public float angleOffset;
    [Tooltip("Power points added to your power.")]
    public float powerOffset;

    public VolleyShot() { }
    public VolleyShot(float delay, float angle, float power)
    {
        launchDelay = delay;
        angleOffset = angle;
        powerOffset = power;
    }
}

/// <summary>Shells that fall from the sky onto the impact point after the main shell lands.</summary>
[System.Serializable]
public class StrikeDef
{
    [Tooltip("Seconds after the main shell lands.")]
    public float delay = 0.4f;
    [Tooltip("How far above the impact point the strikes start.")]
    public float height = 4.5f;
    public float fallSpeed = 9f;
    [Tooltip("Seconds between each strike.")]
    public float interval = 0.08f;
    [Tooltip("Horizontal offset of each strike from the impact point (one strike per entry).")]
    public float[] xOffsets = { 0f };
    public ShellDef shell = new ShellDef { gravityScale = 0.6f, affectedByWind = false, alignToVelocity = true };
}

/// <summary>A weapon slot (Shot 1 / Shot 2 / SS). Editable per mobile in the Inspector.</summary>
[System.Serializable]
public class WeaponDef
{
    public string name = "Shot 1";
    [Tooltip("Turn delay added when fired (plus the mobile's base delay).")]
    public int delay = 250;
    [Tooltip("Can only be fired once per match (Super Shot).")]
    public bool oncePerMatch;

    [Tooltip("The shell you fire.")]
    public ShellDef shell = new ShellDef();
    [Tooltip("Extra shells: launch timing and angle/power offsets. Empty = a single shell.")]
    public VolleyShot[] volley = new VolleyShot[0];

    [Tooltip("Drop strikes from the sky where the shell lands.")]
    public bool useStrike;
    public StrikeDef strike = new StrikeDef();

    public int ShellCount => volley != null && volley.Length > 0 ? volley.Length : 1;

    /// <summary>Generic Shot 1 / Shot 2 / SS used when nothing is set up in the Inspector.</summary>
    public static WeaponDef[] Defaults()
    {
        return new[]
        {
            new WeaponDef
            {
                name = "Shot 1", delay = 250,
                shell = new ShellDef { damage = 250, crater = new Vector2(0.42f, 0.32f), blastRadius = 0.55f }
            },
            new WeaponDef
            {
                name = "Shot 2", delay = 400,
                shell = new ShellDef { damage = 150, crater = new Vector2(0.34f, 0.26f), blastRadius = 0.45f, trailColor = new Color(0.55f, 0.9f, 1f) },
                volley = new[] { new VolleyShot(0f, 0f, 0f), new VolleyShot(0.1f, 3f, 3f) }
            },
            new WeaponDef
            {
                name = "SS", delay = 800, oncePerMatch = true,
                shell = new ShellDef { damage = 150, crater = new Vector2(0.4f, 0.3f), blastRadius = 0.5f, trailColor = new Color(1f, 0.4f, 0.3f), hitSize = 0.22f },
                useStrike = true,
                strike = new StrikeDef
                {
                    xOffsets = new[] { -0.6f, -0.2f, 0.2f, 0.6f },
                    shell = new ShellDef { damage = 90, crater = new Vector2(0.37f, 0.27f), blastRadius = 0.45f, gravityScale = 0.6f, affectedByWind = false, alignToVelocity = true, trailColor = new Color(1f, 0.5f, 0.4f) }
                }
            }
        };
    }
}
