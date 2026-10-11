using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HierarchicalStateMachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Tools > Lucy > Build Kitsune Boss. Turns the sheets in Assets/Sprites/Enemy/Kitsune into the Level2 boss, the
/// same way MedusaBossBuilder made the Level1 one: sprite import (grid, pixels per unit, per-sheet pivots measured
/// from the art), clips, the animator controller, the two animated fire projectiles (Fire_1, Fire_2), the attack
/// data, the adaptation to the gold-gear upgrades, the Kitsune prefab, and the swap into the Level2 arena
/// (BossGate rewired). Level2's boss data (Sentinela.asset) keeps its bossId and reward; only its face and its
/// adaptation change.
///
/// Safe to run again. Generated assets (clips, controllers, sprite import, prefab structure) are updated in place
/// so their GUIDs and file IDs survive. Tuning assets (stats, the three attacks, the attack set, the adaptation,
/// the entity) are only created when missing, so balancing done in the Inspector is never overwritten: delete an
/// asset to get its defaults back. The scene swap is skipped once the gate already points at a Kitsune.
/// </summary>
public static class KitsuneBossBuilder
{
    private const string SpriteFolder = "Assets/Sprites/Enemy/Kitsune";
    private const string AnimationFolder = "Assets/Animations/Enemy/Kitsune";
    private const string DataFolder = "Assets/ObjectData/Bosses/Kitsune";
    private const string AttackFolder = DataFolder + "/Attacks";
    private const string ControllerPath = AnimationFolder + "/Kitsune.controller";
    private const string StatsPath = DataFolder + "/Kitsune_Stats.asset";
    private const string AttackSetPath = DataFolder + "/Kitsune_AttackSet.asset";
    private const string ScalingPath = DataFolder + "/Kitsune_UpgradeScaling.asset";
    private const string EntityPath = "Assets/ObjectData/Entity/Kitsune.asset";
    private const string BossDataPath = "Assets/ObjectData/Bosses/Sentinela.asset";
    private const string PlaceholderEntityName = "Sentinela";
    private const string PrefabPath = "Assets/Renan/Prefabs/Kitsune.prefab";
    private const string BaseProjectilePath = "Assets/Renan/Prefabs/EnemyProjectile.prefab";
    private const string SpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
    private const string Level2ScenePath = "Assets/Scenes/Level2.unity";

    // Level2 answers the second row of the vendor's trees: the upgrades paid in Engrenagem de Ouro.
    private const string GoldGearItemId = "engrenagem_ouro";

    private const int FrameSize = 128;
    // 25 px per unit, like Medusa: Kitsune's ~78 px standing height becomes ~3.1 units, about 1.4x the player.
    private const float PixelsPerUnit = 25f;
    private const string PortraitSpriteName = "Kitsune_Portrait";

    // Rows (from the bottom of a frame) of the skirt. It is the most stable anchor between sheets, whose frames
    // are not aligned: the bodice above carries the sleeves, which reach forward on every casting frame, and the
    // tails swing on every frame.
    private const int SkirtRowMin = 0;
    private const int SkirtRowMax = 20;

    private const int FireFrameSize = 64;
    // A little bigger than the body's scale so the shots read clearly at a distance: ~0.9 units long.
    private const float FirePixelsPerUnit = 20f;

    // Manual pivot fixes, in pixels from the left of a frame, if a sheet ever needs one.
    private static readonly Dictionary<string, float> PivotOverridePx = new();

    private class Sheet
    {
        public string file;
        public string clipName;
        public string stateName;
        public float fps;
        public bool loop;
        public bool pivotFromAllFrames; // loops: average anchor over every frame

        // Filled while building.
        public int frames;
        public float pivotPx;
        public Pixels pixels;
        public Sprite[] sprites;
        public AnimationClip clip;

        public string AssetPath => $"{SpriteFolder}/{file}.png";
    }

    // Reading of every sheet: see the report. State names are the ones the enemy states ask for.
    private static readonly Sheet[] Sheets =
    {
        new() { file = "Idle", clipName = "Kitsune_Idle", stateName = "Enemy_Idle", fps = 8f, loop = true, pivotFromAllFrames = true },
        new() { file = "Run", clipName = "Kitsune_Run", stateName = "Enemy_Running", fps = 12f, loop = true, pivotFromAllFrames = true },
        new() { file = "Hurt", clipName = "Kitsune_Hurt", stateName = "Enemy_Hit", fps = 8f },
        new() { file = "Dead", clipName = "Kitsune_Dead", stateName = "Enemy_Death", fps = 8f },
        new() { file = "Attack_1", clipName = "Kitsune_FoxfireWave", stateName = "Kitsune_FoxfireWave", fps = 12f },
        new() { file = "Attack_3", clipName = "Kitsune_Fireball", stateName = "Kitsune_Fireball", fps = 12f },
        new() { file = "Attack_2", clipName = "Kitsune_FoxfireBarrage", stateName = "Kitsune_FoxfireBarrage", fps = 10f }
    };

    // An earlier version gave her Medusa's petrifying gaze (on the Idle_2 sheet). The Kitsune has no such attack:
    // a rebuild removes it from the attack set and deletes what it generated.
    private const string RetiredGazeName = "Kitsune_CharmGaze";

    private static Sheet GetSheet(string file) => Sheets.First(s => s.file == file);

    /// <summary>A projectile sheet: the first frames fly in a loop, the rest are the impact.</summary>
    private class FireSheet
    {
        public string file;
        public string prefabName;
        public int flyFrames;
        public float flyFps;
        public float impactFps;
        public float colliderRadius;

        // Filled while building.
        public int frames;
        public Sprite[] sprites;
        public AnimationClip flyClip;
        public AnimationClip impactClip;
        public AnimatorController controller;

        public string AssetPath => $"{SpriteFolder}/{file}.png";
        public string PrefabPath => $"Assets/Renan/Prefabs/{prefabName}.prefab";
        public string ControllerPath => $"{AnimationFolder}/{prefabName}.controller";
        public float ImpactDuration => (frames - flyFrames) / impactFps;
    }

