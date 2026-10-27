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
/// Tools > Lucy > Build Mage Boss. Turns the sheets in Assets/Sprites/Enemy/Mage into the Level3 boss, the same way
/// KitsuneBossBuilder made the Level2 one: sprite import (grid, pixels per unit, per-sheet pivots measured from the
/// art), clips, the animator controller, the two animated projectiles (Charge_1 for the sphere, Charge_2 for the
/// arrow), the attack data, the evasive teleport, the intro dialog, the adaptation to
/// the emerald upgrades, the Mage prefab, and the swap into the Level3 arena (BossGate rewired). Level3's boss data
/// (Colosso.asset) keeps its bossId and reward; only its face and its adaptation change.
///
/// Safe to run again. Generated assets (clips, controllers, sprite import, prefab structure) are updated in place
/// so their GUIDs and file IDs survive. Tuning assets (stats, the two attacks, the attack set, the adaptation, the
/// entity) are only created when missing, so balancing done in the Inspector is never overwritten: delete an asset
/// to get its defaults back. The scene swap is skipped once the gate already points at a Mage.
/// </summary>
public static class MageBossBuilder
{
    private const string SpriteFolder = "Assets/Sprites/Enemy/Mage";
    private const string AnimationFolder = "Assets/Animations/Enemy/Mage";
    private const string DataFolder = "Assets/ObjectData/Bosses/Mage";
    private const string AttackFolder = DataFolder + "/Attacks";
    private const string ControllerPath = AnimationFolder + "/Mage.controller";
    private const string StatsPath = DataFolder + "/Mage_Stats.asset";
    private const string AttackSetPath = DataFolder + "/Mage_AttackSet.asset";
    private const string ScalingPath = DataFolder + "/Mage_UpgradeScaling.asset";
    private const string EntityPath = "Assets/ObjectData/Entity/Mago.asset";
    private const string IntroDialogPath = "Assets/ObjectData/Dialog/Mago-intro.asset";
    private const string BossDataPath = "Assets/ObjectData/Bosses/Colosso.asset";
    private const string PlaceholderEntityName = "Colosso";
    private const string PrefabPath = "Assets/Renan/Prefabs/Mage.prefab";
    private const string BaseProjectilePath = "Assets/Renan/Prefabs/EnemyProjectile.prefab";
    private const string SpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
    private const string Level3ScenePath = "Assets/Scenes/Level3.unity";

    // Level3 answers the third row of the vendor's trees: the upgrades paid in Esmeralda.
    private const string EmeraldItemId = "emerald";

    private const int FrameSize = 128;
    // 25 px per unit, like Medusa and the Kitsune: the Mage's ~66 px standing height becomes ~2.6 units.
    private const float PixelsPerUnit = 25f;
    private const string PortraitSpriteName = "Mage_Portrait";

    // What the Mage says instead of the full intro on a rematch after the player died: 2nd fight, 3rd, 4th, then
    // the last line from the 5th on, with {N} = the fight number. After the Mage is beaten there is no line at all.
    private static readonly string[] RetryIntroLines =
    {
        "Mais uma vez? Parece que você não aprende, né?",
        "Você é persistente, hein, mocinha? Mas isso não vai mudar o final.",
        "Quatro vezes já... Estou começando a sentir pena de você.",
        "{N} e contando... Volte quantas vezes quiser, o resultado vai ser o mesmo."
    };

    // Rows (from the bottom of a frame) of the legs. The bandaged shins and the shoes are the most stable anchor
    // between sheets: the robe and the cape swing, and the bow is held out in front.
    private const int LegRowMax = 14;

    // The projectile sheets are 64 px wide and as tall as the body sheets.
    private const int ChargeFrameWidth = 64;
    private const int ChargeFrameHeight = 128;
    // A little bigger than the body's scale so the shots read clearly at a distance.
    private const float ChargePixelsPerUnit = 20f;

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

    // State names are the ones the enemy states ask for.
    private static readonly Sheet[] Sheets =
    {
        new() { file = "Idle", clipName = "Mage_Idle", stateName = "Enemy_Idle", fps = 8f, loop = true, pivotFromAllFrames = true },
        new() { file = "Run", clipName = "Mage_Run", stateName = "Enemy_Running", fps = 12f, loop = true, pivotFromAllFrames = true },
        new() { file = "Hurt", clipName = "Mage_Hurt", stateName = "Enemy_Hit", fps = 8f },
        new() { file = "Dead", clipName = "Mage_Dead", stateName = "Enemy_Death", fps = 6f },
        new() { file = "Magic_arrow", clipName = "Mage_MagicArrow", stateName = "Mage_MagicArrow", fps = 10f },
        new() { file = "Magic_sphere", clipName = "Mage_MagicSphere", stateName = "Mage_MagicSphere", fps = 12f }
    };

