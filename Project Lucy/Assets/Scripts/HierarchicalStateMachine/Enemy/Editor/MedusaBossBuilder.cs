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
/// Tools > Lucy > Build Medusa Boss. Turns the sheets in Assets/Sprites/Enemy/Medusa into the Level1 boss:
/// sprite import (grid, pixels per unit, per-sheet pivots measured from the art), clips, the animator
/// controller, the attack data, the Medusa prefab, and the swap into the Level1 arena (BossGate rewired).
///
/// Safe to run again. Generated assets (clips, controller, sprite import, prefab structure) are updated in
/// place so their GUIDs and file IDs survive. Tuning assets (stats, the five attacks, the attack set, the
/// entity) are only created when missing, so balancing done in the Inspector is never overwritten: delete an
/// asset to get its defaults back. The scene swap is skipped once the gate already points at a Medusa.
/// </summary>
public static class MedusaBossBuilder
{
    private const string SpriteFolder = "Assets/Sprites/Enemy/Medusa";
    private const string AnimationFolder = "Assets/Animations/Enemy/Medusa";
    private const string DataFolder = "Assets/ObjectData/Bosses/Medusa";
    private const string AttackFolder = DataFolder + "/Attacks";
    private const string ControllerPath = AnimationFolder + "/Medusa.controller";
    private const string StatsPath = DataFolder + "/Medusa_Stats.asset";
    private const string AttackSetPath = DataFolder + "/Medusa_AttackSet.asset";
    private const string EntityPath = "Assets/ObjectData/Entity/Medusa.asset";
    private const string BossDataPath = "Assets/ObjectData/Bosses/Guardiao.asset";
    private const string PrefabPath = "Assets/Renan/Prefabs/Medusa.prefab";
    private const string VenomPrefabPath = "Assets/Renan/Prefabs/MedusaVenom.prefab";
    private const string BaseProjectilePath = "Assets/Renan/Prefabs/EnemyProjectile.prefab";
    private const string SpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
    private const string Level1ScenePath = "Assets/Scenes/Level1.unity";

    private const int FrameSize = 128;
    // 25 px per unit: Medusa's ~82 px standing height becomes ~3.3 units, about 1.5x the player.
    private const float PixelsPerUnit = 25f;
    private const string PortraitSpriteName = "Medusa_Portrait";

