using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// GunBound / DragonBound match flow: main menu, delay-based turn order, wind,
/// input, volleys, sky strikes, damage and win check. Hot-seat on one keyboard.
///
/// Controls: Left/Right (A/D) move · Up/Down (W/S) angle · hold/release Space to fire
///           1/2/3 pick Shot 1 / Shot 2 / SS · P pass turn · Ctrl+R restart
///
/// Rules follow DragonBound's server logic:
///   turn delay = shot delay + mobile delay + 10 × seconds used;  pass = mobile delay + 10 × seconds used
///   wind angle changes every 2 turns, wind power every 3 turns; wind pushes shells with wind% of gravity.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public class GameManager : MonoBehaviour
{
    public enum Phase { Menu, Starting, Aiming, Flying, Resolving, GameOver }

    public static GameManager Instance { get; private set; }

    [Header("Physics")]
    public float gravity = 6f;
    [Tooltip("Strongest wind that can roll.")]
    public int maxWind = 26;
    [Tooltip("1 = DragonBound (each wind point pushes with 1% of gravity).")]
    public float windStrength = 1f;
    public int turnsPerWindAngleChange = 2;
    public int turnsPerWindPowerChange = 3;

    [Header("Turns")]
    public float turnTime = 20f;
    [Tooltip("Seconds to charge from 0 to 100 power.")]
    public float powerFillTime = 1.8f;
    [Tooltip("Delay added per second spent in your turn (DragonBound: 10).")]
    public float delayPerSecond = 10f;
    [Tooltip("Pause after the last shell lands before the next turn.")]
    public float settleTime = 0.6f;
    [Tooltip("Degrees per second when holding Up/Down (a tap is always 1 degree).")]
    public float angleRepeatSpeed = 30f;

    [Header("Menu (optional – found by name if left empty)")]
    public GameObject mainMenu;
    public Button startButton;
    public Button exitButton;

    [Header("Camera")]
    [Tooltip("Follow the active mobile and the shells (GunBound style). Off = show the whole map.")]
    public bool followCamera = true;
    [Tooltip("Orthographic size while following.")]
    public float followSize = 4f;
    public float followSpeed = 4f;

    [Header("Feedback")]
    public float shakeStrength = 0.15f;

    public static readonly Color[] TeamColors =
    {
        new Color(0.35f, 0.65f, 1f),
        new Color(1f, 0.4f, 0.35f),
        new Color(0.45f, 0.9f, 0.4f),
        new Color(1f, 0.85f, 0.3f),
    };

    public Phase CurrentPhase { get; private set; } = Phase.Starting;
    public List<Mobile> Mobiles { get; } = new List<Mobile>();
    public Mobile Current { get; private set; }
    public Mobile Winner { get; private set; }
    public int TurnNumber { get; private set; }
    public float TurnTimeLeft { get; private set; }
    public float Power { get; private set; }
    public bool Charging { get; private set; }
    public int Wind { get; private set; }
    public float WindAngle { get; private set; }
    public Vector2 WindAcceleration { get; private set; }
    public Vector2 Gravity => new Vector2(0f, -gravity);
    public DestructibleTerrain Terrain => terrain;

    struct PendingShell
    {
        public float time;
        public Mobile owner;
        public ShellDef shell;
        public WeaponDef weapon;     // weapon whose strike triggers on impact (null for strikes)
        public bool fromMuzzle;      // volley shell (uses aim) vs. strike (uses pos/vel)
        public float angle, power;   // for volley shells
        public Vector2 pos, vel;     // for strikes
    }

    readonly List<PendingShell> pending = new List<PendingShell>();
    static bool skipMenuOnce;

    DestructibleTerrain terrain;
    Camera cam;
    Vector3 camBase;
    float overviewSize;
    Bounds backgroundBounds;
    bool hasBackground;
    float shakeTime, shakeAmount;
    Material spriteMaterial;
    int projectilesInFlight;
    float phaseTimer;
    float angleHold;
    float clock;
    int lastIndex = -1;

    // ------------------------------------------------------------------ bootstrap

    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        hooked = false;
        skipMenuOnce = false;
        Instance = null;
    }

    /// <summary>Creates a GameManager automatically if the scene has players but no manager.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBootstrap()
    {
        if (!hooked)
        {
            hooked = true;
            SceneManager.sceneLoaded += (s, m) => EnsureManager();
        }
        EnsureManager();
    }

    static void EnsureManager()
    {
        if (FindAnyObjectByType<GameManager>()) return;

        // Old scenes: give Player1/Player2 a Mobile if they don't have one yet.
        foreach (string n in new[] { "Player1", "Player2" })
        {
            var p = GameObject.Find(n);
            if (p && !p.GetComponent<Mobile>() && p.GetComponent<SpriteRenderer>()) p.AddComponent<Mobile>();
        }

        if (FindObjectsByType<Mobile>(FindObjectsSortMode.None).Length < 2) return;
        new GameObject("GameManager").AddComponent<GameManager>();
    }

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        cam = Camera.main;
        terrain = FindAnyObjectByType<DestructibleTerrain>();
        if (!terrain)
        {
            var stage = GameObject.Find("Stage-Adium");
            if (!stage) stage = GameObject.Find("Bg-Stage");
            if (stage) terrain = stage.AddComponent<DestructibleTerrain>();
        }
        if (!terrain) Debug.LogError("[GameManager] No DestructibleTerrain found. Add it to your stage sprite.");

        DisableLegacyObjects();

        // Soundtrack loops for the whole match.
        foreach (var a in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            if (a.gameObject.name.ToLowerInvariant().Contains("soundtrack")) a.loop = true;

        if (!GetComponent<GameHUD>()) gameObject.AddComponent<GameHUD>();
    }

    /// <summary>Turns off pieces of the old prototype that this system replaces.</summary>
    static void DisableLegacyObjects()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            string n = root.name;
            if (n == "PowerBar" || n == "Counter" || n == "Logic Manager" || n == "LogicManager")
                root.SetActive(false);
        }
        foreach (string n in new[] { "Player1", "Player2" })
        {
            var p = GameObject.Find(n);
            if (!p) continue;
            foreach (Transform child in p.transform)
            {
                string c = child.name;
                if (c == "healthBar" || c.EndsWith("Arrow") || c == "ShotPoint") child.gameObject.SetActive(false);
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        Mobiles.AddRange(FindObjectsByType<Mobile>(FindObjectsSortMode.None));
        Mobiles.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));

        float avgX = 0f;
        foreach (var m in Mobiles) avgX += m.transform.position.x;
        avgX /= Mathf.Max(1, Mobiles.Count);

        for (int i = 0; i < Mobiles.Count; i++)
        {
            var m = Mobiles[i];
            m.TeamColor = TeamColors[i % TeamColors.Length];
            m.SetFacing(m.transform.position.x <= avgX ? 1 : -1);
            m.Delay = Random.Range(0, 100); // random first player
            if (!spriteMaterial)
            {
                var r = m.GetComponent<SpriteRenderer>();
                if (r) spriteMaterial = r.sharedMaterial;
            }
        }

        FitCamera();
        CoverBackground();
        SetupMenu();
    }

    void SetupMenu()
    {
        if (!mainMenu)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "MainMenuScreen" || root.name == "MainMenu") mainMenu = root;
        }

        if (mainMenu)
        {
            foreach (var b in mainMenu.GetComponentsInChildren<Button>(true))
            {
                string n = b.name.ToLowerInvariant();
                if (!startButton && n.Contains("start")) startButton = b;
                if (!exitButton && (n.Contains("exit") || n.Contains("quit"))) exitButton = b;
            }
            if (startButton) startButton.onClick.AddListener(BeginMatch);
            if (exitButton) exitButton.onClick.AddListener(QuitGame);
        }

        if (mainMenu && !skipMenuOnce)
        {
            mainMenu.SetActive(true);
            CurrentPhase = Phase.Menu;
        }
        else
        {
            if (mainMenu) mainMenu.SetActive(false);
            CurrentPhase = Phase.Starting;
            phaseTimer = 1.2f;
        }
        skipMenuOnce = false;
    }

    public void BeginMatch()
    {
        if (CurrentPhase != Phase.Menu) return;
        if (mainMenu) mainMenu.SetActive(false);
        CurrentPhase = Phase.Starting;
        phaseTimer = 1.2f;
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void FitCamera()
    {
        if (!cam) return;
        if (terrain && cam.orthographic)
        {
            Bounds b = terrain.WorldBounds;
            float needed = (b.size.x * 0.5f + 0.4f) / Mathf.Max(0.1f, cam.aspect);
            if (needed > cam.orthographicSize) cam.orthographicSize = needed;
        }
        overviewSize = cam.orthographicSize;
        camBase = cam.transform.position;
    }

    /// <summary>Scales the background sprite up if it doesn't cover the camera view.</summary>
    void CoverBackground()
    {
        if (!cam || !cam.orthographic) return;
        foreach (var r in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (!r.gameObject.name.ToLowerInvariant().StartsWith("background")) continue;
            r.sortingOrder = -10;
            float viewH = cam.orthographicSize * 2f + 0.6f;
            float viewW = viewH * cam.aspect + 0.6f;
            Vector3 size = r.bounds.size;
            float k = Mathf.Max(viewW / Mathf.Max(0.01f, size.x), viewH / Mathf.Max(0.01f, size.y));
            if (k > 1f) r.transform.localScale *= k;
            backgroundBounds = r.bounds;
            hasBackground = true;
        }
    }

    // ------------------------------------------------------------------ loop

    void Update()
    {
        float dt = Time.deltaTime;
        clock += dt;
        var kb = Keyboard.current;

        if (kb != null && kb.rKey.wasPressedThisFrame &&
            (CurrentPhase == Phase.GameOver || kb.ctrlKey.isPressed))
        {
            Restart();
            return;
        }

        switch (CurrentPhase)
        {
            case Phase.Menu:
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) BeginMatch();
                break;

            case Phase.Starting:
                phaseTimer -= dt;
                if (phaseTimer <= 0f) NextTurn();
                break;

            case Phase.Aiming:
                UpdateAiming(kb, dt);
                break;

            case Phase.Flying:
                ProcessPending();
                if (projectilesInFlight <= 0 && pending.Count == 0)
                {
                    CurrentPhase = Phase.Resolving;
                    phaseTimer = settleTime;
                }
                break;

            case Phase.Resolving:
                if (AllSettled()) phaseTimer -= dt;
                if (phaseTimer <= 0f) EndTurn();
                break;
        }

        UpdateCamera(kb, dt);
        UpdateShake(dt);
    }

    // ------------------------------------------------------------------ camera

    void UpdateCamera(Keyboard kb, float dt)
    {
        if (!cam || !cam.orthographic || !terrain) return;

        bool overview = !followCamera || CurrentPhase == Phase.Menu || CurrentPhase == Phase.GameOver ||
                        (kb != null && kb.tabKey.isPressed);
        float size = overview ? overviewSize : Mathf.Min(followSize, overviewSize);
        float k = 1f - Mathf.Exp(-followSpeed * dt);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, k);

        Bounds b = terrain.WorldBounds;
        Vector2 target = b.center;
        if (!overview)
        {
            if (Projectile.Active.Count > 0)
            {
                Vector2 sum = Vector2.zero;
                foreach (var p in Projectile.Active) sum += (Vector2)p.transform.position;
                target = sum / Projectile.Active.Count;
            }
            else if (Current) target = Current.Center + Vector2.up * 0.8f;
        }

        // Keep the view inside the map/background.
        float hh = cam.orthographicSize, hw = hh * cam.aspect;
        Bounds limit = hasBackground ? backgroundBounds : b;
        float minX = Mathf.Max(limit.min.x, b.min.x - 0.6f) + hw, maxX = Mathf.Min(limit.max.x, b.max.x + 0.6f) - hw;
        float minY = b.min.y - 1.2f + hh, maxY = (hasBackground ? limit.max.y : b.max.y + 6f) - hh;
        target.x = minX <= maxX ? Mathf.Clamp(target.x, minX, maxX) : (minX + maxX) * 0.5f;
        target.y = minY <= maxY ? Mathf.Clamp(target.y, minY, maxY) : (minY + maxY) * 0.5f;

        Vector3 goal = new Vector3(target.x, target.y, camBase.z);
        camBase = Vector3.Lerp(camBase, goal, k);
        if (shakeTime <= 0f) cam.transform.position = camBase;
    }

    void UpdateAiming(Keyboard kb, float dt)
    {
        var m = Current;
        if (!m || !m.IsAlive)
        {
            EndTurn();
            return;
        }

        TurnTimeLeft -= dt;

        if (kb != null)
        {
            if (!Charging)
            {
                int move = ((kb.rightArrowKey.isPressed || kb.dKey.isPressed) ? 1 : 0)
                         - ((kb.leftArrowKey.isPressed || kb.aKey.isPressed) ? 1 : 0);
                if (move != 0) m.Move(move, dt);

                int aim = ((kb.upArrowKey.isPressed || kb.wKey.isPressed) ? 1 : 0)
                        - ((kb.downArrowKey.isPressed || kb.sKey.isPressed) ? 1 : 0);
                if (aim != 0)
                {
                    if (angleHold == 0f) m.AddAngle(aim);
                    angleHold += dt;
                    if (angleHold > 0.3f) m.AddAngle(aim * angleRepeatSpeed * dt);
                }
                else angleHold = 0f;

                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) SelectWeapon(m, 0);
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) SelectWeapon(m, 1);
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) SelectWeapon(m, 2);

                if (kb.pKey.wasPressedThisFrame)
                {
                    Pass();
                    return;
                }

                if (kb.spaceKey.wasPressedThisFrame && m.IsGrounded)
                {
                    Charging = true;
                    Power = 0f;
                }
            }
            else
            {
                Power = Mathf.Min(100f, Power + 100f / powerFillTime * dt);
                if (kb.spaceKey.wasReleasedThisFrame || !kb.spaceKey.isPressed || Power >= 100f)
                {
                    Fire();
                    return;
                }
            }
        }

        if (TurnTimeLeft <= 0f)
        {
            if (Charging) Fire();
            else Pass();
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Test hook: fire the current mobile's weapon at a given power.</summary>
    public void DebugFire(int weaponIndex, float power)
    {
        if (CurrentPhase != Phase.Aiming || !Current) return;
        SelectWeapon(Current, weaponIndex);
        Power = Mathf.Clamp(power, 0f, 100f);
        Fire();
    }
#endif

    void SelectWeapon(Mobile m, int index)
    {
        if (!CanUseWeapon(m, index)) return;
        m.SelectedWeapon = index;
    }

    public bool CanUseWeapon(Mobile m, int index)
    {
        return index < m.weapons.Length && !(m.weapons[index].oncePerMatch && m.SuperShotUsed);
    }

    float SecondsUsed => Mathf.Clamp(turnTime - TurnTimeLeft, 0f, turnTime);

    // ------------------------------------------------------------------ turns

    void NextTurn()
    {
        Mobile next = null;
        int count = Mobiles.Count;
        // Lowest delay goes next; ties go to whoever is next after the previous player.
        for (int k = 1; k <= count; k++)
        {
            var m = Mobiles[(lastIndex + k + count) % count];
            if (!m.IsAlive) continue;
            if (next == null || m.Delay < next.Delay) next = m;
        }

        if (next == null)
        {
            GameOver(null);
            return;
        }

        float min = next.Delay;
        foreach (var m in Mobiles) if (m.IsAlive) m.Delay -= min; // keep numbers small

        Current = next;
        lastIndex = Mobiles.IndexOf(next);
        TurnNumber++;
        UpdateWind();
        TurnTimeLeft = turnTime;
        Power = 0f;
        Charging = false;
        angleHold = 0f;
        Current.BeginTurn();
        CurrentPhase = Phase.Aiming;
    }

    void UpdateWind()
    {
        bool first = TurnNumber == 1;
        if (first || (turnsPerWindPowerChange > 0 && TurnNumber % turnsPerWindPowerChange == 0))
            Wind = Random.Range(0, maxWind + 1);
        if (first || (turnsPerWindAngleChange > 0 && TurnNumber % turnsPerWindAngleChange == 0))
            WindAngle = Random.Range(0, 72) * 5f;

        float a = WindAngle * Mathf.Deg2Rad;
        WindAcceleration = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (gravity * Wind / 100f * windStrength);
    }

    void Fire()
    {
        var m = Current;
        WeaponDef w = m.CurrentWeapon;
        Charging = false;
        m.LastPower = Power;
        m.LastAngle = m.WorldAngle;
        m.EndTurn();
        m.PlayAttack();

        if (w.volley != null && w.volley.Length > 0)
        {
            foreach (var v in w.volley)
                Queue(new PendingShell
                {
                    time = clock + v.launchDelay, owner = m, shell = w.shell, weapon = w, fromMuzzle = true,
                    angle = v.angleOffset, power = Power + v.powerOffset
                });
        }
        else
        {
            Queue(new PendingShell { time = clock, owner = m, shell = w.shell, weapon = w, fromMuzzle = true, angle = 0f, power = Power });
        }
        ProcessPending();

        if (w.oncePerMatch)
        {
            m.SuperShotUsed = true;
            m.SelectedWeapon = 0;
        }

        m.Delay += w.delay + m.baseDelay + SecondsUsed * delayPerSecond;
        CurrentPhase = Phase.Flying;
    }

    void Pass()
    {
        var m = Current;
        Charging = false;
        m.EndTurn();
        m.Delay += m.baseDelay + SecondsUsed * delayPerSecond;
        CurrentPhase = Phase.Resolving;
        phaseTimer = 0.3f;
    }

    void EndTurn()
    {
        Mobile alive = null;
        int aliveCount = 0;
        foreach (var m in Mobiles)
        {
            if (!m.IsAlive) continue;
            aliveCount++;
            alive = m;
        }

        if (aliveCount <= 1) GameOver(alive);
        else NextTurn();
    }

    void GameOver(Mobile winner)
    {
        Winner = winner;
        if (Current) Current.EndTurn();
        CurrentPhase = Phase.GameOver;
    }

    bool AllSettled()
    {
        foreach (var m in Mobiles) if (!m.IsSettled) return false;
        return true;
    }

    void Restart()
    {
        skipMenuOnce = true;
        var scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }

    // ------------------------------------------------------------------ shells

    void Queue(PendingShell p)
    {
        pending.Add(p);
    }

    void ProcessPending()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            PendingShell p = pending[i];
            if (p.time > clock) continue;
            pending.RemoveAt(i);
            Launch(p);
        }
    }

    void Launch(PendingShell p)
    {
        Vector2 pos, vel;
        if (p.fromMuzzle)
        {
            if (!p.owner) return;
            Vector2 dir = Quaternion.Euler(0f, 0f, p.angle * p.owner.Facing) * p.owner.AimDirection;
            float speed = Mathf.Clamp(p.power, 0f, 120f) / 100f * p.owner.maxShotSpeed;
            pos = p.owner.MuzzlePosition;
            vel = dir * speed;
        }
        else
        {
            pos = p.pos;
            vel = p.vel;
        }

        Projectile.Spawn(pos, vel, p.shell, p.weapon, p.owner, spriteMaterial);
        projectilesInFlight++;
    }

    // ------------------------------------------------------------------ combat (called by Projectile)

    public Mobile FindMobileAt(Vector2 pos, float radius, Mobile ignore)
    {
        for (int i = 0; i < Mobiles.Count; i++)
        {
            var m = Mobiles[i];
            if (m == ignore || !m.IsAlive) continue;
            float r = m.HitRadius + radius;
            if ((pos - m.Center).sqrMagnitude <= r * r) return m;
        }
        return null;
    }

    public bool IsOutOfPlay(Vector2 pos)
    {
        if (!terrain) return pos.y < -50f;
        Bounds b = terrain.WorldBounds;
        return pos.y < b.min.y - 2f || pos.x < b.min.x - 8f || pos.x > b.max.x + 8f;
    }

    /// <summary>A shell hit terrain or a mobile.</summary>
    public void OnShellImpact(Vector2 pos, ShellDef shell, WeaponDef weapon, Mobile shooter, Mobile directHit)
    {
        if (shell.damage > 0) Explode(pos, shell, shooter, directHit);
        else ExplosionFX.Spawn(pos, shell, spriteMaterial); // trigger shell: just a spark

        if (weapon == null || !weapon.useStrike || weapon.strike == null || weapon.strike.shell == null) return;

        StrikeDef s = weapon.strike;
        float[] offsets = (s.xOffsets != null && s.xOffsets.Length > 0) ? s.xOffsets : new[] { 0f };
        int facing = shooter ? shooter.Facing : 1;
        for (int i = 0; i < offsets.Length; i++)
        {
            Queue(new PendingShell
            {
                time = clock + s.delay + i * s.interval,
                owner = shooter,
                shell = s.shell,
                weapon = null,
                fromMuzzle = false,
                pos = new Vector2(pos.x + offsets[i] * facing, pos.y + s.height),
                vel = new Vector2(0f, -s.fallSpeed)
            });
        }
    }

    void Explode(Vector2 pos, ShellDef shell, Mobile shooter, Mobile directHit)
    {
        if (terrain) terrain.Carve(pos, shell.crater);
        ExplosionFX.Spawn(pos, shell, spriteMaterial);
        Shake(0.25f, shakeStrength * Mathf.Clamp(shell.crater.x / 0.4f, 0.6f, 2f));

        foreach (var m in Mobiles)
        {
            if (!m.IsAlive) continue;
            float d = Vector2.Distance(pos, m.Center) - m.HitRadius;
            if (m != directHit && d > shell.blastRadius) continue;

            float falloff = (m == directHit || d <= 0f) ? 1f : 1f - Mathf.Clamp01(d / shell.blastRadius) * 0.75f;
            int dealt = m.TakeDamage(Mathf.RoundToInt(shell.damage * falloff));
            if (dealt > 0 && GameHUD.Instance)
                GameHUD.Instance.AddPopup(m.Center + Vector2.up * 0.45f, "-" + dealt,
                    m == shooter ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 0.95f, 0.4f));
        }
    }

    public void OnProjectileFinished()
    {
        projectilesInFlight = Mathf.Max(0, projectilesInFlight - 1);
    }

    // ------------------------------------------------------------------ camera shake

    void Shake(float duration, float amount)
    {
        shakeTime = Mathf.Max(shakeTime, duration);
        shakeAmount = Mathf.Max(shakeAmount, amount);
    }

    void UpdateShake(float dt)
    {
        if (!cam || shakeTime <= 0f) return;
        shakeTime -= dt;
        if (shakeTime <= 0f)
        {
            cam.transform.position = camBase;
            shakeAmount = 0f;
            return;
        }
        Vector2 o = Random.insideUnitCircle * shakeAmount * Mathf.Clamp01(shakeTime / 0.25f);
        cam.transform.position = camBase + new Vector3(o.x, o.y, 0f);
    }
}