    private static Sheet GetSheet(string file) => Sheets.First(s => s.file == file);

    /// <summary>A projectile sheet: some frames fly in a loop, the frames after them (if any) are the impact.</summary>
    private class ChargeSheet
    {
        public string file;
        public string prefabName;
        public int flyFirst;
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
        public int ImpactFirst => flyFirst + flyFrames;
        public int ImpactFrames => Mathf.Max(0, frames - ImpactFirst);
        public float ImpactDuration => ImpactFrames / impactFps;
    }

    // Charge_1: a ball of magic with a short tail (f0-4) that bursts into a ring of sparks (f5-8).
    // Charge_2: a spark (f0) that stretches into a thin magic arrow (f1-5). It has no impact frames: it just vanishes.
    private static readonly ChargeSheet[] ChargeSheets =
    {
        new() { file = "Charge_1", prefabName = "MageSphere", flyFirst = 0, flyFrames = 5, flyFps = 12f, impactFps = 14f, colliderRadius = 0.3f },
        new() { file = "Charge_2", prefabName = "MageArrow", flyFirst = 1, flyFrames = 5, flyFps = 14f, impactFps = 14f, colliderRadius = 0.15f }
    };

    private static ChargeSheet GetChargeSheet(string file) => ChargeSheets.First(s => s.file == file);

    private static readonly List<string> Report = new();

    [MenuItem("Tools/Lucy/Build Mage Boss")]
    public static void Build()
    {
        // Level3 is opened below, so the open scenes have to be saved (or knowingly discarded) first.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Report.Clear();
        string previousScene = SceneManager.GetActiveScene().path;

        EnsureFolder(AnimationFolder);
        EnsureFolder(AttackFolder);

        foreach (Sheet sheet in Sheets) ImportSheet(sheet);
        foreach (ChargeSheet charge in ChargeSheets) ImportChargeSheet(charge);
        foreach (Sheet sheet in Sheets) sheet.clip = BuildClip(sheet.clipName, sheet.sprites, 0, sheet.frames, sheet.fps, sheet.loop);
        foreach (ChargeSheet charge in ChargeSheets) BuildChargeAnimation(charge);
        AnimatorController controller = BuildController();

        Sprite portrait = LoadSprite(GetSheet("Idle").AssetPath, PortraitSpriteName);
        EnemyScriptableObject stats = BuildStats();
        Entity_SO entity = BuildEntity(portrait);
        BossUpgradeScaling_SO scaling = BuildScaling();
        LinkBossData(entity, scaling);

        EnemyProjectile sphere = BuildProjectilePrefab(GetChargeSheet("Charge_1"));
        EnemyProjectile arrow = BuildProjectilePrefab(GetChargeSheet("Charge_2"));
        EnemyAttackSet attackSet = BuildAttackSet(arrow, sphere);
        BuildPrefab(controller, stats, attackSet, sphere);

        AssetDatabase.SaveAssets();

        SwapLevel3Boss();

        if (!string.IsNullOrEmpty(previousScene) && previousScene != Level3ScenePath)
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);

