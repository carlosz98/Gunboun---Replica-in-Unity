#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Editor-only test bot (safe to delete). Active only when &lt;project&gt;/Automation/autopilot.request exists
/// at Play: starts the match from the menu, plays turns for both sides (walking, aiming with a trajectory
/// solver, cycling Shot 1 / Shot 2 / SS), writes a turn log and screenshots to Automation/, then stops Play mode.
/// </summary>
public class AutoPilot : MonoBehaviour
{
    static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Automation"));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string req = Path.Combine(Dir, "autopilot.request");
        if (!File.Exists(req)) return;
        try { File.Delete(req); } catch { }
        new GameObject("AutoPilot").AddComponent<AutoPilot>();
    }

    readonly StringBuilder log = new StringBuilder();
    int shots;

    void Line(string s)
    {
        log.AppendLine($"[{Time.time:0.0}s] {s}");
        try { File.WriteAllText(Path.Combine(Dir, "autopilot.txt"), log.ToString()); } catch { }
    }

    void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name + ".png"));
        shots++;
    }

    IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        yield return new WaitForSeconds(1.0f);
        var gm = GameManager.Instance;
        if (!gm) { Line("NO GAMEMANAGER"); yield break; }

        Line($"phase={gm.CurrentPhase} mobiles={gm.Mobiles.Count} terrain={(gm.Terrain ? gm.Terrain.name : "none")}");
        foreach (var m in gm.Mobiles)
            Line($"  {m.displayName}: pos={m.transform.position} hp={m.Health} weapons={m.weapons.Length} idle={(m.idleFrames == null ? 0 : m.idleFrames.Length)} walk={(m.walkFrames == null ? 0 : m.walkFrames.Length)} grounded={m.IsGrounded}");

        if (gm.CurrentPhase == GameManager.Phase.Menu)
        {
            Shot("00_menu");
            yield return new WaitForSeconds(0.6f);
            gm.BeginMatch();
            Line("clicked start");
        }

        int turn = 0;
        while (gm && gm.CurrentPhase != GameManager.Phase.GameOver && turn < 14)
        {
            while (gm.CurrentPhase != GameManager.Phase.Aiming && gm.CurrentPhase != GameManager.Phase.GameOver)
                yield return null;
            if (gm.CurrentPhase == GameManager.Phase.GameOver) break;

            turn++;
            var me = gm.Current;
            Mobile foe = null;
            foreach (var m in gm.Mobiles) if (m != me && m.IsAlive) foe = m;
            if (!foe) break;

            yield return new WaitForSeconds(0.5f);
            if (turn <= 2) Shot($"t{turn:00}_a_aim");

            // Walk a bit toward the enemy on the first two turns to exercise movement.
            int dir = foe.Center.x > me.Center.x ? 1 : -1;
            float x0 = me.Center.x, fuel0 = me.Fuel;
            float walk = turn <= 2 ? 0.6f : 0.15f;
            for (float t = 0; t < walk; t += Time.deltaTime)
            {
                me.Move(dir, Time.deltaTime);
                yield return null;
            }
            me.SetFacing(dir);
            Line($"T{turn} {me.displayName} walked {me.Center.x - x0:0.00}u fuel {fuel0:0}->{me.Fuel:0} tilt={me.Tilt:0}");

            int weapon = (turn - 1) % 3;
            if (!gm.CanUseWeapon(me, weapon)) weapon = 0;
            WeaponDef w = me.weapons[weapon];

            // Aim: fixed angle, solve power so the shell lands on the enemy.
            me.AddAngle(55f - me.Angle);
            float power = SolvePower(gm, me, w, foe.Center);
            int hpBefore = foe.Health, myHp = me.Health;
            Line($"T{turn} {me.displayName} fires {w.name} angle={me.WorldAngle:0} power={power:0} wind={gm.Wind}@{gm.WindAngle:0} delay={me.Delay:0}");
            gm.DebugFire(weapon, power);

            yield return new WaitForSeconds(0.45f);
            if (turn <= 3) Shot($"t{turn:00}_b_flight");

            while (gm.CurrentPhase == GameManager.Phase.Flying) yield return null;
            yield return new WaitForSeconds(0.15f);
            if (turn <= 3) Shot($"t{turn:00}_c_impact");

            while (gm.CurrentPhase == GameManager.Phase.Resolving) yield return null;
            Line($"T{turn} result: {foe.displayName} hp {hpBefore}->{foe.Health}, {me.displayName} hp {myHp}->{me.Health}, newDelay={me.Delay:0}, foeDelay={foe.Delay:0}");
        }

        yield return new WaitForSeconds(0.8f);
        Shot("zz_end");
        Line($"END phase={gm.CurrentPhase} winner={(gm.Winner ? gm.Winner.displayName : "none")} turns={turn} screenshots={shots}");
        yield return new WaitForSeconds(1.0f);
        try { File.WriteAllText(Path.Combine(Dir, "stop.request"), ""); } catch { }
    }

    /// <summary>Brute-force the power whose trajectory passes closest to the target.</summary>
    static float SolvePower(GameManager gm, Mobile me, WeaponDef w, Vector2 target)
    {
        float best = 60f, bestErr = float.MaxValue;
        var terrain = gm.Terrain;
        for (float p = 5f; p <= 100f; p += 0.5f)
        {
            Vector2 pos = me.MuzzlePosition;
            Vector2 vel = me.AimDirection * (p / 100f * me.maxShotSpeed);
            Vector2 acc = gm.Gravity * w.shell.gravityScale + (w.shell.affectedByWind ? gm.WindAcceleration : Vector2.zero);
            float err = float.MaxValue;
            for (int i = 0; i < 2400; i++)
            {
                vel += acc * (1f / 240f);
                pos += vel * (1f / 240f);
                err = Mathf.Min(err, (pos - target).magnitude);
                if (vel.y < 0 && pos.y < target.y - 0.4f) break;
                if (i > 30 && terrain && terrain.IsSolid(pos) && (pos - target).magnitude > 0.6f) { err += 2f; break; }
            }
            if (err < bestErr) { bestErr = err; best = p; }
        }
        return best;
    }
}
#endif