    // Rows (from the bottom of a frame) that hold the upper body: below the hair, above the coiled tail.
    // Their skin and cloth pixels are the most stable anchor between sheets, whose frames are not aligned.
    private const int TorsoRowMin = 32;
    private const int TorsoRowMax = 69;

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
        public bool movesForward;       // the body ends further forward inside the frame

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
        new() { file = "Idle", clipName = "Medusa_Idle", stateName = "Enemy_Idle", fps = 8f, loop = true, pivotFromAllFrames = true },
        new() { file = "Walk", clipName = "Medusa_Walk", stateName = "Enemy_Walk", fps = 12f, loop = true, pivotFromAllFrames = true },
        new() { file = "Run", clipName = "Medusa_Run", stateName = "Enemy_Running", fps = 12f, loop = true, pivotFromAllFrames = true },
        new() { file = "Hurt", clipName = "Medusa_Hurt", stateName = "Enemy_Hit", fps = 12f },
        new() { file = "Dead", clipName = "Medusa_Dead", stateName = "Enemy_Death", fps = 6f },
        new() { file = "Attack_2", clipName = "Medusa_TailWhip", stateName = "Medusa_TailWhip", fps = 12f },
        new() { file = "Attack_1", clipName = "Medusa_CoilCrush", stateName = "Medusa_CoilCrush", fps = 12f, movesForward = true },
        new() { file = "Attack_3", clipName = "Medusa_Lunge", stateName = "Medusa_Lunge", fps = 12f, movesForward = true },
        new() { file = "Idle_2", clipName = "Medusa_VenomSpit", stateName = "Medusa_VenomSpit", fps = 10f },
        new() { file = "Special", clipName = "Medusa_Gaze", stateName = "Medusa_Gaze", fps = 10f }
    };

    private static Sheet GetSheet(string file) => Sheets.First(s => s.file == file);

    private static readonly List<string> Report = new();

    [MenuItem("Tools/Lucy/Build Medusa Boss")]
    public static void Build()
    {
        // Level1 is opened below, so the open scenes have to be saved (or knowingly discarded) first.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Report.Clear();
        string previousScene = SceneManager.GetActiveScene().path;

        EnsureFolder(AnimationFolder);
        EnsureFolder(AttackFolder);

        foreach (Sheet sheet in Sheets) ImportSheet(sheet);
        foreach (Sheet sheet in Sheets) BuildClip(sheet);
        AnimatorController controller = BuildController();

        Sprite portrait = LoadSprite(GetSheet("Idle").AssetPath, PortraitSpriteName);
        EnemyScriptableObject stats = BuildStats();
        Entity_SO entity = BuildEntity(portrait);
        LinkBossData(entity);
        EnemyProjectile venom = BuildVenomPrefab();
        EnemyAttackSet attackSet = BuildAttackSet(venom);
        BuildPrefab(controller, stats, attackSet, venom);

        AssetDatabase.SaveAssets();

        SwapLevel1Boss();

        if (!string.IsNullOrEmpty(previousScene) && previousScene != Level1ScenePath)
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);

        Debug.Log("Medusa boss built:\n- " + string.Join("\n- ", Report));
    }

    #region Sprites

    // Pixel data straight from the PNG, so the texture does not have to be imported as readable.
    private class Pixels
    {
        private readonly Color32[] data;
        private readonly int width;

        public Pixels(string assetPath)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(assetPath));
            width = texture.width;
            Frames = texture.width / FrameSize;
            data = texture.GetPixels32();
            Object.DestroyImmediate(texture);
        }

        public int Frames { get; }

        // x inside the frame, row counted from the bottom of the frame.
        private Color32 Get(int frame, int x, int row) => data[row * width + frame * FrameSize + x];

        public bool Opaque(int frame, int x, int row) => Get(frame, x, row).a > 20;

        private static bool IsSkinOrCloth(Color32 c) => c.a > 20 && c.r > c.g + 25;

        /// <summary>Horizontal centre of the upper body's skin and cloth, or -1 when the frame shows none.</summary>
        public float TorsoX(int frame)
        {
            float sum = 0f;
            int count = 0;
            for (int row = TorsoRowMin; row <= TorsoRowMax; row++)
            {
                for (int x = 0; x < FrameSize; x++)
                {
                    if (!IsSkinOrCloth(Get(frame, x, row))) continue;
                    sum += x;
                    count++;
                }
            }
            return count > 0 ? sum / count : -1f;
        }

        /// <summary>Opaque bounds over frames [first, last], only counting columns at or right of minX.</summary>
        public RectInt OpaqueBounds(int first, int last, int minX = 0, int maxRow = FrameSize - 1)
        {
            int xMin = int.MaxValue, xMax = -1, rowMin = int.MaxValue, rowMax = -1;
            for (int f = first; f <= last; f++)
            {
                for (int row = 0; row <= maxRow; row++)
                {
                    for (int x = Mathf.Max(0, minX); x < FrameSize; x++)
                    {
                        if (!Opaque(f, x, row)) continue;
                        xMin = Mathf.Min(xMin, x);
                        xMax = Mathf.Max(xMax, x);
                        rowMin = Mathf.Min(rowMin, row);
                        rowMax = Mathf.Max(rowMax, row);
                    }
                }
            }
            return xMax < 0 ? new RectInt() : new RectInt(xMin, rowMin, xMax - xMin + 1, rowMax - rowMin + 1);
        }

        /// <summary>Right-most skin pixel of the head (the mouth / tongue tip when facing right).</summary>
        public Vector2Int MouthTip(int frame, int rowMin, int rowMax)
        {
            var best = new Vector2Int(-1, -1);
            for (int row = rowMin; row <= rowMax; row++)
            {
                for (int x = 0; x < FrameSize; x++)
                {
                    if (IsSkinOrCloth(Get(frame, x, row)) && x > best.x) best = new Vector2Int(x, row);
                }
            }
            return best;
        }
    }

    private static float ComputePivot(Sheet sheet)
    {
        if (PivotOverridePx.TryGetValue(sheet.file, out float manual)) return manual;

        if (!sheet.pivotFromAllFrames) return Mathf.Round(Mathf.Max(0f, sheet.pixels.TorsoX(0)));

        float sum = 0f;
        int count = 0;
        for (int f = 0; f < sheet.frames; f++)
        {
            float x = sheet.pixels.TorsoX(f);
            if (x < 0f) continue;
            sum += x;
            count++;
        }
        return Mathf.Round(count > 0 ? sum / count : FrameSize * 0.5f);
    }

    // Grid-slices the sheet (keeping the existing sprite IDs), and puts every frame's pivot on the torso
    // at the feet, so all clips line up with each other and the prefab root sits on the floor.
    private static void ImportSheet(Sheet sheet)
    {
        sheet.pixels = new Pixels(sheet.AssetPath);
        sheet.frames = sheet.pixels.Frames;
        sheet.pivotPx = ComputePivot(sheet);

        var importer = (TextureImporter)AssetImporter.GetAtPath(sheet.AssetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        List<SpriteRect> rects = provider.GetSpriteRects().ToList();
        var nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> pairs = nameProvider.GetNameFileIdPairs().ToList();

        Vector2 pivot = new Vector2(sheet.pivotPx / FrameSize, 0f);
        for (int i = 0; i < sheet.frames; i++)
        {
            SpriteRect rect = FindOrAddRect(rects, pairs, $"{sheet.file}_{i}");
            rect.rect = new Rect(i * FrameSize, 0f, FrameSize, FrameSize);
            rect.alignment = SpriteAlignment.Custom;
            rect.pivot = pivot;
        }

        // Head crop of the first idle frame for the boss bar and dialogs.
        if (sheet.file == "Idle")
        {
            const int size = 52;
            Vector2Int mouth = sheet.pixels.MouthTip(0, 52, 70);
            int centreX = mouth.x > 0 ? mouth.x - 6 : Mathf.RoundToInt(sheet.pivotPx);
            int top = sheet.pixels.OpaqueBounds(0, 0).yMax; // top of the hair
            int x = Mathf.Clamp(centreX - size / 2, 0, FrameSize - size);
            int y = Mathf.Clamp(top - size, 0, FrameSize - size);

            SpriteRect portrait = FindOrAddRect(rects, pairs, PortraitSpriteName);
            portrait.rect = new Rect(x, y, size, size);
            portrait.alignment = SpriteAlignment.Center;
            portrait.pivot = new Vector2(0.5f, 0.5f);
        }

        provider.SetSpriteRects(rects.ToArray());
        nameProvider.SetNameFileIdPairs(pairs);
        provider.Apply();
        importer.SaveAndReimport();

        sheet.sprites = new Sprite[sheet.frames];
        for (int i = 0; i < sheet.frames; i++) sheet.sprites[i] = LoadSprite(sheet.AssetPath, $"{sheet.file}_{i}");

        Report.Add($"{sheet.file}: {sheet.frames} frames of {FrameSize}px, pivot x={sheet.pivotPx}px (feet), clip {sheet.clipName}");
    }

    private static SpriteRect FindOrAddRect(List<SpriteRect> rects, List<SpriteNameFileIdPair> pairs, string rectName)
    {
        SpriteRect rect = rects.FirstOrDefault(r => r.name == rectName);
        if (rect != null) return rect;

        rect = new SpriteRect { name = rectName, spriteID = GUID.Generate() };
        rects.Add(rect);
        pairs.Add(new SpriteNameFileIdPair(rectName, rect.spriteID));
        return rect;
    }

    private static Sprite LoadSprite(string assetPath, string spriteName)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().FirstOrDefault(s => s.name == spriteName);
    }

    #endregion

    #region Animation

    // One key per frame plus a closing key, so the clip lasts exactly frames / fps and the attack states can
    // compute where each frame starts.
    private static void BuildClip(Sheet sheet)
    {
        string path = $"{AnimationFolder}/{sheet.clipName}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool created = clip == null;
        if (created) clip = new AnimationClip { name = sheet.clipName };

        clip.frameRate = sheet.fps;
        var keys = new ObjectReferenceKeyframe[sheet.frames + 1];
        for (int i = 0; i < sheet.frames; i++)
        {
            keys[i] = new ObjectReferenceKeyframe { time = i / sheet.fps, value = sheet.sprites[i] };
        }
        keys[sheet.frames] = new ObjectReferenceKeyframe { time = sheet.frames / sheet.fps, value = sheet.sprites[sheet.frames - 1] };

        EditorCurveBinding binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = sheet.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (created) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);

        sheet.clip = clip;
    }

    // Same pattern as Enemy.controller: one state per clip and no transitions, because the enemy states
    // pick the clip in code (Animator.Play) through EnemyStateDriver.
    private static AnimatorController BuildController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = null;
        for (int i = 0; i < Sheets.Length; i++)
        {
            Sheet sheet = Sheets[i];
            AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == sheet.stateName)
                                  ?? machine.AddState(sheet.stateName, new Vector3(300f + (i % 2) * 260f, 40f + i * 50f, 0f));
            state.motion = sheet.clip;
            state.writeDefaultValues = true;
            if (sheet.file == "Idle") idle = state;
        }

        if (idle != null) machine.defaultState = idle;
        EditorUtility.SetDirty(controller);
        Report.Add($"Controller {ControllerPath}: {machine.states.Length} states");
        return controller;
    }

    #endregion

    #region Data

    private static EnemyScriptableObject BuildStats()
    {
        return LoadOrCreate<EnemyScriptableObject>(StatsPath, stats =>
        {
            stats.moveSpeed = 3.2f;
            stats.maxHealth = 150f;
            stats.damage = 12f;        // only used by the legacy attacks; the attack assets carry their own
            stats.knockbackForce = 25f;
        });
    }

    private static Entity_SO BuildEntity(Sprite portrait)
    {
        Entity_SO entity = LoadOrCreate<Entity_SO>(EntityPath, e =>
        {
            e.entityName = "Medusa";
            e.entitySprite = portrait;
        });

        if (entity.entitySprite == null && portrait != null)
        {
            entity.entitySprite = portrait;
            EditorUtility.SetDirty(entity);
        }
        return entity;
    }

    // Level1's boss data keeps its asset name and bossId (saves and the Sentinela prerequisite depend on
    // them); only the face changes. A different entity set by hand is left alone.
    private static void LinkBossData(Entity_SO entity)
    {
        BossData_SO data = AssetDatabase.LoadAssetAtPath<BossData_SO>(BossDataPath);
        if (data == null)
        {
            Report.Add($"WARNING: {BossDataPath} not found, the boss bar will show the object name");
            return;
        }

        if (data.entity == entity) return;
        if (data.entity != null && data.entity.name != "Guardiao")
        {
            Report.Add($"{BossDataPath} keeps its hand-set entity '{data.entity.name}'");
            return;
        }

        data.entity = entity;
        EditorUtility.SetDirty(data);
        Report.Add($"{BossDataPath}: entity -> Medusa (bossId '{data.bossId}' unchanged)");
    }

    private static EnemyProjectile BuildVenomPrefab()
    {
        GameObject venom = AssetDatabase.LoadAssetAtPath<GameObject>(VenomPrefabPath);
        if (venom == null)
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseProjectilePath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            instance.name = "MedusaVenom";
            foreach (SpriteRenderer renderer in instance.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.color = new Color(0.45f, 0.95f, 0.35f, 1f);

            venom = PrefabUtility.SaveAsPrefabAsset(instance, VenomPrefabPath);
            Object.DestroyImmediate(instance);
            Report.Add($"{VenomPrefabPath}: green variant of EnemyProjectile");
        }

        return venom.GetComponent<EnemyProjectile>();
    }

    private static EnemyAttackSet BuildAttackSet(EnemyProjectile venom)
    {
        MeleeAttackDefinition whip = BuildAttack<MeleeAttackDefinition>("Medusa_TailWhip", "Attack_2", InitTailWhip);
        MeleeAttackDefinition crush = BuildAttack<MeleeAttackDefinition>("Medusa_CoilCrush", "Attack_1", InitCoilCrush);
        MeleeAttackDefinition lunge = BuildAttack<MeleeAttackDefinition>("Medusa_Lunge", "Attack_3", InitLunge);
        ProjectileAttackDefinition spit = BuildAttack<ProjectileAttackDefinition>("Medusa_VenomSpit", "Idle_2",
            (d, s) => InitVenomSpit(d, venom));
        GazeAttackDefinition gaze = BuildAttack<GazeAttackDefinition>("Medusa_Gaze", "Special", InitGaze);

        return LoadOrCreate<EnemyAttackSet>(AttackSetPath, set =>
        {
            set.attacks = new List<EnemyAttackDefinition> { whip, crush, lunge, spit, gaze };
            set.globalCooldown = 0.7f;
            set.repeatPenalty = 0.35f;
            set.secondRepeatPenalty = 0.75f;
            set.tiebreakWindow = 0.15f;
            set.rangeEdgeScore = 0.6f;
            set.closeRange = 2.6f;
            set.phases = new List<BossPhase>
            {
                new() { name = "Fase 1", healthFraction = 1f },
                new()
                {
                    name = "Furia", healthFraction = 0.6f, cooldownMultiplier = 0.8f, globalCooldownMultiplier = 0.7f,
                    openingAttack = gaze
                }
            };
            set.allowRetreat = true;
            set.maxRetreatTime = 1.2f;
            set.retreatSpeedMultiplier = 0.8f;
            set.runDistance = 8f;
            set.runSpeedMultiplier = 1.6f;
            set.holdTolerance = 0.4f;
            set.rangeMargin = 0.3f;
        });
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
        EditorUtility.SetDirty(attack);
        return attack;
    }

    private static AttackSegment Segment(string label, AttackSegmentKind kind, int first, int last, float duration)
    {
        return new AttackSegment { label = label, kind = kind, firstFrame = first, lastFrame = last, duration = duration };
    }

    // Active segment whose box is fitted to the art: everything opaque on those frames from frontOffsetPx
    // (relative to the torso) forward, shrunk by a couple of pixels so near misses stay misses.
    private static AttackSegment ActiveSegment(Sheet sheet, string label, int first, int last, float duration,
        int frontOffsetPx, float damage, float knockback, float verticalKnockback, float advance = 0f)
    {
        const int inset = 2;
        RectInt px = sheet.pixels.OpaqueBounds(first, last, Mathf.RoundToInt(sheet.pivotPx) + frontOffsetPx);
        float width = Mathf.Max(4, px.width - inset * 2) / PixelsPerUnit;
        float height = Mathf.Max(4, px.height - inset * 2) / PixelsPerUnit;
        float centreX = (px.x + px.width * 0.5f - sheet.pivotPx) / PixelsPerUnit;
        float centreY = (px.y + px.height * 0.5f) / PixelsPerUnit;

        AttackSegment segment = Segment(label, AttackSegmentKind.Active, first, last, duration);
        segment.advanceDistance = advance;
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

    // How far forward the torso ends inside the frame, for sheets whose last frame is not where they began.
    private static float ExitOffset(Sheet sheet)
    {
        if (!sheet.movesForward) return 0f;
        float start = sheet.pixels.TorsoX(0);
        float end = sheet.pixels.TorsoX(sheet.frames - 1);
        return start < 0f || end < 0f ? 0f : Round2((end - start) / PixelsPerUnit);
    }

    // Attack_2: the tail lifts (f0-3), arcs over and sweeps forward along the ground (f4-5), settles (f6).
    private static void InitTailWhip(MeleeAttackDefinition d, Sheet s)
    {
        d.displayName = "Tail Whip";
        d.segments = new List<AttackSegment>
        {
            Segment("cauda sobe", AttackSegmentKind.WindUp, 0, 3, 0.45f),
            ActiveSegment(s, "chicote", 4, 5, 0.18f, 0, 12f, 22f, 0.45f),
            Segment("volta", AttackSegmentKind.Recovery, 6, 6, 0.35f)
        };
        d.minRange = 0f;
        d.maxRange = 2.6f;
        d.minTargetHeight = -1f;
        d.maxTargetHeight = 2.5f;
        d.cooldown = 1.6f;
        d.scoring = new AttackScoring { weight = 1f, targetAttacking = 1.5f, targetAirborne = 0.8f, targetPetrified = 1.5f };
        d.telegraphColor = new Color(1f, 0.85f, 0.4f, 1f);
        d.exitForwardOffset = ExitOffset(s);
    }

    // Attack_1: coils (f0-5), lunges and bites (f6-7), rises and coils again (f8-12), then rears up tall in
    // a rising strike (f13-14) that also catches a player jumping over her, and settles (f15).
    private static void InitCoilCrush(MeleeAttackDefinition d, Sheet s)
    {
        d.displayName = "Coil Crush";
        d.segments = new List<AttackSegment>
        {
            Segment("enrola", AttackSegmentKind.WindUp, 0, 5, 0.8f),
            ActiveSegment(s, "bote", 6, 7, 0.2f, 4, 16f, 30f, 0.4f),
            Segment("prepara o golpe alto", AttackSegmentKind.WindUp, 8, 12, 0.55f),
            ActiveSegment(s, "golpe ascendente", 13, 14, 0.2f, -14, 20f, 35f, 1.2f),
            Segment("recupera", AttackSegmentKind.Recovery, 15, 15, 0.6f)
        };
        d.minRange = 0f;
        d.maxRange = 3.2f;
        d.minTargetHeight = -1f;
        d.maxTargetHeight = 4f;
        d.cooldown = 7f;
        d.scoring = new AttackScoring
        {
            weight = 1.1f, enragedMultiplier = 1.3f, targetAirborne = 1.4f, targetPetrified = 2f,
            lingerBonusPerSecond = 0.35f, maxLingerMultiplier = 2f
        };
        d.telegraphColor = new Color(1f, 0.35f, 0.3f, 1f);
        d.exitForwardOffset = ExitOffset(s);
    }

    // Attack_3: coils (f0-5), shoots forward low and long (f6-8), rises further ahead (f9).
    private static void InitLunge(MeleeAttackDefinition d, Sheet s)
    {
        d.displayName = "Serpent Lunge";
        d.segments = new List<AttackSegment>
        {
            Segment("enrola", AttackSegmentKind.WindUp, 0, 5, 0.6f),
            ActiveSegment(s, "investida", 6, 8, 0.3f, 8, 15f, 28f, 0.5f, advance: 2f),
            Segment("levanta", AttackSegmentKind.Recovery, 9, 9, 0.45f)
        };
        d.minRange = 2.8f;
        d.maxRange = 6f;
        d.minTargetHeight = -1f;
        d.maxTargetHeight = 1.8f;
        d.cooldown = 4f;
        d.scoring = new AttackScoring
        {
            weight = 1f, enragedMultiplier = 1.1f, targetRetreating = 1.3f, targetAirborne = 0.5f, targetPetrified = 2.5f
        };
        d.telegraphColor = new Color(1f, 0.6f, 0.25f, 1f);
        d.exitForwardOffset = ExitOffset(s);
    }

    // Idle_2: a hiss - the tongue flicks out on f2-3, read as the spit.
    private static void InitVenomSpit(ProjectileAttackDefinition d, EnemyProjectile venom)
    {
        d.displayName = "Venom Spit";
        d.segments = new List<AttackSegment>
        {
            Segment("sibila", AttackSegmentKind.WindUp, 0, 1, 0.5f),
            Segment("cospe", AttackSegmentKind.Active, 2, 3, 0.2f),
            Segment("volta", AttackSegmentKind.Recovery, 4, 4, 0.3f)
        };
        d.projectilePrefab = venom;
        d.speed = 11f;
        d.damage = 10f;
        d.knockback = 18f;
        d.count = 1;
        d.extraProjectilesPerPhase = 2;
        d.spreadAngle = 16f;
        d.aimAtTarget = true;
        d.maxAimAngle = 35f;
        d.minRange = 4.5f;
        d.maxRange = 18f;
        d.minTargetHeight = -3f;
        d.maxTargetHeight = 6f;
        d.requiresLineOfSight = true;
        d.cooldown = 2.2f;
        d.scoring = new AttackScoring { weight = 0.9f, targetGrappling = 1.4f, targetAirborne = 1.2f };
        d.telegraphColor = new Color(0.55f, 1f, 0.45f, 1f);
    }

    // Special: rears up, hair flaring; the eyes glint from f2. Held on f0-1 as a long telegraph.
    private static void InitGaze(GazeAttackDefinition d, Sheet s)
    {
        d.displayName = "Petrifying Gaze";
        d.segments = new List<AttackSegment>
        {
            Segment("encara", AttackSegmentKind.WindUp, 0, 1, 1f),
            Segment("olhar", AttackSegmentKind.Active, 2, 4, 0.6f),
            Segment("recupera", AttackSegmentKind.Recovery, 4, 4, 0.4f)
        };
        d.gazeRange = 12f;
        d.coneAngle = 70f;
        d.requireTargetFacing = true;
        d.petrifyDuration = 1.8f; // outlasts recovery + pause, so her punish wind-up starts while the player is stone
        d.petrifyImmunity = 3f;
        d.damage = 6f;
        d.minRange = 0f;
        d.maxRange = 12f;
        d.minTargetHeight = -3f;
        d.maxTargetHeight = 6f;
        d.requiresLineOfSight = true;
        d.cooldown = 16f;
        d.minPhase = 1;
        d.scoring = new AttackScoring { weight = 1.3f, targetFacing = 2f, targetFacingAway = 0.3f, targetPetrified = 0.2f };
        d.telegraphColor = new Color(0.75f, 0.55f, 1f, 1f);
    }

    #endregion

    #region Prefab

    // New prefab, not a change to EnemyAI.prefab: Level2 and Level3 still use that one. An existing Medusa
    // prefab is edited in place (LoadPrefabContents) so the scene's references to its components survive.
    private static void BuildPrefab(AnimatorController controller, EnemyScriptableObject stats, EnemyAttackSet attackSet, EnemyProjectile venom)
    {
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Medusa");

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

            // Body collider fitted to the idle pose: the torso and coil, not the hair or the tail tip.
            Sheet idle = GetSheet("Idle");
            RectInt idleBounds = idle.pixels.OpaqueBounds(0, 0);
            var collider = GetOrAdd<BoxCollider2D>(root);
            float colliderHeight = idleBounds.yMax * 0.85f / PixelsPerUnit;
            float colliderWidth = idleBounds.width * 0.45f / PixelsPerUnit;
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

            // Mouth: the tongue tip of the hiss frame. Projectiles leave from here and the gaze looks from here.
            Sheet hiss = GetSheet("Idle_2");
            Vector2Int mouth = hiss.pixels.MouthTip(2, 52, 70);
            Vector3 mouthLocal = mouth.x >= 0
                ? new Vector3(Round2((mouth.x - hiss.pivotPx) / PixelsPerUnit), Round2(mouth.y / PixelsPerUnit), 0f)
                : new Vector3(0.7f, 2.6f, 0f);
            Transform mouthPoint = GetOrCreateChild(root.transform, "MouthPoint", mouthLocal);
            mouthPoint.localPosition = mouthLocal;

            var driver = GetOrAdd<EnemyStateDriver>(root);
            var so = new SerializedObject(driver);
            so.FindProperty("data").objectReferenceValue = stats;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("attackSet").objectReferenceValue = attackSet;
            so.FindProperty("projectilePrefab").objectReferenceValue = venom;
            so.FindProperty("projectileSpawnPoint").objectReferenceValue = mouthPoint;
            so.FindProperty("groundCheck").objectReferenceValue = groundCheck;
            so.FindProperty("groundLayer").intValue = LayerMask.GetMask("Ground");
            so.FindProperty("tintRenderer").objectReferenceValue = renderer;

            BossData_SO bossData = AssetDatabase.LoadAssetAtPath<BossData_SO>(BossDataPath);
            if (so.FindProperty("bossData").objectReferenceValue == null) so.FindProperty("bossData").objectReferenceValue = bossData;

            if (!exists)
            {
                so.FindProperty("detectionRange").floatValue = 30f; // the whole arena
                so.FindProperty("attackRange").floatValue = 2.6f;
                so.FindProperty("stopDistance").floatValue = 1.2f;
                so.FindProperty("waitForActivation").boolValue = true;
                so.FindProperty("superArmorWhileAttacking").boolValue = true;
                so.FindProperty("groundCheckRadius").floatValue = 0.2f;
                so.FindProperty("invulnerabilityTime").floatValue = 0.15f;
                so.FindProperty("hitAnimationTime").floatValue = GetSheet("Hurt").frames / GetSheet("Hurt").fps;
                so.FindProperty("deathDelay").floatValue = GetSheet("Dead").frames / GetSheet("Dead").fps + 0.8f;
                so.FindProperty("logStatePath").boolValue = false;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Report.Add($"{PrefabPath} {(exists ? "updated" : "created")}: collider {collider.size}, mouth {mouthLocal}");
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
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

    #region Level1

    // Replaces the boss the Level1 BossGate points at with a Medusa instance at the same spot, rewires the
    // gate, and deletes the old instance. Everything else (barrier, task, boss bar, progression) goes through
    // the gate or the driver's BossData, so it follows automatically.
    private static void SwapLevel1Boss()
    {
        Scene scene = EditorSceneManager.OpenScene(Level1ScenePath, OpenSceneMode.Single);

        // Loaded again after the scene switch: OpenScene unloads assets only this code was holding.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        BossGate gate = Object.FindObjectsByType<BossGate>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(g => g.gameObject.scene == scene);
        if (gate == null)
        {
            Report.Add("WARNING: no BossGate in Level1, the boss was not swapped");
            return;
        }

        var gateSo = new SerializedObject(gate);
        SerializedProperty bossProperty = gateSo.FindProperty("boss");
        var oldBoss = bossProperty.objectReferenceValue as EnemyStateDriver;

        if (oldBoss != null && PrefabUtility.GetCorrespondingObjectFromOriginalSource(oldBoss.gameObject) == prefab)
        {
            Report.Add("Level1 already uses the Medusa prefab; scene left as is");
            return;
        }

        Vector3 position = oldBoss != null ? oldBoss.transform.position : gate.transform.position;
        position.y = FindFloor(position, oldBoss);

        var medusa = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        medusa.name = "Medusa (Boss)";
        if (oldBoss != null)
        {
            medusa.transform.SetParent(oldBoss.transform.parent, false);
            medusa.transform.SetSiblingIndex(oldBoss.transform.GetSiblingIndex());
        }
        // Faces the arena entrance (the gate trigger side) until the fight starts and it turns to the player.
        medusa.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

        bossProperty.objectReferenceValue = medusa.GetComponent<EnemyStateDriver>();
        gateSo.ApplyModifiedPropertiesWithoutUndo();

        if (oldBoss != null)
        {
            GameObject oldRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(oldBoss.gameObject);
            string oldName = (oldRoot != null ? oldRoot : oldBoss.gameObject).name;
            Object.DestroyImmediate(oldRoot != null ? oldRoot : oldBoss.gameObject);
            Report.Add($"Level1: '{oldName}' replaced by Medusa at {position}, BossGate.boss rewired");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // The floor under the old boss, so Medusa's feet (her pivot) start on the ground.
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
