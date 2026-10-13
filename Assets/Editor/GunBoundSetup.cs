using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click scene setup:  menu  GunBound ▸ Setup Scene.
/// Safe to run again at any time. It:
///  • removes the old prototype pieces (missing scripts, physics bodies, world-space bars, arrows, Counter, PowerBar, Logic Manager)
///  • makes the Adium stage destructible
///  • turns Player1/Player2 into Knight/Dragon mobiles with DragonBound-style weapons and all sprite-sheet animations
///  • creates the GameManager + HUD with your panel, power bar and timer art, and hooks up the main menu
///  • fixes texture import settings and saves the scene
/// </summary>
public static class GunBoundSetup
{
    static Dictionary<string, string> texturePaths;

    [MenuItem("GunBound/Setup Scene")]
    public static void Setup()
    {
        IndexTextures();
        Scene scene = SceneManager.GetActiveScene();
        var log = new List<string>();

        // ---------------------------------------------------------- 1. clean out the old prototype
        int missing = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                missing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        if (missing > 0) log.Add($"removed {missing} missing-script component(s)");

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "PowerBar" || root.name == "Counter" || root.name == "Logic Manager" || root.name == "LogicManager")
            {
                log.Add("removed old " + root.name);
                Undo.DestroyObjectImmediate(root);
            }
        }

        // ---------------------------------------------------------- 2. stage
        var terrain = Object.FindAnyObjectByType<DestructibleTerrain>();
        if (!terrain)
        {
            var stage = GameObject.Find("Stage-Adium");
            if (!stage) stage = GameObject.Find("Bg-Stage");
            if (stage) terrain = Undo.AddComponent<DestructibleTerrain>(stage);
        }
        if (terrain)
        {
            RemovePhysics(terrain.gameObject);
            var sr = terrain.GetComponent<SpriteRenderer>();
            Undo.RecordObject(sr, "Sorting");
            sr.sortingOrder = 0;
            if (sr.sprite) FixTexture(sr.sprite.texture, readable: true);
            log.Add("stage: " + terrain.name);
        }
        else Debug.LogWarning("[GunBound] No stage found. Add DestructibleTerrain to your map sprite.");

        foreach (var r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!r.name.ToLowerInvariant().StartsWith("background")) continue;
            Undo.RecordObject(r, "Sorting");
            r.sortingOrder = -10;
        }

        // ---------------------------------------------------------- 3. mobiles
        foreach (string playerName in new[] { "Player1", "Player2" })
        {
            var go = GameObject.Find(playerName);
            if (!go) continue;

            foreach (Transform child in go.transform.Cast<Transform>().ToArray())
            {
                string c = child.name;
                if (c == "healthBar" || c.EndsWith("Arrow") || c == "ShotPoint") Undo.DestroyObjectImmediate(child.gameObject);
            }
            RemovePhysics(go);

            var mobile = go.GetComponent<Mobile>();
            if (!mobile) mobile = Undo.AddComponent<Mobile>(go);
            var sr = go.GetComponent<SpriteRenderer>();
            Undo.RecordObject(mobile, "Mobile Setup");
            Undo.RecordObject(sr, "Sorting");
            Undo.RecordObject(go.transform, "Scale");
            sr.sortingOrder = 5;
            go.transform.localScale = new Vector3(1.3f, 1.3f, 1f); // a bit larger than DragonBound's 1:1 so they read well

            string tex = sr.sprite ? sr.sprite.texture.name.ToLowerInvariant() : "";
            bool dragon = tex.Contains("dragon") || (!tex.Contains("knight") && playerName == "Player2");
            if (dragon) SetupDragon(mobile);
            else SetupKnight(mobile);

            if (mobile.idleFrames != null && mobile.idleFrames.Length > 0) sr.sprite = mobile.idleFrames[0];
            EditorUtility.SetDirty(mobile);
            log.Add($"{playerName} -> {mobile.displayName}");
        }

        // ---------------------------------------------------------- 4. manager + HUD + menu
        var gm = Object.FindAnyObjectByType<GameManager>();
        if (!gm)
        {
            var go = new GameObject("GameManager");
            Undo.RegisterCreatedObjectUndo(go, "Create GameManager");
            gm = go.AddComponent<GameManager>();
        }
        var hud = gm.GetComponent<GameHUD>();
        if (!hud) hud = Undo.AddComponent<GameHUD>(gm.gameObject);
        Undo.RecordObject(hud, "HUD Art");
        hud.panelTexture = Tex("image-removebg-preview", hudArt: true);
        hud.powerBarTexture = Tex("Outline Power Bar", hudArt: true);
        hud.timerDigits = Enumerable.Range(1, 20).Select(i => Tex("turn_timer_" + i, hudArt: true)).ToArray();
        EditorUtility.SetDirty(hud);

        Undo.RecordObject(gm, "Menu");
        var menu = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "MainMenuScreen");
        if (menu)
        {
            gm.mainMenu = menu;
            foreach (var b in menu.GetComponentsInChildren<Button>(true))
            {
                string n = b.name.ToLowerInvariant();
                if (n.Contains("start")) gm.startButton = b;
                if (n.Contains("exit")) gm.exitButton = b;
            }
            log.Add("main menu hooked up");
        }
        EditorUtility.SetDirty(gm);

        foreach (var a in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!a.name.ToLowerInvariant().Contains("soundtrack")) continue;
            Undo.RecordObject(a, "Loop");
            a.loop = true;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[GunBound] Setup complete: " + string.Join(", ", log) + ". Press Play!");
    }

    // ================================================================== mobiles

    static void SetupKnight(Mobile m)
    {
        Sprite[] sheet = Sheet("Knight2D", bottomPivot: true);
        m.displayName = "Knight";
        m.artFacesRight = false;
        m.baseDelay = 550;
        m.idleFrames = Range(sheet, 0, 37);
        m.walkFrames = Range(sheet, 39, 74);
        m.attackFrames = Range(sheet, 110, 135);
        m.hitFrames = Range(sheet, 234, 263);
        m.defeatFrames = Range(sheet, 279, 293);
        m.animFps = 13f;

        Sprite[] orb = Sheet("Knight-Shot").Take(1).ToArray();
        Sprite[] spark = Sheet("knight12");
        Sprite[] bigSword = Sheet("knightIon 1");
        Sprite[] smallSword = Sheet("knightIon");
        Sprite[] ssOrb = Sheet("knightSS");
        Sprite[] boom = Sheet("knight 1");

        ShellDef Sword(int dmg, Sprite[] art, float size) => new ShellDef
        {
            damage = dmg, crater = new Vector2(0.38f, 0.41f), blastRadius = 0.5f,
            gravityScale = 0.6f, affectedByWind = false, hitSize = 0.14f,
            frames = art, fps = 20f, spriteSize = size, alignToVelocity = true,
            trailColor = new Color(1f, 0.55f, 0.6f),
            explosionFrames = boom, explosionFps = 30f, explosionSize = 1.1f
        };

        ShellDef Trigger(Sprite[] art, float size, Color trail) => new ShellDef
        {
            damage = 0, crater = Vector2.zero, blastRadius = 0.3f, hitSize = 0.16f,
            frames = art, fps = 18f, spriteSize = size, trailColor = trail,
            explosionFrames = spark, explosionFps = 30f, explosionSize = 0.55f
        };

        m.weapons = new[]
        {
            // DragonBound: orb lands, then one sword (250) drops on the spot.
            new WeaponDef
            {
                name = "Shot 1", delay = 250,
                shell = Trigger(orb, 0.42f, new Color(0.5f, 0.6f, 1f)),
                useStrike = true,
                strike = new StrikeDef { delay = 0.4f, height = 4.5f, fallSpeed = 9f, xOffsets = new[] { 0f }, shell = Sword(250, bigSword, 0.75f) }
            },
            // Three swords.
            new WeaponDef
            {
                name = "Shot 2", delay = 400,
                shell = Trigger(spark, 0.45f, new Color(1f, 0.5f, 0.55f)),
                useStrike = true,
                strike = new StrikeDef { delay = 0.3f, height = 4.5f, fallSpeed = 9f, interval = 0.08f, xOffsets = new[] { -0.25f, 0f, 0.25f }, shell = Sword(150, smallSword, 0.5f) }
            },
            // SS: six swords.
            new WeaponDef
            {
                name = "SS", delay = 840, oncePerMatch = true,
                shell = Trigger(ssOrb, 0.5f, new Color(1f, 0.6f, 0.3f)),
                useStrike = true,
                strike = new StrikeDef { delay = 0.2f, height = 4.5f, fallSpeed = 10f, interval = 0.1f, xOffsets = new[] { 0f, -0.2f, 0.2f, -0.4f, 0.4f, 0f }, shell = Sword(95, bigSword, 0.7f) }
            }
        };
    }

    static void SetupDragon(Mobile m)
    {
        Sheet("Dragon2D", bottomPivot: true); // fix pivots before reading the clips
        m.displayName = "Dragon";
        m.artFacesRight = false;
        m.baseDelay = 550;
        m.idleFrames = ClipSprites("idle");
        m.walkFrames = ClipSprites("movement");
        m.attackFrames = ClipSprites("attack");
        m.hitFrames = ClipSprites("hit");
        m.defeatFrames = ClipSprites("defeat");
        m.animFps = 13f;

        Sprite[] fireball = Sheet("Dragon-Shot").Take(1).ToArray();
        Sprite[] ssOrb = Sheet("dragon2_ss");
        Sprite[] fire = Sheet("dragonf");

        ShellDef Fireball(int dmg, float size) => new ShellDef
        {
            damage = dmg, crater = new Vector2(0.40f, 0.30f), blastRadius = 0.45f, hitSize = 0.15f,
            frames = fireball, spriteSize = size, alignToVelocity = true, trailColor = new Color(1f, 0.6f, 0.2f),
            explosionFrames = fire, explosionFps = 24f, explosionSize = 0.9f
        };

        m.weapons = new[]
        {
            // DragonBound: four fireballs, staggered 0.1 s, fanning out.
            new WeaponDef
            {
                name = "Shot 1", delay = 250,
                shell = Fireball(80, 0.4f),
                volley = new[]
                {
                    new VolleyShot(0f, 0f, 0f), new VolleyShot(0.1f, 4f, 2.5f),
                    new VolleyShot(0.2f, -2f, -2.5f), new VolleyShot(0.3f, 6f, 5f)
                }
            },
            // Six fireballs in two waves.
            new WeaponDef
            {
                name = "Shot 2", delay = 400,
                shell = Fireball(90, 0.35f),
                volley = new[]
                {
                    new VolleyShot(0f, 0f, 0f), new VolleyShot(0f, 5f, 4f), new VolleyShot(0f, 10f, 8f),
                    new VolleyShot(0.1f, 0f, 0f), new VolleyShot(0.1f, 5f, 4f), new VolleyShot(0.1f, 10f, 8f)
                }
            },
            // SS: an orb that calls down six more from the sky.
            new WeaponDef
            {
                name = "SS", delay = 800, oncePerMatch = true,
                shell = new ShellDef
                {
                    damage = 0, crater = Vector2.zero, blastRadius = 0.3f, hitSize = 0.2f,
                    frames = ssOrb, fps = 12f, spriteSize = 0.5f, trailColor = new Color(1f, 0.5f, 0.7f),
                    explosionFrames = fire, explosionSize = 0.6f
                },
                useStrike = true,
                strike = new StrikeDef
                {
                    delay = 0.1f, height = 4.4f, fallSpeed = 8f, interval = 0.06f,
                    xOffsets = new[] { -1f, 1f, 0.6f, -0.6f, 0.2f, -0.2f },
                    shell = new ShellDef
                    {
                        damage = 75, crater = new Vector2(0.37f, 0.27f), blastRadius = 0.45f,
                        gravityScale = 0.7f, affectedByWind = false, hitSize = 0.15f,
                        frames = ssOrb, fps = 12f, spriteSize = 0.38f, trailColor = new Color(1f, 0.5f, 0.7f),
                        explosionFrames = fire, explosionSize = 0.8f
                    }
                }
            }
        };
    }

    // ================================================================== asset helpers

    static void IndexTextures()
    {
        texturePaths = new Dictionary<string, string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (!texturePaths.ContainsKey(key) || path.Contains("/Characters/") || path.Contains("/Sprites/"))
                texturePaths[key] = path;
        }
    }

    static string PathOf(string textureName)
    {
        texturePaths.TryGetValue(textureName.ToLowerInvariant(), out string p);
        if (p == null) Debug.LogWarning("[GunBound] Texture not found: " + textureName);
        return p;
    }

    static Texture2D Tex(string textureName, bool hudArt = false)
    {
        string path = PathOf(textureName);
        if (path == null) return null;
        if (hudArt)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp && (imp.textureCompression != TextureImporterCompression.Uncompressed || imp.mipmapEnabled))
            {
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>All sprites of a sliced sheet, ordered by their trailing frame number.</summary>
    static Sprite[] Sheet(string textureName, bool bottomPivot = false)
    {
        string path = PathOf(textureName);
        if (path == null) return new Sprite[0];
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp)
        {
            bool changed = false;
            if (imp.textureCompression != TextureImporterCompression.Uncompressed)
            {
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }
            if (changed) imp.SaveAndReimport();
            if (bottomPivot) SetBottomPivots(imp);
        }
        return AssetDatabase.LoadAllAssetRepresentationsAtPath(path)
            .OfType<Sprite>()
            .OrderBy(s => FrameNumber(s.name))
            .ToArray();
    }

    /// <summary>Feet-anchored pivots so frames of different heights don't bob up and down.</summary>
    static void SetBottomPivots(TextureImporter imp)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(imp);
        if (dp == null) return;
        dp.InitSpriteEditorDataProvider();
        var rects = dp.GetSpriteRects();
        bool changed = false;
        foreach (var r in rects)
        {
            if (r.alignment == SpriteAlignment.BottomCenter) continue;
            r.alignment = SpriteAlignment.BottomCenter;
            r.pivot = new Vector2(0.5f, 0f);
            changed = true;
        }
        if (!changed) return;
        dp.SetSpriteRects(rects);
        dp.Apply();
        imp.SaveAndReimport();
    }

    static Sprite[] Range(Sprite[] sheet, int from, int to)
    {
        return sheet.Where(s => { int n = FrameNumber(s.name); return n >= from && n <= to; }).ToArray();
    }

    /// <summary>Sprites keyed in one of your animation clips (Assets/Characters/&lt;name&gt;.anim).</summary>
    static Sprite[] ClipSprites(string clipName)
    {
        foreach (string guid in AssetDatabase.FindAssets(clipName + " t:AnimationClip", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileNameWithoutExtension(path).Equals(clipName, System.StringComparison.OrdinalIgnoreCase)) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip) continue;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.propertyName != "m_Sprite") continue;
                var frames = AnimationUtility.GetObjectReferenceCurve(clip, binding)
                    .Select(k => k.value as Sprite).Where(s => s).ToArray();
                if (frames.Length > 0) return frames;
            }
        }
        Debug.LogWarning("[GunBound] Animation clip not found or empty: " + clipName);
        return new Sprite[0];
    }

    static void RemovePhysics(GameObject go)
    {
        foreach (var c in go.GetComponents<Collider2D>()) Undo.DestroyObjectImmediate(c);
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb) Undo.DestroyObjectImmediate(rb);
    }

    static void FixTexture(Texture tex, bool readable)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) return;
        bool changed = false;
        if (readable && !imp.isReadable) { imp.isReadable = true; changed = true; }
        if (imp.textureCompression != TextureImporterCompression.Uncompressed)
        {
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }
        if (imp.mipmapEnabled) { imp.mipmapEnabled = false; changed = true; }
        if (changed) imp.SaveAndReimport();
    }

    static int FrameNumber(string spriteName)
    {
        var match = Regex.Match(spriteName, @"(\d+)$");
        return match.Success ? int.Parse(match.Value) : 0;
    }
}