        Debug.Log("Mage boss built:\n- " + string.Join("\n- ", Report));
    }

    #region Sprites

    // Pixel data straight from the PNG, so the texture does not have to be imported as readable.
    private class Pixels
    {
        private readonly Color32[] data;
        private readonly int width;
        private readonly int frameWidth;

        public Pixels(string assetPath, int frameWidth)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(assetPath));
            width = texture.width;
            Height = texture.height;
            this.frameWidth = frameWidth;
            Frames = texture.width / frameWidth;
            data = texture.GetPixels32();
            Object.DestroyImmediate(texture);
        }

        public int Frames { get; }
        public int Height { get; }

        // x inside the frame, row counted from the bottom of the frame.
        private Color32 Get(int frame, int x, int row) => data[row * width + frame * frameWidth + x];

        private static bool Opaque(Color32 c) => c.a > 20;

        private static bool Is(Color32 c, int r, int g, int b) => c.r == r && c.g == g && c.b == b;

        // Shin bandages, skin and shoes. Nothing else near the floor has those colours.
        private static bool IsLeg(Color32 c) =>
            Opaque(c) && (Is(c, 178, 139, 120) || Is(c, 177, 138, 119) || Is(c, 233, 181, 163) || Is(c, 66, 36, 51));

        // The bow: its wood and its grip.
        public static bool IsBow(Color32 c) =>
            Opaque(c) && (Is(c, 113, 65, 59) || Is(c, 187, 117, 71) || Is(c, 142, 82, 82));

        // The magic: pale blues and white. Nothing on the body is that bright and cold.
        public static bool IsMagic(Color32 c) => Opaque(c) && c.b > 200 && c.b >= c.r && c.b >= c.g;

        // The body without the bow and the magic, outline included.
        private static bool IsBody(Color32 c) => Opaque(c) && !IsBow(c) && !IsMagic(c);

        /// <summary>Horizontal centre of the legs, or -1 when the frame shows none.</summary>
        public float LegX(int frame)
        {
            float sum = 0f;
            int count = 0;
            for (int row = 0; row <= LegRowMax; row++)
            {
                for (int x = 0; x < frameWidth; x++)
                {
                    if (!IsLeg(Get(frame, x, row))) continue;
                    sum += x;
                    count++;
                }
            }
            return count > 0 ? sum / count : -1f;
        }

        /// <summary>Bounds of the body on one frame (no bow, no magic). Empty RectInt when there is none.</summary>
        public RectInt BodyBounds(int frame) => Bounds(frame, frame, 0, IsBody);

        /// <summary>Opaque bounds over frames [first, last], only counting columns at or right of minX.</summary>
        public RectInt OpaqueBounds(int first, int last, int minX = 0) => Bounds(first, last, minX, Opaque);

        /// <summary>Bounds of the magic drawn on frames [first, last], only counting columns at or right of minX.</summary>
        public RectInt MagicBounds(int first, int last, int minX = 0) => Bounds(first, last, minX, IsMagic);

        /// <summary>Bounds of the bow drawn on frames [first, last], only counting columns at or right of minX.</summary>
        public RectInt BowBounds(int first, int last, int minX = 0) => Bounds(first, last, minX, IsBow);

        /// <summary>Centre of the opaque pixels of frames [first, last], in pixels from the frame's bottom left.</summary>
        public Vector2 OpaqueCentre(int first, int last)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            for (int f = first; f <= last; f++)
            {
                for (int row = 0; row < Height; row++)
                {
                    for (int x = 0; x < frameWidth; x++)
                    {
                        if (!Opaque(Get(f, x, row))) continue;
                        sum += new Vector2(x, row);
                        count++;
                    }
                }
            }
            return count > 0 ? sum / count : new Vector2(frameWidth * 0.5f, Height * 0.5f);
        }

        private RectInt Bounds(int first, int last, int minX, Func<Color32, bool> include)
        {
            int xMin = int.MaxValue, xMax = -1, rowMin = int.MaxValue, rowMax = -1;
            for (int f = first; f <= last; f++)
            {
                for (int row = 0; row < Height; row++)
                {
                    for (int x = Mathf.Max(0, minX); x < frameWidth; x++)
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
        if (!sheet.pivotFromAllFrames)
        {
            float first = sheet.pixels.LegX(0);
            return Mathf.Round(first >= 0f ? first : FrameSize * 0.5f);
        }

        float sum = 0f;
        int count = 0;
        for (int f = 0; f < sheet.frames; f++)
        {
            float x = sheet.pixels.LegX(f);
            if (x < 0f) continue;
            sum += x;
            count++;
        }
        return Mathf.Round(count > 0 ? sum / count : FrameSize * 0.5f);
    }

    // Grid-slices the sheet (keeping the existing sprite IDs), and puts every frame's pivot between the legs at the
    // feet, so all clips line up with each other and the prefab root sits on the floor.
    private static void ImportSheet(Sheet sheet)
    {
        sheet.pixels = new Pixels(sheet.AssetPath, FrameSize);
        sheet.frames = sheet.pixels.Frames;
        sheet.pivotPx = ComputePivot(sheet);

        Vector2 pivot = new Vector2(sheet.pivotPx / FrameSize, 0f);
        var extra = new List<(string name, Rect rect, Vector2 pivot)>();

        // Head crop of the first idle frame for the boss bar and dialogs: the hood and the face.
        if (sheet.file == "Idle")
        {
            const int size = 28;
            RectInt body = sheet.pixels.BodyBounds(0);
            int top = (body.width > 0 ? body.yMax : sheet.pixels.OpaqueBounds(0, 0).yMax) + 1;
            int x = Mathf.Clamp(Mathf.RoundToInt(sheet.pivotPx) - size / 2, 0, FrameSize - size);
            int y = Mathf.Clamp(top - size, 0, FrameSize - size);
            extra.Add((PortraitSpriteName, new Rect(x, y, size, size), new Vector2(0.5f, 0.5f)));
        }

        sheet.sprites = SliceGrid(sheet.AssetPath, sheet.file, sheet.frames, FrameSize, FrameSize, PixelsPerUnit, pivot, extra);
        Report.Add($"{sheet.file}: {sheet.frames} frames of {FrameSize}px, pivot x={sheet.pivotPx}px (feet), clip {sheet.clipName}");
    }

    // The head of the shot sits on the pivot: that is where the collider is and what the shot is aimed with.
    private static void ImportChargeSheet(ChargeSheet charge)
    {
        var pixels = new Pixels(charge.AssetPath, ChargeFrameWidth);
        charge.frames = pixels.Frames;

        int lastFly = charge.flyFirst + charge.flyFrames - 1;
        RectInt flight = pixels.OpaqueBounds(charge.flyFirst, lastFly);
        Vector2 centre = pixels.OpaqueCentre(charge.flyFirst, lastFly);
        float headX = flight.width > 0 ? flight.xMax - 4f : ChargeFrameWidth * 0.5f;
        Vector2 pivot = new Vector2(headX / ChargeFrameWidth, centre.y / pixels.Height);

        charge.sprites = SliceGrid(charge.AssetPath, charge.file, charge.frames, ChargeFrameWidth, ChargeFrameHeight, ChargePixelsPerUnit, pivot, null);
        Report.Add($"{charge.file}: {charge.frames} frames of {ChargeFrameWidth}x{ChargeFrameHeight}px " +
                   $"(flying {charge.flyFirst}-{lastFly}, impact {charge.ImpactFrames}), pivot {pivot.x * ChargeFrameWidth:0},{pivot.y * ChargeFrameHeight:0}px");
    }

    /// <summary>
    /// One sprite per grid cell named "{file}_{i}", plus any extra rects. Rects that already carry those names keep
    /// their sprite IDs (so clips that point at them survive a rebuild); every other rect is dropped, which also
    /// clears what Unity's automatic slicing left behind (effects cut out as separate sprites).
    /// </summary>
    private static Sprite[] SliceGrid(string assetPath, string file, int frames, int frameWidth, int frameHeight, float pixelsPerUnit,
        Vector2 pivot, List<(string name, Rect rect, Vector2 pivot)> extra)
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
        for (int i = 0; i < frames; i++)
        {
            SpriteRect rect = FindOrCreateRect(existing, pairs, $"{file}_{i}");
            rect.rect = new Rect(i * frameWidth, 0f, frameWidth, frameHeight);
            rect.alignment = SpriteAlignment.Custom;
            rect.pivot = pivot;
            rect.border = Vector4.zero;
            rects.Add(rect);
        }

        if (extra != null)
        {
            foreach ((string name, Rect area, Vector2 extraPivot) in extra)
            {
                SpriteRect rect = FindOrCreateRect(existing, pairs, name);
                rect.rect = area;
                rect.alignment = SpriteAlignment.Custom;
                rect.pivot = extraPivot;
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

    // Same pattern as Medusa.controller and Kitsune.controller: one state per clip and no transitions, because the
    // enemy states pick the clip in code (Animator.Play) through EnemyStateDriver.
    private static AnimatorController BuildController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
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

    // Flight loops until EnemyProjectile plays the impact, which it waits out (impactDuration) before destroying the
    // shot. A sheet without impact frames gets no Impact state, and the shot simply vanishes when it lands.
    private static void BuildChargeAnimation(ChargeSheet charge)
    {
        charge.flyClip = BuildClip($"{charge.prefabName}_Fly", charge.sprites, charge.flyFirst, charge.flyFrames, charge.flyFps, true);
        if (charge.ImpactFrames > 0)
            charge.impactClip = BuildClip($"{charge.prefabName}_Impact", charge.sprites, charge.ImpactFirst, charge.ImpactFrames, charge.impactFps, false);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(charge.ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(charge.ControllerPath);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState fly = SetState(machine, "Fly", charge.flyClip, new Vector3(300f, 40f, 0f));
        if (charge.impactClip != null) SetState(machine, "Impact", charge.impactClip, new Vector3(300f, 120f, 0f));
        machine.defaultState = fly;

        EditorUtility.SetDirty(controller);
        charge.controller = controller;
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

    // Level3 is the last boss, and without any upgrade answer he is already stronger than the Kitsune fully adapted
    // to the second row (Lamina Estelar, Nucleo Vital, Propulsores): she tops out at 250 x1.75 = 437.5 health, her
    // strongest hit at 24 x1.12 = 26.9 and speed 3.4 x1.04 = 3.54. The attacks below carry the damage.
    private static EnemyScriptableObject BuildStats()
    {
        return LoadOrCreate<EnemyScriptableObject>(StatsPath, stats =>
        {
            stats.moveSpeed = 3.7f;
            stats.maxHealth = 480f;
            stats.damage = 28f;        // only used by the legacy attacks; the attack assets carry their own
            stats.knockbackForce = 25f;
        });
    }

    private static Entity_SO BuildEntity(Sprite portrait)
    {
        Entity_SO entity = LoadOrCreate<Entity_SO>(EntityPath, e =>
        {
            e.entityName = "O Mago";
            e.entitySprite = portrait;
        });

        if (entity.entitySprite == null && portrait != null)
        {
            entity.entitySprite = portrait;
            EditorUtility.SetDirty(entity);
            Report.Add($"{EntityPath}: portrait set");
        }
        return entity;
    }

    // One response per upgrade paid in Esmeralda (the third row of the vendor's trees), balanced by the same "half
    // the advantage" rule as the other bosses: damage -> health, life -> damage, speed -> speed.
    private static BossUpgradeScaling_SO BuildScaling()
    {
        BossUpgradeScaling_SO existing = AssetDatabase.LoadAssetAtPath<BossUpgradeScaling_SO>(ScalingPath);
        if (existing != null) return existing;

        return LoadOrCreate<BossUpgradeScaling_SO>(ScalingPath, scaling =>
        {
            scaling.compensation = 0.5f;
            foreach (UpgradeNode_SO node in FindNodesPaidWith(EmeraldItemId))
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
                           $"Mago {BossUpgradeScaling_SO.Describe(response.bossStat, response.multiplier)} (x{response.multiplier})");
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

    // Level3's boss data keeps its asset name, bossId, reward task and scene (saves and the boss selector depend on
    // them); only the face and the adaptation change. A different entity or adaptation set by hand is left alone.
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
                Report.Add($"{BossDataPath}: entity -> Mago (bossId '{data.bossId}' unchanged)");
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
    private static EnemyProjectile BuildProjectilePrefab(ChargeSheet charge)
    {
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(charge.PrefabPath) != null;
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(charge.PrefabPath) : new GameObject(charge.prefabName);

        try
        {
            var body = GetOrAdd<Rigidbody2D>(root);
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var collider = GetOrAdd<CircleCollider2D>(root);
            collider.isTrigger = true;
            if (!exists) collider.radius = charge.colliderRadius;

            Transform visual = GetOrCreateChild(root.transform, "Visual", Vector3.zero);
            var renderer = GetOrAdd<SpriteRenderer>(visual.gameObject);
            renderer.sprite = charge.sprites[charge.flyFirst];
            renderer.sortingOrder = 2; // in front of the boss (1), so a shot leaving his hand is never hidden
            Material spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
            if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;

            var animator = GetOrAdd<Animator>(visual.gameObject);
            animator.runtimeAnimatorController = charge.controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var projectile = GetOrAdd<EnemyProjectile>(root);
            var so = new SerializedObject(projectile);
            if (!exists) CopyProjectileBasics(so);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("impactState").stringValue = charge.impactClip != null ? "Impact" : "";
            so.FindProperty("impactDuration").floatValue = Round2(charge.ImpactDuration);
            so.FindProperty("alignToDirection").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, charge.PrefabPath);
            Report.Add($"{charge.PrefabPath} {(exists ? "updated" : "created")}: {charge.flyFrames} flying frames, impact {Round2(charge.ImpactDuration)}s");
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

    private static EnemyAttackSet BuildAttackSet(EnemyProjectile arrow, EnemyProjectile sphere)
    {
        ProjectileAttackDefinition arrowAttack = BuildAttack<ProjectileAttackDefinition>("Mage_MagicArrow", "Magic_arrow",
            (d, s) => InitMagicArrow(d, s, arrow));
        ProjectileAttackDefinition sphereAttack = BuildAttack<ProjectileAttackDefinition>("Mage_MagicSphere", "Magic_sphere",
            (d, s) => InitMagicSphere(d, s, sphere));

        return LoadOrCreate<EnemyAttackSet>(AttackSetPath, set =>
        {
            set.attacks = new List<EnemyAttackDefinition> { arrowAttack, sphereAttack };
            set.globalCooldown = 0.5f;
            set.repeatPenalty = 0.35f;
            set.secondRepeatPenalty = 0.75f;
            set.tiebreakWindow = 0.15f;
            set.rangeEdgeScore = 0.6f;
            set.closeRange = 3f;
            // Phase 1: the arrow and the sphere, both from a distance. Phase 2 (half health) opens with the sphere,
            // the arrow and the sphere become fans of three, the sphere is cast twice, and every attack comes back faster.
            set.phases = new List<BossPhase>
            {
                new() { name = "Fase 1", healthFraction = 1f },
                new()
                {
                    name = "Furia Arcana", healthFraction = 0.5f, cooldownMultiplier = 0.75f, globalCooldownMultiplier = 0.7f,
                    openingAttack = sphereAttack
                }
            };
            // He keeps his distance: backs away while the player is closer than his attacks want.
            set.allowRetreat = true;
            set.maxRetreatTime = 1.6f;
            set.retreatSpeedMultiplier = 1f;
            set.runDistance = 12f;
            set.runSpeedMultiplier = 1.4f;
            set.holdTolerance = 0.5f;
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

    // Where a shot leaves on that sheet, from the root (x forward, y up from the feet), measured from the art.
    private static Vector2 ToLocal(Sheet sheet, RectInt px, Vector2 fallback)
    {
        if (px.width <= 0) return fallback;
        return new Vector2(Round2((px.x + px.width * 0.5f - sheet.pivotPx) / PixelsPerUnit), Round2((px.y + px.height * 0.5f) / PixelsPerUnit));
    }

    // Magic_arrow: he raises the bow (f0-1) and draws the string to his chin (f2-5). The sheet has no release
    // frame, so the shot leaves on the open-armed frame (f1, the string slack again) and he lowers the bow (f0).
    // The arrow leaves from the middle of the bow, measured on the fully drawn frame.
    private static void InitMagicArrow(ProjectileAttackDefinition d, Sheet s, EnemyProjectile arrow)
    {
        d.displayName = "Flecha Magica";
        d.segments = new List<AttackSegment>
        {
            Segment("ergue o arco", AttackSegmentKind.WindUp, 0, 1, 0.3f),
            Segment("puxa a corda", AttackSegmentKind.WindUp, 2, 5, 0.55f),
            Segment("solta", AttackSegmentKind.Active, 1, 1, 0.12f),
            Segment("abaixa o arco", AttackSegmentKind.Recovery, 0, 0, 0.3f)
        };
        d.projectilePrefab = arrow;
        d.useSpawnOffset = true;
        // Past the belt (same browns as the bow), so only the bow itself is measured.
        d.spawnOffset = ToLocal(s, s.pixels.BowBounds(5, 5, Mathf.RoundToInt(s.pivotPx) + 10), new Vector2(1.3f, 1.5f));
        d.speed = 15f;
        d.damage = 28f;
        d.knockback = 18f;
        d.count = 1;
        d.extraProjectilesPerPhase = 2; // second phase: a fan of three arrows
        d.spreadAngle = 16f;
        d.aimAtTarget = true;
        d.maxAimAngle = 35f;
        d.minRange = 3.5f;
        d.maxRange = 20f;
        d.minTargetHeight = -3f;
        d.maxTargetHeight = 7f;
        d.requiresLineOfSight = true;
        d.cooldown = 2f;
        d.scoring = new AttackScoring
        {
            weight = 1.2f, targetRetreating = 1.3f, targetGrappling = 1.4f, targetAirborne = 1.2f
        };
        d.telegraphColor = new Color(0.6f, 0.75f, 1f, 1f);
    }

    // Magic_sphere: he reaches out (f0-4) and a sphere of magic gathers in front of his palm (f5-11, the long
    // telegraph), then he swings his cape and releases it (f12) and lowers the arm (f13-15). Slower and harder than
    // the arrow. The sphere leaves from where it is drawn on the release frame.
    private static void InitMagicSphere(ProjectileAttackDefinition d, Sheet s, EnemyProjectile sphere)
    {
        d.displayName = "Esfera Magica";
        d.segments = new List<AttackSegment>
        {
            Segment("estende a mao", AttackSegmentKind.WindUp, 0, 4, 0.4f),
            Segment("concentra a esfera", AttackSegmentKind.WindUp, 5, 11, 0.6f),
            Segment("lanca", AttackSegmentKind.Active, 12, 12, 0.12f),
            Segment("recupera", AttackSegmentKind.Recovery, 13, 15, 0.4f)
        };
        d.recoveryHoldTime = 0.2f;
        d.projectilePrefab = sphere;
        d.useSpawnOffset = true;
        d.spawnOffset = ToLocal(s, s.pixels.MagicBounds(12, 12, Mathf.RoundToInt(s.pivotPx)), new Vector2(1.6f, 1.8f));
        d.speed = 8f;
        d.damage = 32f;
        d.knockback = 26f;
        d.count = 1;
        d.extraProjectilesPerPhase = 2; // second phase: a fan of three spheres...
        d.extraCastsPerPhase = 1;       // ...cast twice in a row
        d.recastFromSegment = 0;
        d.spreadAngle = 24f;
        d.aimAtTarget = true;
        d.maxAimAngle = 30f;
        d.minRange = 3f;
        d.maxRange = 16f;
        d.minTargetHeight = -3f;
        d.maxTargetHeight = 6f;
        d.requiresLineOfSight = true;
        d.cooldown = 3.5f;
        d.scoring = new AttackScoring
        {
            weight = 1f, enragedMultiplier = 1.2f, targetAttacking = 1.2f, targetAirborne = 0.8f
        };
        d.telegraphColor = new Color(0.55f, 0.7f, 1f, 1f);
    }

    #endregion

    #region Prefab

    // A new prefab, built like Kitsune.prefab. An existing Mage prefab is edited in place (LoadPrefabContents) so the
    // scene's references to its components survive.
    private static void BuildPrefab(AnimatorController controller, EnemyScriptableObject stats, EnemyAttackSet attackSet, EnemyProjectile sphere)
    {
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Mage");

        try
        {
            root.layer = LayerMask.NameToLayer("AttackableLayer"); // what the player's attack hits
            root.tag = "Inimigo"; // the player slides off anything with this tag instead of standing on it

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

            // Body collider fitted to the idle pose: the robe, as tall as he stands, not the bow beside him.
            Sheet idle = GetSheet("Idle");
            RectInt idleBody = idle.pixels.BodyBounds(0);
            var collider = GetOrAdd<BoxCollider2D>(root);
            float colliderHeight = idleBody.yMax * 0.9f / PixelsPerUnit;
            float colliderWidth = Mathf.Clamp(idleBody.width * 0.7f, 12f, 24f) / PixelsPerUnit;
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

            // Hand: where the sphere is released. Each attack has its own spawn offset; this one is the line-of-sight
            // eye and the fallback.
            Sheet cast = GetSheet("Magic_sphere");
            Vector2 hand = ToLocal(cast, cast.pixels.MagicBounds(12, 12, Mathf.RoundToInt(cast.pivotPx)), new Vector2(1.6f, 1.8f));
            Vector3 handLocal = new Vector3(hand.x, hand.y, 0f);
            Transform handPoint = GetOrCreateChild(root.transform, "HandPoint", handLocal);
            handPoint.localPosition = handLocal;

            var driver = GetOrAdd<EnemyStateDriver>(root);
            var so = new SerializedObject(driver);
            so.FindProperty("data").objectReferenceValue = stats;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("attackSet").objectReferenceValue = attackSet;
            so.FindProperty("projectilePrefab").objectReferenceValue = sphere;
            so.FindProperty("projectileSpawnPoint").objectReferenceValue = handPoint;
            so.FindProperty("groundCheck").objectReferenceValue = groundCheck;
            so.FindProperty("groundLayer").intValue = LayerMask.GetMask("Ground");
            so.FindProperty("tintRenderer").objectReferenceValue = renderer;
            so.FindProperty("hurtbox").objectReferenceValue = hurtbox;

            BossData_SO bossData = AssetDatabase.LoadAssetAtPath<BossData_SO>(BossDataPath);
            if (so.FindProperty("bossData").objectReferenceValue == null) so.FindProperty("bossData").objectReferenceValue = bossData;

            Dialog_SO intro = AssetDatabase.LoadAssetAtPath<Dialog_SO>(IntroDialogPath);
            if (so.FindProperty("introDialog").objectReferenceValue == null)
            {
                so.FindProperty("introDialog").objectReferenceValue = intro;
                if (intro == null) Report.Add($"WARNING: {IntroDialogPath} not found, the fight starts without the intro");
            }

            SerializedProperty retryLines = so.FindProperty("retryIntroLines");
            if (retryLines.arraySize == 0)
            {
                retryLines.arraySize = RetryIntroLines.Length;
                for (int i = 0; i < RetryIntroLines.Length; i++)
                    retryLines.GetArrayElementAtIndex(i).stringValue = RetryIntroLines[i];
                Report.Add($"{PrefabPath}: {RetryIntroLines.Length} rematch lines set");
            }

            so.FindProperty("skipIntroAfterVictory").boolValue = true;

            if (!exists)
            {
                so.FindProperty("detectionRange").floatValue = 30f; // the whole arena
                so.FindProperty("attackRange").floatValue = 3f;
                so.FindProperty("stopDistance").floatValue = 1.2f;
                so.FindProperty("waitForActivation").boolValue = true;
                so.FindProperty("superArmorWhileAttacking").boolValue = true;
                so.FindProperty("groundCheckRadius").floatValue = 0.2f;
                so.FindProperty("invulnerabilityTime").floatValue = 0.15f;
                so.FindProperty("hitAnimationTime").floatValue = GetSheet("Hurt").frames / GetSheet("Hurt").fps;
                so.FindProperty("deathDelay").floatValue = GetSheet("Dead").frames / GetSheet("Dead").fps + 0.8f;
                so.FindProperty("logStatePath").boolValue = false;

                // After two hits (in a row or not) he teleports back to keep his distance.
                SerializedProperty dash = so.FindProperty("dash");
                dash.FindPropertyRelative("hitsToDash").intValue = 2;
                dash.FindPropertyRelative("distance").floatValue = 5f;
                dash.FindPropertyRelative("duration").floatValue = 0.3f; // hidden between the two bursts
                dash.FindPropertyRelative("reappearTime").floatValue = 0.2f;
                dash.FindPropertyRelative("cooldown").floatValue = 3f;
                dash.FindPropertyRelative("invulnerable").boolValue = true;
            }

            // How he escapes is part of who he is, not tuning: always a teleport in a burst of blue lightning.
            SerializedProperty escape = so.FindProperty("dash");
            escape.FindPropertyRelative("enabled").boolValue = true;
            escape.FindPropertyRelative("teleport").boolValue = true;
            so.FindProperty("teleportSparks").FindPropertyRelative("enabled").boolValue = true;

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
    // driver moves it with the drawn body during attacks. Created once; an existing one keeps its size.
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

    #region Level3

    // Replaces the placeholder boss the Level3 BossGate points at with a Mage instance at the same spot, rewires the
    // gate, and deletes the placeholder. Everything else (barrier, camera, task, boss bar, progression, Daisy) goes
    // through the gate or the driver's BossData, so it follows automatically.
    private static void SwapLevel3Boss()
    {
        Scene scene = EditorSceneManager.OpenScene(Level3ScenePath, OpenSceneMode.Single);

        // Loaded again after the scene switch: OpenScene unloads assets only this code was holding.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        BossGate gate = Object.FindObjectsByType<BossGate>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(g => g.gameObject.scene == scene);
        if (gate == null)
        {
            Report.Add("WARNING: no BossGate in Level3, the boss was not swapped");
            return;
        }

        var gateSo = new SerializedObject(gate);
        SerializedProperty bossProperty = gateSo.FindProperty("boss");
        var oldBoss = bossProperty.objectReferenceValue as EnemyStateDriver;

        if (oldBoss != null && PrefabUtility.GetCorrespondingObjectFromOriginalSource(oldBoss.gameObject) == prefab)
        {
            Report.Add("Level3 already uses the Mage prefab; scene left as is");
            return;
        }

        Vector3 position = oldBoss != null ? oldBoss.transform.position : gate.transform.position;
        position.y = FindFloor(position, oldBoss);

        var mage = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        mage.name = "Mago (Boss)";
        if (oldBoss != null)
        {
            mage.transform.SetParent(oldBoss.transform.parent, false);
            mage.transform.SetSiblingIndex(oldBoss.transform.GetSiblingIndex());
        }
        // Faces the arena entrance (the gate trigger side) until the fight starts and he turns to the player.
        mage.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

        bossProperty.objectReferenceValue = mage.GetComponent<EnemyStateDriver>();
        gateSo.ApplyModifiedPropertiesWithoutUndo();

        if (oldBoss != null)
        {
            GameObject oldRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(oldBoss.gameObject);
            string oldName = (oldRoot != null ? oldRoot : oldBoss.gameObject).name;
            Object.DestroyImmediate(oldRoot != null ? oldRoot : oldBoss.gameObject);
            Report.Add($"Level3: '{oldName}' replaced by the Mage at {position}, BossGate.boss rewired");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // The floor under the old boss, so the Mage's feet (his pivot) start on the ground.
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
