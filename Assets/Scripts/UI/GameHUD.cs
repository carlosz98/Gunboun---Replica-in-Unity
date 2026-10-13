using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GunBound-style HUD drawn with IMGUI (no Canvas setup needed).
/// Uses your art when assigned: the bottom control panel, the power bar outline and the
/// turn-timer digit images; falls back to simple shapes otherwise.
/// Shows wind, turn order with delays, HP tags, angle, power (+ last-shot marker),
/// move bar, weapon slots, minimap, turn timer, damage popups and off-screen shell markers.
/// </summary>
[DisallowMultipleComponent]
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    [Header("Art (optional)")]
    [Tooltip("Bottom control panel (image-removebg-preview.png).")]
    public Texture2D panelTexture;
    [Tooltip("Power bar background (Outline Power Bar.png).")]
    public Texture2D powerBarTexture;
    [Tooltip("Turn timer digits: element 0 = '01', element 19 = '20' (turn_timer_1..20).")]
    public Texture2D[] timerDigits;

    struct Popup
    {
        public Vector3 world;
        public string text;
        public Color color;
        public float age;
    }

    const float RefHeight = 720f;
    const float PopupLife = 1.3f;

    // Regions inside panelTexture (pixel coords of the 464x95 GunBound panel).
    static readonly Rect PanelLastBox = new Rect(28, 22, 72, 27);
    static readonly Rect PanelInfoBox = new Rect(112, 22, 216, 28);
    static readonly Rect PanelMapBox = new Rect(334, 22, 110, 26);
    static readonly Rect PanelPowerSlot = new Rect(80, 63, 356, 15);
    static readonly Rect PanelMoveSlot = new Rect(80, 84, 340, 5);

    readonly List<Popup> popups = new List<Popup>();
    readonly List<Mobile> order = new List<Mobile>();
    Camera cam;
    float scale;
    GUIStyle label, labelCenter, labelRight, big, small, smallCenter, hint, hintRight, title;
    bool stylesReady;

    static readonly Color PanelColor = new Color(0.05f, 0.08f, 0.14f, 0.82f);
    static readonly Color BarBack = new Color(0f, 0f, 0f, 0.6f);
    static readonly Color Muted = new Color(0.8f, 0.85f, 1f);

    void Awake()
    {
        Instance = this;
        cam = Camera.main;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void AddPopup(Vector3 world, string text, Color color)
    {
        // Stack numbers that land on the same spot at the same time.
        int stack = 0;
        foreach (var p in popups)
            if (p.age < 0.5f && Mathf.Abs(p.world.x - world.x) < 0.6f) stack++;
        world.y += stack * 0.32f;
        popups.Add(new Popup { world = world, text = text, color = color });
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            Popup p = popups[i];
            p.age += dt;
            if (p.age >= PopupLife) popups.RemoveAt(i);
            else popups[i] = p;
        }
    }

    void EnsureStyles()
    {
        if (stylesReady) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
        label.normal.textColor = Color.white;
        labelCenter = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        labelRight = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
        small = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Normal };
        smallCenter = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
        hint = new GUIStyle(smallCenter) { fontSize = 11 };
        hintRight = new GUIStyle(hint) { alignment = TextAnchor.MiddleRight };
        big = new GUIStyle(labelCenter) { fontSize = 30 };
        title = new GUIStyle(labelCenter) { fontSize = 44 };
        stylesReady = true;
    }

    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (!gm || gm.CurrentPhase == GameManager.Phase.Menu) return;
        if (!cam) cam = Camera.main;
        if (!cam) return;

        EnsureStyles();
        scale = Screen.height / RefHeight;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float w = Screen.width / scale;
        float h = RefHeight;

        DrawMobileTags(gm);
        DrawPopups();
        DrawOffscreenShells(w);
        DrawWind(gm, w);
        DrawTurnOrder(gm);
        DrawTimer(gm, w);
        if (panelTexture) DrawPanel(gm, w, h);
        else DrawBottomBar(gm, w, h);

        if (gm.CurrentPhase == GameManager.Phase.Starting)
            Shadow(new Rect(0, h * 0.35f, w, 60), "READY!", title, Color.white);

        if (gm.CurrentPhase == GameManager.Phase.GameOver)
            DrawGameOver(gm, w, h);

        GUI.matrix = Matrix4x4.identity;
        GUI.color = Color.white;
    }

    // ------------------------------------------------------------------ helpers

    Vector2 ToGui(Vector3 world, out bool inFront)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        inFront = s.z > 0f;
        return new Vector2(s.x / scale, (Screen.height - s.y) / scale);
    }

    static void Box(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, SpriteFactory.White);
        GUI.color = Color.white;
    }

    static void Bar(Rect r, float fill, Color c)
    {
        Box(r, BarBack);
        Box(new Rect(r.x + 1, r.y + 1, Mathf.Max(0f, (r.width - 2) * Mathf.Clamp01(fill)), r.height - 2), c);
    }

    static void Shadow(Rect r, string text, GUIStyle style, Color color)
    {
        GUI.color = new Color(0, 0, 0, 0.85f * color.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
        GUI.color = color;
        GUI.Label(r, text, style);
        GUI.color = Color.white;
    }

    static void DrawArrow(Vector2 center, float size, float screenAngleDeg, Color color)
    {
        // Rotate around the centre in the current (scaled) GUI space.
        Matrix4x4 m = GUI.matrix;
        GUI.matrix = m * Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, screenAngleDeg), Vector3.one)
                       * Matrix4x4.TRS(-center, Quaternion.identity, Vector3.one);
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), SpriteFactory.Arrow);
        GUI.color = Color.white;
        GUI.matrix = m;
    }

    static Color PowerColor(float p) =>
        Color.Lerp(new Color(1f, 0.85f, 0.25f), new Color(1f, 0.3f, 0.2f), p / 100f);

    // ------------------------------------------------------------------ world-space bits

    void DrawMobileTags(GameManager gm)
    {
        foreach (var m in gm.Mobiles)
        {
            if (!m.IsAlive) continue;
            Vector2 p = ToGui(m.Center + Vector2.up * (m.HitRadius + 0.35f), out bool vis);
            if (!vis) continue;

            Shadow(new Rect(p.x - 60, p.y - 22, 120, 20), m.displayName, labelCenter, m.TeamColor);
            float hp = m.Health / (float)m.maxHealth;
            Color hpc = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.9f, 0.35f), hp);
            Bar(new Rect(p.x - 30, p.y, 60, 7), hp, hpc);

            if (m == gm.Current && gm.CurrentPhase == GameManager.Phase.Aiming)
            {
                float bob = Mathf.Sin(Time.time * 6f) * 4f;
                DrawArrow(new Vector2(p.x, p.y - 36 + bob), 22, 90f, new Color(1f, 0.9f, 0.3f));
            }
        }
    }

    void DrawPopups()
    {
        foreach (var pop in popups)
        {
            float k = pop.age / PopupLife;
            Vector2 p = ToGui(pop.world, out bool vis);
            if (!vis) continue;
            Color c = pop.color;
            c.a = 1f - k * k;
            Shadow(new Rect(p.x - 60, p.y - 40 * k - 15, 120, 34), pop.text, big, c);
        }
    }

    void DrawOffscreenShells(float w)
    {
        foreach (var shell in Projectile.Active)
        {
            if (!shell) continue;
            Vector2 p = ToGui(shell.transform.position, out _);
            if (p.y > 0f) continue;
            float x = Mathf.Clamp(p.x, 20f, w - 20f);
            DrawArrow(new Vector2(x, 70f), 20, -90f, shell.Shell.trailColor);
            Shadow(new Rect(x - 30, 82, 60, 18), Mathf.RoundToInt(-p.y / 10f).ToString(), smallCenter, Color.white);
        }
    }

    // ------------------------------------------------------------------ top

    void DrawWind(GameManager gm, float w)
    {
        Vector2 c = new Vector2(w * 0.5f, 42f);
        GUI.color = PanelColor;
        GUI.DrawTexture(new Rect(c.x - 34, c.y - 34, 68, 68), SpriteFactory.Circle.texture);
        GUI.color = Color.white;
        if (gm.Wind > 0)
        {
            float intensity = gm.Wind / (float)Mathf.Max(1, gm.maxWind);
            Color wc = Color.Lerp(new Color(0.6f, 0.95f, 1f), new Color(1f, 0.35f, 0.3f), intensity);
            DrawArrow(c, 30f + 20f * intensity, -gm.WindAngle, wc); // GUI Y is down, so negate the angle
        }
        Shadow(new Rect(c.x - 40, c.y + 30, 80, 22), "WIND " + gm.Wind, labelCenter, Color.white);
    }

    void DrawTurnOrder(GameManager gm)
    {
        int n = gm.Mobiles.Count;
        Box(new Rect(10, 10, 210, 30 + n * 26), PanelColor);
        Shadow(new Rect(20, 14, 200, 20), "TURN " + gm.TurnNumber + "      DELAY", small, Muted);

        order.Clear();
        order.AddRange(gm.Mobiles);
        order.Sort((a, b) =>
        {
            if (a.IsAlive != b.IsAlive) return a.IsAlive ? -1 : 1;
            return a.Delay.CompareTo(b.Delay);
        });

        for (int i = 0; i < order.Count; i++)
        {
            var m = order[i];
            float y = 36 + i * 26;
            Color c = m.IsAlive ? m.TeamColor : new Color(0.5f, 0.5f, 0.5f);
            string prefix = m == gm.Current && gm.CurrentPhase == GameManager.Phase.Aiming ? "> " : "   ";
            Shadow(new Rect(18, y, 120, 22), prefix + m.displayName, label, c);
            Shadow(new Rect(110, y, 50, 22), m.IsAlive ? Mathf.RoundToInt(m.Delay).ToString() : "--", labelRight, Color.white);
            Bar(new Rect(165, y + 7, 45, 8), m.Health / (float)m.maxHealth, c);
        }
    }

    void DrawTimer(GameManager gm, float w)
    {
        if (gm.CurrentPhase != GameManager.Phase.Aiming) return;
        int secs = Mathf.Clamp(Mathf.CeilToInt(gm.TurnTimeLeft), 0, 99);
        Rect r = new Rect(w - 135, 10, 125, 100);

        if (timerDigits != null && secs >= 1 && secs <= timerDigits.Length && timerDigits[secs - 1])
        {
            GUI.DrawTexture(r, timerDigits[secs - 1], ScaleMode.ScaleToFit);
        }
        else
        {
            Color tc = secs <= 5 ? new Color(1f, 0.35f, 0.3f) : Color.white;
            Shadow(r, secs.ToString("00"), title, tc);
        }
    }

    // ------------------------------------------------------------------ bottom (with panel art)

    void DrawPanel(GameManager gm, float w, float h)
    {
        var m = gm.Current;
        if (!m) return;

        float pw = Mathf.Min(w - 20f, 780f);
        float k = pw / panelTexture.width;
        float ph = panelTexture.height * k;
        Rect panel = new Rect((w - pw) * 0.5f, h - ph - 6f, pw, ph);
        Rect R(Rect px) => new Rect(panel.x + px.x * k, panel.y + px.y * k, px.width * k, px.height * k);

        GUI.DrawTexture(panel, panelTexture);

        // LAST box: last shot's angle / power.
        Rect last = R(PanelLastBox);
        string lastText = m.LastPower >= 0f ? Mathf.RoundToInt(m.LastAngle) + "° / " + Mathf.RoundToInt(m.LastPower) : "--";
        Shadow(new Rect(last.x, last.y + last.height * 0.35f, last.width, last.height * 0.65f), lastText, smallCenter, Muted);

        // Info box: name, HP, weapon, angle.
        Rect info = R(PanelInfoBox);
        Shadow(new Rect(info.x + 8, info.y + 2, info.width * 0.6f, info.height * 0.5f), m.displayName, label, m.TeamColor);
        Shadow(new Rect(info.x + 8, info.y + info.height * 0.5f, info.width * 0.6f, info.height * 0.5f),
            m.CurrentWeapon.name + "   HP " + m.Health, small, Color.white);
        Shadow(new Rect(info.xMax - 90, info.y, 84, info.height), Mathf.RoundToInt(m.WorldAngle) + "°", big, Color.white);

        // Minimap box.
        DrawMinimap(gm, R(PanelMapBox));

        // Power slot.
        Rect slot = R(PanelPowerSlot);
        if (powerBarTexture) GUI.DrawTexture(slot, powerBarTexture);
        Box(new Rect(slot.x, slot.y, slot.width * gm.Power / 100f, slot.height), PowerColor(gm.Power) * new Color(1, 1, 1, 0.9f));
        if (m.LastPower >= 0f)
        {
            float lx = slot.x + slot.width * (m.LastPower / 100f);
            Box(new Rect(lx - 1.5f, slot.y - 3, 3, slot.height + 6), new Color(0.3f, 1f, 0.9f));
        }
        Shadow(new Rect(slot.xMax - 50, slot.y - 1, 46, slot.height + 2), Mathf.RoundToInt(gm.Power).ToString(), labelRight, Color.white);

        // Move slot.
        Rect move = R(PanelMoveSlot);
        Box(new Rect(move.x, move.y, move.width * (m.Fuel / m.maxFuel), move.height), new Color(0.35f, 0.9f, 0.45f));

        // Weapon buttons above the panel.
        DrawWeaponSlots(gm, m, new Vector2(panel.x + 8f, panel.y - 30f));
        Shadow(new Rect(panel.x, panel.y - 30, panel.width - 8, 24),
            "Left/Right move   Up/Down angle   Space power   P pass   Tab map", hintRight,
            new Color(1, 1, 1, 0.7f));
    }

    void DrawWeaponSlots(GameManager gm, Mobile m, Vector2 origin)
    {
        for (int i = 0; i < m.weapons.Length && i < 3; i++)
        {
            Rect r = new Rect(origin.x + i * 84, origin.y, 80, 24);
            bool usable = gm.CanUseWeapon(m, i);
            bool selected = m.SelectedWeapon == i;
            Box(r, selected ? new Color(1f, 0.8f, 0.25f, 0.95f) : new Color(0f, 0f, 0f, usable ? 0.6f : 0.35f));
            string text = (i + 1) + "  " + m.weapons[i].name;
            if (selected)
            {
                GUI.color = new Color(0.1f, 0.06f, 0f);
                GUI.Label(r, text, smallCenter);
                GUI.color = Color.white;
            }
            else Shadow(r, text, smallCenter, usable ? Color.white : new Color(1, 1, 1, 0.3f));
        }
    }

    void DrawMinimap(GameManager gm, Rect r)
    {
        var t = gm.Terrain;
        if (!t) return;
        Box(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), new Color(0.9f, 0.92f, 0.95f));
        Bounds b = t.WorldBounds;
        float top = b.max.y + 3f, bottom = b.min.y;

        // Camera viewport (red frame, like GunBound).
        if (cam && cam.orthographic)
        {
            float hh = cam.orthographicSize, hw = hh * cam.aspect;
            Vector3 c = cam.transform.position;
            float x0 = Mathf.Clamp01(Mathf.InverseLerp(b.min.x, b.max.x, c.x - hw));
            float x1 = Mathf.Clamp01(Mathf.InverseLerp(b.min.x, b.max.x, c.x + hw));
            float y0 = Mathf.Clamp01(Mathf.InverseLerp(top, bottom, c.y + hh));
            float y1 = Mathf.Clamp01(Mathf.InverseLerp(top, bottom, c.y - hh));
            Rect v = new Rect(r.x + x0 * r.width, r.y + y0 * r.height, (x1 - x0) * r.width, Mathf.Max(4f, (y1 - y0) * r.height));
            Color red = new Color(0.85f, 0.15f, 0.2f);
            Box(new Rect(v.x, v.y, v.width, 1.5f), red);
            Box(new Rect(v.x, v.yMax - 1.5f, v.width, 1.5f), red);
            Box(new Rect(v.x, v.y, 1.5f, v.height), red);
            Box(new Rect(v.xMax - 1.5f, v.y, 1.5f, v.height), red);
        }
        foreach (var m in gm.Mobiles)
        {
            if (!m.IsAlive) continue;
            float nx = Mathf.InverseLerp(b.min.x, b.max.x, m.Center.x);
            float ny = Mathf.InverseLerp(top, bottom, m.Center.y);
            float s = m == gm.Current ? 9f : 7f;
            Box(new Rect(r.x + nx * r.width - s * 0.5f, r.y + ny * r.height - s * 0.5f, s, s), m.TeamColor);
        }
        foreach (var p in Projectile.Active)
        {
            if (!p) continue;
            float nx = Mathf.InverseLerp(b.min.x, b.max.x, p.transform.position.x);
            float ny = Mathf.InverseLerp(top, bottom, p.transform.position.y);
            Box(new Rect(r.x + nx * r.width - 2, r.y + ny * r.height - 2, 4, 4), Color.white);
        }
    }

    // ------------------------------------------------------------------ bottom (fallback)

    void DrawBottomBar(GameManager gm, float w, float h)
    {
        var m = gm.Current;
        if (!m) return;

        float barH = 112f;
        Rect panel = new Rect(0, h - barH, w, barH);
        Box(panel, PanelColor);
        Box(new Rect(0, h - barH, w, 2), m.TeamColor);

        Shadow(new Rect(16, panel.y + 8, 200, 22), m.displayName, label, m.TeamColor);
        Shadow(new Rect(16, panel.y + 30, 200, 20), "HP " + m.Health + " / " + m.maxHealth, small, Color.white);
        Shadow(new Rect(16, panel.y + 52, 60, 20), "ANGLE", small, Muted);
        Shadow(new Rect(10, panel.y + 66, 90, 40), Mathf.RoundToInt(m.WorldAngle) + "°", big, Color.white);

        float bw = Mathf.Min(520f, w - 420f);
        float bx = (w - bw) * 0.5f;
        Rect power = new Rect(bx, panel.y + 30, bw, 26);
        Shadow(new Rect(bx, panel.y + 8, 200, 20), "POWER", small, Muted);
        if (powerBarTexture) GUI.DrawTexture(power, powerBarTexture);
        else Box(power, BarBack);
        Box(new Rect(power.x, power.y, power.width * gm.Power / 100f, power.height), PowerColor(gm.Power) * new Color(1, 1, 1, 0.9f));
        if (m.LastPower >= 0f)
        {
            float lx = power.x + power.width * (m.LastPower / 100f);
            Box(new Rect(lx - 1, power.y - 4, 3, power.height + 8), new Color(0.3f, 1f, 0.9f));
        }

        Rect move = new Rect(bx, panel.y + 72, bw, 12);
        Shadow(new Rect(bx, panel.y + 56, 200, 18), "MOVE", small, Muted);
        Bar(move, m.Fuel / m.maxFuel, new Color(0.35f, 0.85f, 0.45f));

        Shadow(new Rect(bx, panel.y + 88, bw, 20),
            "Left/Right move   Up/Down angle   Space power   1/2/3 weapon   P pass   Tab map   Ctrl+R restart", hint, new Color(1, 1, 1, 0.6f));

        DrawWeaponSlots(gm, m, new Vector2(w - 262f, panel.y + 20f));
    }

    void DrawGameOver(GameManager gm, float w, float h)
    {
        Box(new Rect(0, 0, w, h), new Color(0, 0, 0, 0.45f));
        string text = gm.Winner ? gm.Winner.displayName + " WINS!" : "DRAW!";
        Color c = gm.Winner ? gm.Winner.TeamColor : Color.white;
        Shadow(new Rect(0, h * 0.38f, w, 60), text, title, c);
        Shadow(new Rect(0, h * 0.38f + 64, w, 30), "Press R to play again", labelCenter, Color.white);
    }
}
