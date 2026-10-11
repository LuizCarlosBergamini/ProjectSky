using System;
using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// Blood spray an enemy throws when hit: a burst of droplets flying away from the attacker, a few
    /// bigger blobs, and a short mist puff, falling under gravity and landing on the ground. The particle
    /// system is built in code, so every enemy gets it without a prefab; tune it from the Inspector.
    /// </summary>
    [Serializable]
    public class EnemyHitBlood
    {
        [Tooltip("Liga o jato de sangue em particulas ao levar dano.")]
        public bool enabled = true;

        [Header("Quantidade")]
        [Tooltip("Gotas por golpe.")]
        [Range(0, 200)] public int dropletsPerHit = 28;
        [Tooltip("Gotas grandes por golpe (mais lentas, caem logo).")]
        [Range(0, 50)] public int blobsPerHit = 6;
        [Tooltip("Particulas de nevoa por golpe (grandes, quase paradas, somem rapido).")]
        [Range(0, 30)] public int mistPerHit = 5;
        [Tooltip("Multiplica todas as quantidades no golpe que mata.")]
        [Range(1f, 5f)] public float killMultiplier = 2.5f;

        [Header("Direcao e velocidade")]
        [Tooltip("Abertura total do jato, em graus, centrada na direcao oposta ao atacante.")]
        [Range(0f, 360f)] public float sprayAngle = 70f;
        [Tooltip("Inclina o jato para cima, em graus.")]
        [Range(-45f, 90f)] public float upwardTilt = 20f;
        public Vector2 dropletSpeed = new Vector2(4f, 11f);
        public Vector2 blobSpeed = new Vector2(2f, 5f);
        [Tooltip("Fracao das gotas lancadas para o lado do atacante (respingo de volta).")]
        [Range(0f, 1f)] public float backSplash = 0.2f;

        [Header("Aparencia")]
        [Tooltip("Tamanho das gotas em unidades do mundo (min, max).")]
        public Vector2 dropletSize = new Vector2(0.06f, 0.14f);
        public Vector2 blobSize = new Vector2(0.16f, 0.28f);
        public Vector2 mistSize = new Vector2(0.5f, 0.9f);
        [Tooltip("Cada particula sorteia uma cor entre estas duas.")]
        public Color colorA = new Color(0.85f, 0.04f, 0.04f, 1f);
        public Color colorB = new Color(0.45f, 0f, 0.02f, 1f);
        public Color mistColor = new Color(0.7f, 0.02f, 0.02f, 0.45f);
        [Tooltip("Tempo de vida das gotas em segundos (min, max).")]
        public Vector2 lifetime = new Vector2(0.45f, 0.9f);
        public float mistLifetime = 0.25f;
        [Tooltip("Forca da gravidade sobre as gotas (1 = gravidade do projeto).")]
        public float gravity = 2.5f;

        [Header("Chao")]
        [Tooltip("As gotas param no chao (layer Ground do inimigo) em vez de atravessa-lo.")]
        public bool collideWithGround = true;

        [Header("Renderizacao")]
        [Tooltip("Vazio = Sprite-Unlit-Default do URP 2D, que nao e escurecido pela iluminacao.")]
        public Material material;
        [Tooltip("Somado a ordem do sprite do inimigo, para o sangue aparecer na frente dele.")]
        public int sortingOrderOffset = 2;

        private ParticleSystem system;

        public void Init(SpriteRenderer sprite, LayerMask ground)
        {
            if (!enabled || system != null) return;

            // Built inactive so the modules can be set before the system ever plays.
            var go = new GameObject("HitBlood (" + (sprite != null ? sprite.transform.root.name : "Enemy") + ")");
            go.SetActive(false);
            system = go.AddComponent<ParticleSystem>();

            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = 1000;
            main.stopAction = ParticleSystemStopAction.Destroy;

            // Nothing is emitted on its own: Play() only keeps the system simulating, Emit() adds the hits.
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1f, 0.2f)));

            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            if (collideWithGround && ground.value != 0)
            {
                var collision = system.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision2D;
                collision.collidesWith = ground;
                collision.dampen = 0.9f;
                collision.bounce = 0.05f;
                collision.radiusScale = 0.5f;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material != null ? material : DefaultMaterial(sprite);
            if (sprite != null)
            {
                renderer.sortingLayerID = sprite.sortingLayerID;
                renderer.sortingOrder = sprite.sortingOrder + sortingOrderOffset;
            }

            go.SetActive(true);
            system.Play();
        }

        /// <param name="point">Where the hit landed, in world space.</param>
        /// <param name="direction">Horizontal side the blood flies to: +1 right, -1 left.</param>
        public void Emit(Vector2 point, float direction, bool killingBlow)
        {
            if (!enabled || system == null) return;

            float scale = killingBlow ? killMultiplier : 1f;
            float side = direction >= 0f ? 1f : -1f;

            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            int droplets = Mathf.RoundToInt(dropletsPerHit * scale);
            for (int i = 0; i < droplets; i++)
            {
                float s = UnityEngine.Random.value < backSplash ? -side : side;
                float speed = Range(dropletSpeed) * (s == side ? 1f : 0.5f);
                Fill(ref p, point, SprayDirection(s) * speed, Range(dropletSize), RandomColor(), Range(lifetime));
                system.Emit(p, 1);
            }

            int blobs = Mathf.RoundToInt(blobsPerHit * scale);
            for (int i = 0; i < blobs; i++)
            {
                Fill(ref p, point, SprayDirection(side) * Range(blobSpeed), Range(blobSize), RandomColor(), Range(lifetime));
                system.Emit(p, 1);
            }

            int mist = Mathf.RoundToInt(mistPerHit * scale);
            for (int i = 0; i < mist; i++)
            {
                // Mist drifts up a little so it hangs in the air for its short life instead of dropping like the droplets.
                Vector2 drift = new Vector2(side * UnityEngine.Random.Range(0.3f, 1.2f), UnityEngine.Random.Range(0.5f, 1.5f));
                Fill(ref p, point + UnityEngine.Random.insideUnitCircle * 0.15f, drift, Range(mistSize), mistColor,
                    mistLifetime * UnityEngine.Random.Range(0.8f, 1.2f));
                system.Emit(p, 1);
            }
        }

        /// <summary>
        /// Called when the enemy goes away. The system lives at the scene root so a death burst is not
        /// destroyed with the body; it removes itself once its last droplet is gone.
        /// </summary>
        public void Release()
        {
            if (system == null) return;
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            system = null;
        }

        private Vector2 SprayDirection(float side)
        {
            float angle = upwardTilt + UnityEngine.Random.Range(-sprayAngle, sprayAngle) * 0.5f;
            float rad = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad) * side, Mathf.Sin(rad));
        }

        private Color RandomColor() => Color.Lerp(colorA, colorB, UnityEngine.Random.value);

        private static float Range(Vector2 range) => UnityEngine.Random.Range(range.x, range.y);

        private static void Fill(ref ParticleSystem.EmitParams p, Vector2 position, Vector2 velocity, float size, Color color, float life)
        {
            p.position = position;
            p.velocity = velocity;
            p.startSize = size;
            p.startColor = color;
            p.startLifetime = life;
            p.rotation = UnityEngine.Random.Range(0f, 360f);
        }

        // The URP 2D renderer always ships its unlit sprite material, so this shader is in every build.
        private static Material sharedDefault;

        private static Material DefaultMaterial(SpriteRenderer sprite)
        {
            if (sharedDefault != null) return sharedDefault;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null) sharedDefault = new Material(shader) { name = "HitBlood (runtime)" };
            else if (sprite != null) sharedDefault = sprite.sharedMaterial;
            return sharedDefault;
        }
    }
}