    // Fire_1: a thin, fast bolt (f0-7) that bursts into a crescent (f8-13). Fire_2: a fox-fire wisp with a curling
    // tail (f0-4) that blooms into a big flame (f5-10).
    private static readonly FireSheet[] FireSheets =
    {
        new() { file = "Fire_1", prefabName = "KitsuneFoxfire", flyFrames = 8, flyFps = 14f, impactFps = 14f, colliderRadius = 0.2f },
        new() { file = "Fire_2", prefabName = "KitsuneFireball", flyFrames = 5, flyFps = 10f, impactFps = 14f, colliderRadius = 0.28f }
    };

    private static FireSheet GetFireSheet(string file) => FireSheets.First(s => s.file == file);

    private static readonly List<string> Report = new();

    [MenuItem("Tools/Lucy/Build Kitsune Boss")]
    public static void Build()
    {
        // Level2 is opened below, so the open scenes have to be saved (or knowingly discarded) first.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Report.Clear();
        string previousScene = SceneManager.GetActiveScene().path;

        EnsureFolder(AnimationFolder);
        EnsureFolder(AttackFolder);

        foreach (Sheet sheet in Sheets) ImportSheet(sheet);
        foreach (FireSheet fire in FireSheets) ImportFireSheet(fire);
        foreach (Sheet sheet in Sheets) sheet.clip = BuildClip(sheet.clipName, sheet.sprites, 0, sheet.frames, sheet.fps, sheet.loop);
        foreach (FireSheet fire in FireSheets) BuildFireAnimation(fire);
        AnimatorController controller = BuildController();

        Sprite portrait = LoadSprite(GetSheet("Idle").AssetPath, PortraitSpriteName);
        EnemyScriptableObject stats = BuildStats();
        Entity_SO entity = BuildEntity(portrait);
        BossUpgradeScaling_SO scaling = BuildScaling();
        LinkBossData(entity, scaling);

        EnemyProjectile foxfire = BuildProjectilePrefab(GetFireSheet("Fire_1"));
        EnemyProjectile fireball = BuildProjectilePrefab(GetFireSheet("Fire_2"));
        EnemyAttackSet attackSet = BuildAttackSet(fireball, foxfire);
        BuildPrefab(controller, stats, attackSet, fireball);

        AssetDatabase.SaveAssets();

        SwapLevel2Boss();

        if (!string.IsNullOrEmpty(previousScene) && previousScene != Level2ScenePath)
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);

