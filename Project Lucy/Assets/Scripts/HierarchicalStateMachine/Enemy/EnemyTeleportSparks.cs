using System;
using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// Blue lightning an enemy throws where it vanishes and where it reappears when it teleports (EnemyDashSettings
    /// with teleport on): a burst of short, stretched sparks that jitter like electricity, plus a few tall bolts
    /// crackling up through the body. Built in code like EnemyHitBlood, so no prefab is needed; tune it in the Inspector.
    /// </summary>
    [Serializable]
    public class EnemyTeleportSparks
    {
        [Tooltip("Liga os raios azuis no teleporte.")]
        public bool enabled = true;

        [Header("Quantidade")]
        [Tooltip("Faiscas por explosao (uma ao sumir, outra ao reaparecer).")]
        [Range(0, 200)] public int sparksPerBurst = 45;
        [Tooltip("Raios altos que sobem pelo corpo em cada explosao.")]
        [Range(0, 30)] public int boltsPerBurst = 7;

        [Header("Movimento")]
        [Tooltip("Velocidade das faiscas saindo do corpo (min, max).")]
        public Vector2 sparkSpeed = new Vector2(4f, 10f);
        [Tooltip("Velocidade dos raios subindo (min, max).")]
        public Vector2 boltSpeed = new Vector2(6f, 12f);
        [Tooltip("Tremida eletrica das particulas. 0 = retas.")]
        [Range(0f, 10f)] public float jitter = 4f;

        [Header("Aparencia")]
        [Tooltip("Espessura das faiscas e dos raios em unidades do mundo (min, max).")]
        public Vector2 sparkSize = new Vector2(0.05f, 0.1f);
        public Vector2 boltSize = new Vector2(0.08f, 0.14f);
        [Tooltip("Quanto as particulas esticam na direcao do movimento.")]
        [Range(0f, 1f)] public float stretch = 0.12f;
        [Tooltip("Cada particula sorteia uma cor entre estas duas.")]
        public Color colorA = new Color(0.45f, 0.72f, 1f, 1f);
        public Color colorB = new Color(0.9f, 0.96f, 1f, 1f);
        [Tooltip("Tempo de vida em segundos (min, max).")]
        public Vector2 lifetime = new Vector2(0.15f, 0.35f);

        [Header("Renderizacao")]
        [Tooltip("Vazio = Sprite-Unlit-Default do URP 2D, que nao e escurecido pela iluminacao.")]
        public Material material;
        [Tooltip("Somado a ordem do sprite do inimigo, para os raios aparecerem na frente dele.")]
        public int sortingOrderOffset = 3;

        private ParticleSystem system;

        public void Init(SpriteRenderer sprite)
        {
            if (!enabled || system != null) return;

            // Built inactive so the modules can be set before the system ever plays.
            var go = new GameObject("TeleportSparks (" + (sprite != null ? sprite.transform.root.name : "Enemy") + ")");
            go.SetActive(false);
            system = go.AddComponent<ParticleSystem>();

            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.maxParticles = 600;
            main.stopAction = ParticleSystemStopAction.Destroy;

            // Nothing is emitted on its own: Play() only keeps the system simulating, Emit() adds the bursts.
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            // Electricity: the sparks lose speed fast and zigzag instead of flying straight.
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1f;
            limit.dampen = 0.25f;

            if (jitter > 0f)
            {
                var noise = system.noise;
                noise.enabled = true;
                noise.strength = jitter;
                noise.frequency = 3f;
                noise.scrollSpeed = 2f;
                noise.quality = ParticleSystemNoiseQuality.Low;
            }

            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = stretch;
            renderer.lengthScale = 1f;
            renderer.sharedMaterial = material != null ? material : DefaultMaterial(sprite);
            if (sprite != null)
            {
                renderer.sortingLayerID = sprite.sortingLayerID;
                renderer.sortingOrder = sprite.sortingOrder + sortingOrderOffset;
            }

            go.SetActive(true);
            system.Play();
        }

        /// <param name="body">The enemy's body in world space: the sparks burst from it and the bolts climb through it.</param>
        public void Emit(Bounds body)
        {
            if (!enabled || system == null) return;

            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            for (int i = 0; i < sparksPerBurst; i++)
            {
                Vector2 from = RandomPoint(body);
                Vector2 outward = (from - (Vector2)body.center).normalized;
                if (outward.sqrMagnitude < 0.01f) outward = UnityEngine.Random.insideUnitCircle.normalized;
                Vector2 direction = (outward + UnityEngine.Random.insideUnitCircle * 0.6f).normalized;
                Fill(ref p, from, direction * Range(sparkSpeed), Range(sparkSize), Range(lifetime));
                system.Emit(p, 1);
            }

            for (int i = 0; i < boltsPerBurst; i++)
            {
                Vector2 from = new Vector2(UnityEngine.Random.Range(body.min.x, body.max.x), body.min.y);
                Vector2 direction = new Vector2(UnityEngine.Random.Range(-0.25f, 0.25f), 1f).normalized;
                Fill(ref p, from, direction * Range(boltSpeed), Range(boltSize), Range(lifetime) * 1.2f);
                system.Emit(p, 1);
            }
        }

        /// <summary>
        /// Called when the enemy goes away. The system lives at the scene root so a burst is not destroyed with the
        /// body; it removes itself once its last spark is gone.
        /// </summary>
        public void Release()
        {
            if (system == null) return;
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            system = null;
        }

        private static Vector2 RandomPoint(Bounds b) =>
            new Vector2(UnityEngine.Random.Range(b.min.x, b.max.x), UnityEngine.Random.Range(b.min.y, b.max.y));

        private Color RandomColor() => Color.Lerp(colorA, colorB, UnityEngine.Random.value);

        private static float Range(Vector2 range) => UnityEngine.Random.Range(range.x, range.y);

        private void Fill(ref ParticleSystem.EmitParams p, Vector2 position, Vector2 velocity, float size, float life)
        {
            p.position = position;
            p.velocity = velocity;
            p.startSize = size;
            p.startColor = RandomColor();
            p.startLifetime = life;
        }

        // The URP 2D renderer always ships its unlit sprite material, so this shader is in every build.
        private static Material sharedDefault;

        private static Material DefaultMaterial(SpriteRenderer sprite)
        {
            if (sharedDefault != null) return sharedDefault;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null) sharedDefault = new Material(shader) { name = "TeleportSparks (runtime)" };
            else if (sprite != null) sharedDefault = sprite.sharedMaterial;
            return sharedDefault;
        }
    }
}