        Debug.Log("Kitsune boss built:\n- " + string.Join("\n- ", Report));
    }

    #region Sprites

    // Pixel data straight from the PNG, so the texture does not have to be imported as readable.
    private class Pixels
    {
        private readonly Color32[] data;
        private readonly int width;
        private readonly int frameSize;

        public Pixels(string assetPath, int frameSize)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(assetPath));
            width = texture.width;
            Height = texture.height;
            this.frameSize = frameSize;
            Frames = texture.width / frameSize;
            data = texture.GetPixels32();
            Object.DestroyImmediate(texture);
        }

        public int Frames { get; }
        public int Height { get; }

        // x inside the frame, row counted from the bottom of the frame.
        private Color32 Get(int frame, int x, int row) => data[row * width + frame * frameSize + x];

        public bool Opaque(int frame, int x, int row) => Get(frame, x, row).a > 20;

        // The red dress: dark reds with almost no green. Hair, tails and skin are all far warmer and brighter.
        private static bool IsDress(Color32 c) => c.a > 20 && c.g < 50 && c.r > c.g + 30 && c.b < 70;

        // The fox fire: cyan and blue. Nothing on the body is that cold.
        public static bool IsFire(Color32 c) => c.a > 20 && c.r < 160 && c.g > 140 && c.b > 150;

        /// <summary>Horizontal centre of the skirt, or -1 when the frame shows none (the body already burned away).</summary>
        public float SkirtX(int frame)
        {
            float sum = 0f;
            int count = 0;
            for (int row = SkirtRowMin; row <= SkirtRowMax; row++)
            {
                for (int x = 0; x < frameSize; x++)
                {
                    if (!IsDress(Get(frame, x, row))) continue;
                    sum += x;
                    count++;
                }
            }
            return count > 0 ? sum / count : -1f;
        }

        /// <summary>Bounds of the dress on one frame: the body without hair, tails or effects.</summary>
        public RectInt DressBounds(int frame) => Bounds(frame, frame, 0, IsDress);

        /// <summary>Opaque bounds over frames [first, last], only counting columns at or right of minX.</summary>
        public RectInt OpaqueBounds(int first, int last, int minX = 0) => Bounds(first, last, minX, c => c.a > 20);

        /// <summary>Bounds of the fire drawn on frames [first, last], only counting columns at or right of minX.</summary>
        public RectInt FireBounds(int first, int last, int minX = 0) => Bounds(first, last, minX, IsFire);

        /// <summary>
        /// Bounds of the body drawn on one frame: only saturated pixels that are not fire (dress, skin, hair and
        /// tails), so the black outlines and the fox fire around her are left out. Empty RectInt when there is none.
        /// </summary>
        public RectInt BodyBounds(int frame)
        {
            return Bounds(frame, frame, 0, c =>
            {
                if (c.a <= 20 || IsFire(c)) return false;
                int chroma = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                return chroma >= 24;
            });
        }

        /// <summary>Centre of the opaque pixels of frames [first, last], in pixels from the frame's bottom left.</summary>
        public Vector2 OpaqueCentre(int first, int last)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            for (int f = first; f <= last; f++)
            {
                for (int row = 0; row < Height; row++)
                {
                    for (int x = 0; x < frameSize; x++)
                    {
                        if (!Opaque(f, x, row)) continue;
                        sum += new Vector2(x, row);
                        count++;
                    }
                }
            }
            return count > 0 ? sum / count : new Vector2(frameSize * 0.5f, Height * 0.5f);
        }

        private RectInt Bounds(int first, int last, int minX, Func<Color32, bool> include)
        {
            int xMin = int.MaxValue, xMax = -1, rowMin = int.MaxValue, rowMax = -1;
            for (int f = first; f <= last; f++)
            {
                for (int row = 0; row < Height; row++)
                {
                    for (int x = Mathf.Max(0, minX); x < frameSize; x++)
                    {
                        if (!include(Get(f, x, row))) continue;
                        xMin = Mathf.Min(xMin, x);
                        xMax = Mathf.Max(xMax, x);
                        rowMin = Mathf.Min(rowMin, row);
                        rowMax = Mathf.Max(rowMax, row);
                    }
                }
            }
            return xMax < 0 ? new RectInt() : new RectInt(xMin, rowMin, xMax - xMin + 1, rowMax - rowMin + 1);
        }
    }

    private static float ComputePivot(Sheet sheet)
    {
        if (PivotOverridePx.TryGetValue(sheet.file, out float manual)) return manual;

        if (!sheet.pivotFromAllFrames)
        {
            float first = sheet.pixels.SkirtX(0);
            return Mathf.Round(first >= 0f ? first : FrameSize * 0.5f);
        }

        float sum = 0f;
        int count = 0;
        for (int f = 0; f < sheet.frames; f++)
        {
            float x = sheet.pixels.SkirtX(f);
            if (x < 0f) continue;
            sum += x;
            count++;
        }
        return Mathf.Round(count > 0 ? sum / count : FrameSize * 0.5f);
    }

    // Grid-slices the sheet (keeping the existing sprite IDs), and puts every frame's pivot on the skirt at the
    // feet, so all clips line up with each other and the prefab root sits on the floor.
    private static void ImportSheet(Sheet sheet)
    {
        sheet.pixels = new Pixels(sheet.AssetPath, FrameSize);
        sheet.frames = sheet.pixels.Frames;
        sheet.pivotPx = ComputePivot(sheet);

        Vector2 pivot = new Vector2(sheet.pivotPx / FrameSize, 0f);
        var extra = new List<(string name, Rect rect, Vector2 pivot)>();

        // Head crop of the first idle frame for the boss bar and dialogs.
        if (sheet.file == "Idle")
        {
            const int size = 34;
            int top = sheet.pixels.OpaqueBounds(0, 0).yMax + 1; // the tips of the ears
            int x = Mathf.Clamp(Mathf.RoundToInt(sheet.pivotPx) - 1 - size / 2, 0, FrameSize - size);
            int y = Mathf.Clamp(top - size, 0, FrameSize - size);
            extra.Add((PortraitSpriteName, new Rect(x, y, size, size), new Vector2(0.5f, 0.5f)));
        }

        sheet.sprites = SliceGrid(sheet.AssetPath, sheet.file, sheet.frames, FrameSize, PixelsPerUnit, _ => pivot, extra);
        Report.Add($"{sheet.file}: {sheet.frames} frames of {FrameSize}px, pivot x={sheet.pivotPx}px (feet), clip {sheet.clipName}");
    }

    // The flame's head sits on the pivot: that is where the collider is and what the shot is aimed with.
    private static void ImportFireSheet(FireSheet fire)
    {
        var pixels = new Pixels(fire.AssetPath, FireFrameSize);
        fire.frames = pixels.Frames;

        RectInt flight = pixels.OpaqueBounds(0, fire.flyFrames - 1);
        Vector2 centre = pixels.OpaqueCentre(0, fire.flyFrames - 1);
        float headX = flight.width > 0 ? flight.xMax - 4f : FireFrameSize * 0.5f;
        Vector2 pivot = new Vector2(headX / FireFrameSize, centre.y / pixels.Height);

        fire.sprites = SliceGrid(fire.AssetPath, fire.file, fire.frames, FireFrameSize, FirePixelsPerUnit, _ => pivot, null);
        Report.Add($"{fire.file}: {fire.frames} frames of {FireFrameSize}px ({fire.flyFrames} flying), pivot {pivot * FireFrameSize}px");
    }

    /// <summary>
    /// One sprite per grid cell named "{file}_{i}", plus any extra rects. Rects that already carry those names keep
    /// their sprite IDs (so clips that point at them survive a rebuild); every other rect is dropped, which also
    /// clears what Unity's automatic slicing left behind (effects cut out as separate sprites).
    /// </summary>
    private static Sprite[] SliceGrid(string assetPath, string file, int frames, int frameSize, float pixelsPerUnit,
        Func<int, Vector2> pivotFor, List<(string name, Rect rect, Vector2 pivot)> extra)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        List<SpriteRect> existing = provider.GetSpriteRects().ToList();
        var nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> pairs = nameProvider.GetNameFileIdPairs().ToList();

        var rects = new List<SpriteRect>();
        int height = Mathf.RoundToInt(frameSize);
        for (int i = 0; i < frames; i++)
        {
            SpriteRect rect = FindOrCreateRect(existing, pairs, $"{file}_{i}");
            rect.rect = new Rect(i * frameSize, 0f, frameSize, height);
            rect.alignment = SpriteAlignment.Custom;
            rect.pivot = pivotFor(i);
            rect.border = Vector4.zero;
            rects.Add(rect);
        }

        if (extra != null)
        {
            foreach ((string name, Rect area, Vector2 pivot) in extra)
            {
                SpriteRect rect = FindOrCreateRect(existing, pairs, name);
                rect.rect = area;
                rect.alignment = SpriteAlignment.Custom;
                rect.pivot = pivot;
                rect.border = Vector4.zero;
                rects.Add(rect);
            }
        }

        int dropped = existing.Count(r => !rects.Contains(r));
        if (dropped > 0) Report.Add($"{file}: {dropped} sprites of the old slicing removed");

        provider.SetSpriteRects(rects.ToArray());
        nameProvider.SetNameFileIdPairs(pairs);
        provider.Apply();
        importer.SaveAndReimport();

        var sprites = new Sprite[frames];
        for (int i = 0; i < frames; i++) sprites[i] = LoadSprite(assetPath, $"{file}_{i}");
        return sprites;
    }

    private static SpriteRect FindOrCreateRect(List<SpriteRect> rects, List<SpriteNameFileIdPair> pairs, string rectName)
    {
        SpriteRect rect = rects.FirstOrDefault(r => r.name == rectName);
        if (rect != null) return rect;

        rect = new SpriteRect { name = rectName, spriteID = GUID.Generate() };
        pairs.Add(new SpriteNameFileIdPair(rectName, rect.spriteID));
        return rect;
    }

    private static Sprite LoadSprite(string assetPath, string spriteName)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().FirstOrDefault(s => s.name == spriteName);
    }

    #endregion

    #region Animation

    // One key per frame plus a closing key, so the clip lasts exactly count / fps and the attack states can
    // compute where each frame starts.
    private static AnimationClip BuildClip(string clipName, Sprite[] sprites, int first, int count, float fps, bool loop)
    {
        string path = $"{AnimationFolder}/{clipName}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool created = clip == null;
        if (created) clip = new AnimationClip { name = clipName };

        clip.frameRate = fps;
        var keys = new ObjectReferenceKeyframe[count + 1];
        for (int i = 0; i < count; i++)
        {
            keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = sprites[first + i] };
        }
        keys[count] = new ObjectReferenceKeyframe { time = count / fps, value = sprites[first + count - 1] };

        EditorCurveBinding binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (created) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);

        return clip;
    }

    // Same pattern as Medusa.controller: one state per clip and no transitions, because the enemy states
    // pick the clip in code (Animator.Play) through EnemyStateDriver.
    private static AnimatorController BuildController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name == RetiredGazeName) machine.RemoveState(child.state);
        }

        AnimatorState idle = null;
        for (int i = 0; i < Sheets.Length; i++)
        {
            Sheet sheet = Sheets[i];
            AnimatorState state = SetState(machine, sheet.stateName, sheet.clip, new Vector3(300f + (i % 2) * 260f, 40f + i * 50f, 0f));
            if (sheet.file == "Idle") idle = state;
        }

        if (idle != null) machine.defaultState = idle;
        EditorUtility.SetDirty(controller);
        Report.Add($"Controller {ControllerPath}: {machine.states.Length} states");
        return controller;
    }

    // Two clips per projectile: the flight loops until EnemyProjectile plays the impact, which it waits out
    // (impactDuration) before destroying the shot.
    private static void BuildFireAnimation(FireSheet fire)
    {
        fire.flyClip = BuildClip($"{fire.prefabName}_Fly", fire.sprites, 0, fire.flyFrames, fire.flyFps, true);
        fire.impactClip = BuildClip($"{fire.prefabName}_Impact", fire.sprites, fire.flyFrames, fire.frames - fire.flyFrames, fire.impactFps, false);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(fire.ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(fire.ControllerPath);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState fly = SetState(machine, "Fly", fire.flyClip, new Vector3(300f, 40f, 0f));
        SetState(machine, "Impact", fire.impactClip, new Vector3(300f, 120f, 0f));
        machine.defaultState = fly;

        EditorUtility.SetDirty(controller);
        fire.controller = controller;
    }

    private static AnimatorState SetState(AnimatorStateMachine machine, string stateName, Motion clip, Vector3 position)
    {
        AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName)
                              ?? machine.AddState(stateName, position);
        state.motion = clip;
        state.writeDefaultValues = true;
        return state;
    }

    #endregion

    #region Data

    // Level2 is the second boss: a little more health and a little faster than Medusa (150 / 3.2).
    private static EnemyScriptableObject BuildStats()
    {
        return LoadOrCreate<EnemyScriptableObject>(StatsPath, stats =>
        {
            stats.moveSpeed = 3.4f;
            stats.maxHealth = 220f;
            stats.damage = 14f;        // only used by the legacy attacks; the attack assets carry their own
            stats.knockbackForce = 25f;
        });
    }

    private static Entity_SO BuildEntity(Sprite portrait)
    {
        Entity_SO entity = LoadOrCreate<Entity_SO>(EntityPath, e =>
        {
            e.entityName = "Kitsune";
            e.entitySprite = portrait;
        });

        if (entity.entitySprite == null && portrait != null)
        {
            entity.entitySprite = portrait;
            EditorUtility.SetDirty(entity);
        }
        return entity;
    }

    // One response per upgrade paid in Engrenagem de Ouro (the second row of the vendor's trees), balanced by the
    // same "half the advantage" rule as Medusa's bronze row: damage -> health, life -> damage, speed -> speed.
    private static BossUpgradeScaling_SO BuildScaling()
    {
        BossUpgradeScaling_SO existing = AssetDatabase.LoadAssetAtPath<BossUpgradeScaling_SO>(ScalingPath);
        if (existing != null) return existing;

        return LoadOrCreate<BossUpgradeScaling_SO>(ScalingPath, scaling =>
        {
            scaling.compensation = 0.5f;
            foreach (UpgradeNode_SO node in FindNodesPaidWith(GoldGearItemId))
            {
                scaling.responses.Add(new BossUpgradeResponse
                {
                    upgrade = node,
                    bossStat = BossUpgradeScaling_SO.CounterFor(node.stat)
                });
            }

            if (BossUpgradeScaling_SO.TryReadPlayerBase(out float damage, out float health, out float speed))
                scaling.Recalculate(damage, health, speed);
            else
                Report.Add("WARNING: player base stats not found, adaptation multipliers left at 1 (use the context menu)");

            foreach (BossUpgradeResponse response in scaling.responses)
            {
                Report.Add($"{response.upgrade.nodeName} ({response.upgrade.stat.FormatBonus(response.upgrade.bonusValue)}) -> " +
                           $"Kitsune {BossUpgradeScaling_SO.Describe(response.bossStat, response.multiplier)} (x{response.multiplier})");
            }
        });
    }

    private static IEnumerable<UpgradeNode_SO> FindNodesPaidWith(string itemId)
    {
        return AssetDatabase.FindAssets("t:UpgradeNode_SO")
            .Select(guid => AssetDatabase.LoadAssetAtPath<UpgradeNode_SO>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(node => node != null && node.cost != null &&
                           node.cost.Any(slot => slot != null && slot.item != null && slot.item.itemId == itemId && slot.quantity > 0))
            .OrderBy(node => node.stat);
    }

    // Level2's boss data keeps its asset name, bossId, reward task and scene (saves, the boss selector and the
    // Colosso prerequisite depend on them); only the face and the adaptation change. A different entity or
    // adaptation set by hand is left alone.
    private static void LinkBossData(Entity_SO entity, BossUpgradeScaling_SO scaling)
    {
        BossData_SO data = AssetDatabase.LoadAssetAtPath<BossData_SO>(BossDataPath);
        if (data == null)
        {
            Report.Add($"WARNING: {BossDataPath} not found, the boss bar will show the object name");
            return;
        }

        if (data.entity != entity)
        {
            if (data.entity == null || data.entity.name == PlaceholderEntityName)
            {
                data.entity = entity;
                Report.Add($"{BossDataPath}: entity -> Kitsune (bossId '{data.bossId}' unchanged)");
            }
            else
            {
                Report.Add($"{BossDataPath} keeps its hand-set entity '{data.entity.name}'");
            }
        }

        if (data.upgradeScaling == null)
        {
            data.upgradeScaling = scaling;
            Report.Add($"{BossDataPath}: upgradeScaling -> {scaling.name}");
        }
        else if (data.upgradeScaling != scaling)
        {
            Report.Add($"{BossDataPath} keeps its hand-set adaptation '{data.upgradeScaling.name}'");
        }

        EditorUtility.SetDirty(data);
    }

    // A fresh prefab rather than a variant of EnemyProjectile.prefab: that one is a scaled circle on the root, and
    // these need an animated child. What it hits and what stops it are copied from it, so all enemy shots agree.
    private static EnemyProjectile BuildProjectilePrefab(FireSheet fire)
    {
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(fire.PrefabPath) != null;
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(fire.PrefabPath) : new GameObject(fire.prefabName);

        try
        {
            var body = GetOrAdd<Rigidbody2D>(root);
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var collider = GetOrAdd<CircleCollider2D>(root);
            collider.isTrigger = true;
            if (!exists) collider.radius = fire.colliderRadius;

            Transform visual = GetOrCreateChild(root.transform, "Visual", Vector3.zero);
            var renderer = GetOrAdd<SpriteRenderer>(visual.gameObject);
            renderer.sprite = fire.sprites[0];
            renderer.sortingOrder = 2; // in front of the boss (1), so a shot leaving her hand is never hidden
            Material spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;

            var animator = GetOrAdd<Animator>(visual.gameObject);
            animator.runtimeAnimatorController = fire.controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var projectile = GetOrAdd<EnemyProjectile>(root);
            var so = new SerializedObject(projectile);
            if (!exists) CopyProjectileBasics(so);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("impactState").stringValue = "Impact";
            so.FindProperty("impactDuration").floatValue = Round2(fire.ImpactDuration);
            so.FindProperty("alignToDirection").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, fire.PrefabPath);
            Report.Add($"{fire.PrefabPath} {(exists ? "updated" : "created")}: {fire.flyFrames} flying frames, impact {Round2(fire.ImpactDuration)}s");
            return saved.GetComponent<EnemyProjectile>();
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    private static void CopyProjectileBasics(SerializedObject target)
    {
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseProjectilePath);
        EnemyProjectile baseProjectile = basePrefab != null ? basePrefab.GetComponent<EnemyProjectile>() : null;
        if (baseProjectile == null)
        {
            target.FindProperty("damageableLayer").intValue = 1 << LayerMask.NameToLayer("Default");
            target.FindProperty("blockingLayer").intValue = LayerMask.GetMask("Ground");
            Report.Add($"WARNING: {BaseProjectilePath} not found, projectile layers set to Default / Ground");
            return;
        }

        var source = new SerializedObject(baseProjectile);
        foreach (string field in new[] { "lifeTime", "damageableLayer", "blockingLayer", "verticalKnockback" })
            target.CopyFromSerializedProperty(source.FindProperty(field));
    }

    private static EnemyAttackSet BuildAttackSet(EnemyProjectile fireball, EnemyProjectile foxfire)
    {
        MeleeAttackDefinition wave = BuildAttack<MeleeAttackDefinition>("Kitsune_FoxfireWave", "Attack_1", InitFoxfireWave);
        ProjectileAttackDefinition shot = BuildAttack<ProjectileAttackDefinition>("Kitsune_Fireball", "Attack_3",
            (d, s) => InitFireball(d, fireball));
        ProjectileAttackDefinition barrage = BuildAttack<ProjectileAttackDefinition>("Kitsune_FoxfireBarrage", "Attack_2",
            (d, s) => InitFoxfireBarrage(d, foxfire));

        EnemyAttackSet attackSet = LoadOrCreate<EnemyAttackSet>(AttackSetPath, set =>
        {
            set.attacks = new List<EnemyAttackDefinition> { wave, shot, barrage };
            set.globalCooldown = 0.6f;
            set.repeatPenalty = 0.35f;
            set.secondRepeatPenalty = 0.75f;
            set.tiebreakWindow = 0.15f;
            set.rangeEdgeScore = 0.6f;
            set.closeRange = 2.4f;
            // Phase 1: the fox fire wave up close and the fireball at range. Phase 2 opens with the barrage and
            // keeps it in the rotation.
            set.phases = new List<BossPhase>
            {
                new() { name = "Fase 1", healthFraction = 1f },
                new()
                {
                    name = "Nove Caudas", healthFraction = 0.55f, cooldownMultiplier = 0.8f, globalCooldownMultiplier = 0.7f,
                    openingAttack = barrage
                }
            };
            set.allowRetreat = true;
            set.maxRetreatTime = 1.4f;
            set.retreatSpeedMultiplier = 0.9f;
            set.runDistance = 7f;
            set.runSpeedMultiplier = 1.5f;
            set.holdTolerance = 0.4f;
            set.rangeMargin = 0.3f;
        });

        RetireGaze(attackSet);
        return attackSet;
    }

    // Takes the retired gaze out of an attack set built by the earlier version (also as a phase opener) and
    // deletes its attack asset and clip. Nothing to do on a fresh build.
    private static void RetireGaze(EnemyAttackSet set)
    {
        int removed = set.attacks.RemoveAll(attack => attack == null || attack.name == RetiredGazeName);
        if (set.phases != null)
        {
            foreach (BossPhase phase in set.phases)
            {
                if (phase.openingAttack != null && phase.openingAttack.name == RetiredGazeName) phase.openingAttack = null;
            }
        }

        if (removed > 0)
        {
            EditorUtility.SetDirty(set);
            Report.Add($"{AttackSetPath}: petrifying gaze removed");
        }

        foreach (string path in new[] { $"{AttackFolder}/{RetiredGazeName}.asset", $"{AnimationFolder}/{RetiredGazeName}.anim" })
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null && AssetDatabase.DeleteAsset(path))
                Report.Add($"{path} deleted");
        }
    }

    // The clip link (state name, frame rate, frame count) always follows the generated clip; everything
    // else is tuning and only written when the asset is created.
    private static T BuildAttack<T>(string assetName, string sheetFile, Action<T, Sheet> init) where T : EnemyAttackDefinition
    {
        Sheet sheet = GetSheet(sheetFile);
        T attack = LoadOrCreate<T>($"{AttackFolder}/{assetName}.asset", created =>
        {
            created.totalFrames = sheet.frames;
            init(created, sheet);
        });

        attack.animationState = sheet.stateName;
        attack.driveAnimation = true;
        attack.sourceFrameRate = sheet.fps;
        attack.totalFrames = sheet.frames;
        attack.bodyFrames = ComputeBodyFrames(sheet);
        EditorUtility.SetDirty(attack);
        return attack;
    }

    // Where the body is drawn on every frame, relative to the root pivot (x forward, y up from the feet). Derived
    // from the art like the clip data, so it is rewritten on every run; it is not tuning.
    private static AttackFrameBody[] ComputeBodyFrames(Sheet sheet)
    {
        const float pad = 0.05f;
        var frames = new AttackFrameBody[sheet.frames];
        for (int i = 0; i < sheet.frames; i++)
        {
            RectInt r = sheet.pixels.BodyBounds(i);
            if (r.width <= 0)
            {
                // No drawn body on this frame: keep the resting box (size 0 is raised to it by the driver).
                frames[i] = new AttackFrameBody { center = Vector2.zero, size = Vector2.zero };
                continue;
            }

            frames[i] = new AttackFrameBody
            {
                center = new Vector2(Round2((r.x + r.width * 0.5f - sheet.pivotPx) / PixelsPerUnit), Round2((r.y + r.height * 0.5f) / PixelsPerUnit)),
                size = new Vector2(Round2(r.width / PixelsPerUnit + pad * 2f), Round2(r.height / PixelsPerUnit + pad * 2f))
            };
        }
        return frames;
    }

    private static AttackSegment Segment(string label, AttackSegmentKind kind, int first, int last, float duration)
    {
        return new AttackSegment { label = label, kind = kind, firstFrame = first, lastFrame = last, duration = duration };
    }

    // Active segment whose box is fitted to the fire drawn on those frames, from frontOffsetPx (relative to the
    // skirt) forward, shrunk by a couple of pixels so near misses stay misses. A big negative offset takes the
    // flames behind her too.
    private static AttackSegment FireSegment(Sheet sheet, string label, int first, int last, float duration,
        int frontOffsetPx, float damage, float knockback, float verticalKnockback)
    {
        const int inset = 2;
        RectInt px = sheet.pixels.FireBounds(first, last, Mathf.RoundToInt(sheet.pivotPx) + frontOffsetPx);
        if (px.width <= 0) px = sheet.pixels.OpaqueBounds(first, last, Mathf.RoundToInt(sheet.pivotPx));

        float width = Mathf.Max(4, px.width - inset * 2) / PixelsPerUnit;
        float height = Mathf.Max(4, px.height - inset * 2) / PixelsPerUnit;
        float centreX = (px.x + px.width * 0.5f - sheet.pivotPx) / PixelsPerUnit;
        float centreY = (px.y + px.height * 0.5f) / PixelsPerUnit;

        AttackSegment segment = Segment(label, AttackSegmentKind.Active, first, last, duration);
        segment.hitbox = new AttackHitbox
        {
            shape = AttackHitShape.Box,
            offset = new Vector2(Round2(centreX), Round2(centreY)),
            size = new Vector2(Round2(width), Round2(height)),
            damage = damage,
            knockback = knockback,
            verticalKnockback = verticalKnockback
        };
        return segment;
    }

    // Attack_1: the tails sweep around (f0-3) and light the floor (f4); blue flames burst along the ground on both
    // sides of her (f5-6), then she flings the fire forward in a tall wave (f7-8) and lowers her arm (f9).
    // The ground burst punishes standing next to her; jumping clears it, the wave catches the landing.
    private static void InitFoxfireWave(MeleeAttackDefinition d, Sheet s)
    {
        d.displayName = "Onda de Fogo-Fatuo";
        d.segments = new List<AttackSegment>
        {
            Segment("gira as caudas", AttackSegmentKind.WindUp, 0, 4, 0.7f),
            FireSegment(s, "chamas no chao", 5, 6, 0.2f, -FrameSize, 14f, 24f, 0.6f),
            FireSegment(s, "onda", 7, 8, 0.2f, 8, 16f, 28f, 0.8f),
            Segment("recupera", AttackSegmentKind.Recovery, 9, 9, 0.45f)
        };
        d.minRange = 0f;
        d.maxRange = 3.4f;
        d.minTargetHeight = -1f;
        d.maxTargetHeight = 2.5f;
        d.cooldown = 3.5f;
        d.scoring = new AttackScoring
        {
            weight = 1.1f, targetAttacking = 1.3f, lingerBonusPerSecond = 0.3f, maxLingerMultiplier = 2f
        };
        d.telegraphColor = new Color(0.45f, 0.85f, 1f, 1f);
    }

    // Attack_3: raises the hand (f0-2), the flame gathers in the palm (f3-5) and leaves as a wisp (f6). The
    // recovery holds the empty outstretched arm (f2), so the shot is not drawn twice while it flies.
    private static void InitFireball(ProjectileAttackDefinition d, EnemyProjectile fireball)
    {
        d.displayName = "Bola de Fogo-Fatuo";
        d.segments = new List<AttackSegment>
        {
            Segment("ergue a mao", AttackSegmentKind.WindUp, 0, 2, 0.45f),
            Segment("concentra a chama", AttackSegmentKind.WindUp, 3, 5, 0.35f),
            Segment("dispara", AttackSegmentKind.Active, 6, 6, 0.15f),
            Segment("abaixa a mao", AttackSegmentKind.Recovery, 2, 2, 0.35f)
        };
        d.projectilePrefab = fireball;
        d.speed = 9f;
        d.damage = 15f;
        d.knockback = 22f;
        d.count = 1;
        d.extraProjectilesPerPhase = 1; // second phase: two wisps in a narrow fan
        d.spreadAngle = 12f;
        d.aimAtTarget = true;
        d.maxAimAngle = 30f;
        d.minRange = 3.5f;
        d.maxRange = 16f;
        d.minTargetHeight = -3f;
        d.maxTargetHeight = 6f;
        d.requiresLineOfSight = true;
        d.cooldown = 2.6f;
        d.scoring = new AttackScoring
        {
            weight = 1f, targetRetreating = 1.3f, targetGrappling = 1.4f, targetAirborne = 1.1f
        };
        d.telegraphColor = new Color(0.4f, 0.8f, 1f, 1f);
    }

    // Attack_2, second phase only: wisps of fox fire appear around her (f0-2, a long telegraph), then she points and
    // sends them in two fans (f3-4 and f6-7) and the last wisps fade (f8-9).
    private static void InitFoxfireBarrage(ProjectileAttackDefinition d, EnemyProjectile foxfire)
    {
        d.displayName = "Rajada de Fogos-Fatuos";
        d.segments = new List<AttackSegment>
        {
            Segment("invoca os fogos", AttackSegmentKind.WindUp, 0, 2, 0.8f),
            Segment("primeira rajada", AttackSegmentKind.Active, 3, 4, 0.25f),
            Segment("mira de novo", AttackSegmentKind.WindUp, 5, 5, 0.2f),
            Segment("segunda rajada", AttackSegmentKind.Active, 6, 7, 0.25f),
            Segment("recupera", AttackSegmentKind.Recovery, 8, 9, 0.45f)
        };
        d.recoveryHoldTime = 0.3f; // lets the last wisps fade (f8-9) before she settles back into Idle
        d.projectilePrefab = foxfire;
        d.speed = 11f;
        d.damage = 9f;
        d.knockback = 16f;
        d.count = 4;
        d.extraProjectilesPerPhase = 0;
        d.spreadAngle = 40f;
        d.aimAtTarget = true;
        d.maxAimAngle = 40f;
        d.minRange = 0f;
        d.maxRange = 18f; // always valid, so it can open the second phase from anywhere in the arena
        d.minTargetHeight = -3f;
        d.maxTargetHeight = 7f;
        d.requiresLineOfSight = true;
        d.cooldown = 7f;
        d.minPhase = 1;
        d.scoring = new AttackScoring
        {
            weight = 1f, enragedMultiplier = 1.2f, targetGrappling = 1.5f, targetAirborne = 1.3f, targetRetreating = 1.2f
        };
        d.telegraphColor = new Color(0.55f, 0.95f, 1f, 1f);
    }

    #endregion

    #region Prefab

    // A new prefab, built like Medusa.prefab. An existing Kitsune prefab is edited in place (LoadPrefabContents) so
    // the scene's references to its components survive.
    private static void BuildPrefab(AnimatorController controller, EnemyScriptableObject stats, EnemyAttackSet attackSet, EnemyProjectile fireball)
    {
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Kitsune");

        try
        {
            root.layer = LayerMask.NameToLayer("AttackableLayer"); // what the player's attack hits

            var body = GetOrAdd<Rigidbody2D>(root);
            if (!exists)
            {
                body.bodyType = RigidbodyType2D.Dynamic;
                body.mass = 5f;
                body.gravityScale = 15f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }

            // Body collider fitted to the idle pose: the dress, as tall as she stands, not the tails behind her.
            Sheet idle = GetSheet("Idle");
            RectInt idleBounds = idle.pixels.OpaqueBounds(0, 0);
            RectInt dress = idle.pixels.DressBounds(0);
            var collider = GetOrAdd<BoxCollider2D>(root);
            float colliderHeight = idleBounds.yMax * 0.85f / PixelsPerUnit;
            float colliderWidth = Mathf.Max(dress.width, 12) * 0.9f / PixelsPerUnit;
            collider.size = new Vector2(Round2(colliderWidth), Round2(colliderHeight));
            collider.offset = new Vector2(0f, Round2(colliderHeight * 0.5f));

            Transform visual = GetOrCreateChild(root.transform, "Visual", Vector3.zero);
            var renderer = GetOrAdd<SpriteRenderer>(visual.gameObject);
            renderer.sprite = idle.sprites[0];
            renderer.sortingOrder = 1;
            Material spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;

            var animator = GetOrAdd<Animator>(visual.gameObject);
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Transform groundCheck = GetOrCreateChild(root.transform, "GroundCheck", new Vector3(0f, 0.05f, 0f));

            BoxCollider2D hurtbox = GetOrCreateHurtbox(root, collider);

            // Hand: where the flame gathers on the fireball sheet. Both projectiles leave from here.
            Sheet cast = GetSheet("Attack_3");
            RectInt flame = cast.pixels.FireBounds(3, 3);
            Vector3 handLocal = flame.width > 0
                ? new Vector3(Round2((flame.x + flame.width * 0.5f - cast.pivotPx) / PixelsPerUnit), Round2((flame.y + flame.height * 0.5f) / PixelsPerUnit), 0f)
                : new Vector3(1.3f, 1.8f, 0f);
            Transform handPoint = GetOrCreateChild(root.transform, "HandPoint", handLocal);
            handPoint.localPosition = handLocal;

            var driver = GetOrAdd<EnemyStateDriver>(root);
            var so = new SerializedObject(driver);
            so.FindProperty("data").objectReferenceValue = stats;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("attackSet").objectReferenceValue = attackSet;
            so.FindProperty("projectilePrefab").objectReferenceValue = fireball;
            so.FindProperty("projectileSpawnPoint").objectReferenceValue = handPoint;
            so.FindProperty("groundCheck").objectReferenceValue = groundCheck;
            so.FindProperty("groundLayer").intValue = LayerMask.GetMask("Ground");
            so.FindProperty("tintRenderer").objectReferenceValue = renderer;
            so.FindProperty("hurtbox").objectReferenceValue = hurtbox;

            BossData_SO bossData = AssetDatabase.LoadAssetAtPath<BossData_SO>(BossDataPath);
            if (so.FindProperty("bossData").objectReferenceValue == null) so.FindProperty("bossData").objectReferenceValue = bossData;

            if (!exists)
            {
                so.FindProperty("detectionRange").floatValue = 30f; // the whole arena
                so.FindProperty("attackRange").floatValue = 3.4f;
                so.FindProperty("stopDistance").floatValue = 1.2f;
                so.FindProperty("waitForActivation").boolValue = true;
                so.FindProperty("superArmorWhileAttacking").boolValue = true;
                so.FindProperty("groundCheckRadius").floatValue = 0.2f;
                so.FindProperty("invulnerabilityTime").floatValue = 0.15f;
                so.FindProperty("hitAnimationTime").floatValue = GetSheet("Hurt").frames / GetSheet("Hurt").fps;
                // The last frames are only sparks: a short hold is enough before she disappears.
                so.FindProperty("deathDelay").floatValue = GetSheet("Dead").frames / GetSheet("Dead").fps + 0.5f;
                so.FindProperty("logStatePath").boolValue = false;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Report.Add($"{PrefabPath} {(exists ? "updated" : "created")}: collider {collider.size}, hand {handLocal}");
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    // The box the player's swings test. A trigger child on the attackable layer, sized like the body collider; the
    // driver moves it with the drawn body during attacks (the physical body collider stays on the root, so the
    // fight's physics do not change). Created once; an existing one keeps its size so it can be tuned by hand.
    private static BoxCollider2D GetOrCreateHurtbox(GameObject root, BoxCollider2D body)
    {
        Transform child = GetOrCreateChild(root.transform, "Hurtbox", Vector3.zero);
        child.gameObject.layer = root.layer;
        if (child.TryGetComponent(out BoxCollider2D existing)) return existing;

        BoxCollider2D hurtbox = child.gameObject.AddComponent<BoxCollider2D>();
        hurtbox.isTrigger = true;
        hurtbox.size = body.size;
        hurtbox.offset = body.offset;
        return hurtbox;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        return go.TryGetComponent(out T component) ? component : go.AddComponent<T>();
    }

    private static Transform GetOrCreateChild(Transform parent, string childName, Vector3 localPosition)
    {
        Transform child = parent.Find(childName);
        if (child != null) return child;

        child = new GameObject(childName).transform;
        child.SetParent(parent, false);
        child.localPosition = localPosition;
        child.gameObject.layer = parent.gameObject.layer;
        return child;
    }

    #endregion

    #region Level2

    // Replaces the placeholder boss the Level2 BossGate points at with a Kitsune instance at the same spot, rewires
    // the gate, and deletes the placeholder. Everything else (barrier, camera, task, boss bar, progression, Daisy)
    // goes through the gate or the driver's BossData, so it follows automatically.
    private static void SwapLevel2Boss()
    {
        Scene scene = EditorSceneManager.OpenScene(Level2ScenePath, OpenSceneMode.Single);

        // Loaded again after the scene switch: OpenScene unloads assets only this code was holding.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        BossGate gate = Object.FindObjectsByType<BossGate>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(g => g.gameObject.scene == scene);
        if (gate == null)
        {
            Report.Add("WARNING: no BossGate in Level2, the boss was not swapped");
            return;
        }

        var gateSo = new SerializedObject(gate);
        SerializedProperty bossProperty = gateSo.FindProperty("boss");
        var oldBoss = bossProperty.objectReferenceValue as EnemyStateDriver;

        if (oldBoss != null && PrefabUtility.GetCorrespondingObjectFromOriginalSource(oldBoss.gameObject) == prefab)
        {
            Report.Add("Level2 already uses the Kitsune prefab; scene left as is");
            return;
        }

        Vector3 position = oldBoss != null ? oldBoss.transform.position : gate.transform.position;
        position.y = FindFloor(position, oldBoss);

        var kitsune = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        kitsune.name = "Kitsune (Boss)";
        if (oldBoss != null)
        {
            kitsune.transform.SetParent(oldBoss.transform.parent, false);
            kitsune.transform.SetSiblingIndex(oldBoss.transform.GetSiblingIndex());
        }
        // Faces the arena entrance (the gate trigger side) until the fight starts and it turns to the player.
        kitsune.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

        bossProperty.objectReferenceValue = kitsune.GetComponent<EnemyStateDriver>();
        gateSo.ApplyModifiedPropertiesWithoutUndo();

        if (oldBoss != null)
        {
            GameObject oldRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(oldBoss.gameObject);
            string oldName = (oldRoot != null ? oldRoot : oldBoss.gameObject).name;
            Object.DestroyImmediate(oldRoot != null ? oldRoot : oldBoss.gameObject);
            Report.Add($"Level2: '{oldName}' replaced by Kitsune at {position}, BossGate.boss rewired");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // The floor under the old boss, so Kitsune's feet (her pivot) start on the ground.
    private static float FindFloor(Vector3 position, EnemyStateDriver oldBoss)
    {
        RaycastHit2D hit = Physics2D.Raycast(new Vector2(position.x, position.y + 2f), Vector2.down, 30f, LayerMask.GetMask("Ground"));
        if (hit.collider != null) return hit.point.y + 0.02f;

        Collider2D oldCollider = oldBoss != null ? oldBoss.GetComponent<Collider2D>() : null;
        return oldCollider != null ? oldCollider.bounds.min.y + 0.02f : position.y;
    }

    #endregion

    private static T LoadOrCreate<T>(string path, Action<T> init) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;

        asset = ScriptableObject.CreateInstance<T>();
        init(asset);
        AssetDatabase.CreateAsset(asset, path);
        Report.Add($"{path} created");
        return asset;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static float Round2(float value) => Mathf.Round(value * 100f) / 100f;
}
