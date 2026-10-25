using System.Collections.Generic;
using HierarchicalStateMachine;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// In-game version of the grapple range gizmos: a dashed circle around Lucy (her reach) and one around
/// each nearby GrappleableTarget (its grappleRadius). The hook works once the two circles touch, and they
/// fade in a little before that so the player sees the range coming. Added by PlayerStateDriver.
/// </summary>
[DisallowMultipleComponent]
public class GrappleRangeIndicator : MonoBehaviour
{
    [Tooltip("Distancia extra (unidades) alem do alcance real em que os circulos comecam a aparecer.")]
    [SerializeField, Min(0f)] private float indicatorExtraRange = 1.5f;
    [SerializeField] private Color color = new Color(0.2f, 0.9f, 1f, 1f);
    [Tooltip("Alpha dos circulos enquanto a Lucy se aproxima, ainda fora do alcance real.")]
    [SerializeField, Range(0f, 1f)] private float approachAlpha = 0.35f;
    [Tooltip("Alpha dos circulos quando ja da para lancar o gancho.")]
    [SerializeField, Range(0f, 1f)] private float inRangeAlpha = 0.9f;
    [SerializeField, Min(0.01f)] private float fadeInTime = 0.2f;
    [SerializeField, Min(0.01f)] private float fadeOutTime = 0.3f;
    [Tooltip("Giro do circulo da Lucy, em graus por segundo, no sentido horario.")]
    [SerializeField] private float lucySpinSpeed = 30f;
    [SerializeField, Min(4)] private int dashCount = 24;
    [Tooltip("Fracao de cada trecho que e traco (o resto e espaco).")]
    [SerializeField, Range(0.1f, 0.9f)] private float dashFill = 0.5f;
    [SerializeField, Min(0.005f)] private float lineWidth = 0.08f;
    [Tooltip("Opcional. Vazio = Sprite-Unlit-Default do URP 2D, que nao e escurecido pela iluminacao.")]
    [SerializeField] private Material material;
    [Tooltip("Somado ao Order in Layer do sprite da Lucy. Negativo desenha atras dela.")]
    [SerializeField] private int sortingOrderOffset = -1;

    private class Ring
    {
        public LineRenderer line;
        public float radius = -1f;
        public float alpha;
        public float targetAlpha;
        public Vector3 lastPosition;
    }

    private PlayerStateDriver driver;
    private Material ringMaterial;
    private Texture2D dashTexture;
    private Ring lucyRing;
    private readonly Dictionary<GrappleableTarget, Ring> targetRings = new Dictionary<GrappleableTarget, Ring>();
    private readonly Stack<Ring> pool = new Stack<Ring>();
    private readonly List<GrappleableTarget> released = new List<GrappleableTarget>();

    private void Awake()
    {
        driver = GetComponent<PlayerStateDriver>();
    }

    private void OnDisable()
    {
        // Drop everything at once: a disabled player should not leave circles behind.
        if (lucyRing != null) SetAlpha(lucyRing, 0f);
        foreach (Ring ring in targetRings.Values) SetAlpha(ring, 0f);
    }

    private void OnDestroy()
    {
        // The rings live at the scene root (see CreateRing), so they are not destroyed with the player.
        if (lucyRing != null) DestroyRing(lucyRing);
        foreach (Ring ring in targetRings.Values) DestroyRing(ring);
        while (pool.Count > 0) DestroyRing(pool.Pop());
        targetRings.Clear();
        if (ringMaterial != null) Destroy(ringMaterial);
        if (dashTexture != null) Destroy(dashTexture);
    }

    private void LateUpdate()
    {
        if (driver == null) return;
        if (lucyRing == null || lucyRing.line == null) lucyRing = CreateRing();

        float reach = driver.GrappleReach;
        bool hidden = driver.IsGrappling || driver.IsDead || reach <= 0f;
        Vector2 lucyPosition = transform.position;
        float lucyTargetAlpha = 0f;

        foreach (Ring ring in targetRings.Values)
        {
            ring.targetAlpha = 0f;
        }

        if (!hidden)
        {
            var targets = GrappleableTarget.Active;
            int layerMask = driver.GrappleableLayer.value;
            for (int i = 0; i < targets.Count; i++)
            {
                GrappleableTarget target = targets[i];
                if (target == null || (layerMask & (1 << target.gameObject.layer)) == 0) continue;

                // Same rule as PlayerContext.TryFindGrappleTarget: the circles touch.
                float grabDistance = reach + target.grappleRadius;
                float distance = Vector2.Distance(lucyPosition, target.GetAnchorPoint());
                if (distance > grabDistance + indicatorExtraRange) continue;

                float alpha = distance <= grabDistance ? inRangeAlpha : approachAlpha;
                if (!targetRings.TryGetValue(target, out Ring ring))
                {
                    ring = TakeRing();
                    targetRings.Add(target, ring);
                }
                ring.targetAlpha = alpha;
                lucyTargetAlpha = Mathf.Max(lucyTargetAlpha, alpha);
            }
        }

        lucyRing.targetAlpha = lucyTargetAlpha;
        UpdateRing(lucyRing, transform.position, reach);
        if (lucyRing.alpha > 0f)
        {
            // Negative z turns clockwise when looking down the camera's forward axis.
            lucyRing.line.transform.Rotate(0f, 0f, -lucySpinSpeed * Time.deltaTime);
        }

        released.Clear();
        foreach (KeyValuePair<GrappleableTarget, Ring> pair in targetRings)
        {
            GrappleableTarget target = pair.Key;
            Ring ring = pair.Value;
            // A destroyed or disabled target fades out where it was last seen.
            bool alive = target != null && target.isActiveAndEnabled;
            Vector3 position = alive ? (Vector3)target.GetAnchorPoint() : ring.lastPosition;
            float radius = alive ? target.grappleRadius : ring.radius;
            UpdateRing(ring, position, radius);
            if (ring.alpha <= 0f && ring.targetAlpha <= 0f) released.Add(target);
        }

        for (int i = 0; i < released.Count; i++)
        {
            Ring ring = targetRings[released[i]];
            targetRings.Remove(released[i]);
            pool.Push(ring);
        }
    }

    private void UpdateRing(Ring ring, Vector3 position, float radius)
    {
        float fadeTime = ring.targetAlpha > ring.alpha ? fadeInTime : fadeOutTime;
        float speed = Mathf.Max(inRangeAlpha, 0.01f) / fadeTime;
        SetAlpha(ring, Mathf.MoveTowards(ring.alpha, ring.targetAlpha, speed * Time.deltaTime));
        if (ring.alpha <= 0f) return;

        if (ring.line == null) return;
        position.z = 0f;
        ring.line.transform.position = position;
        ring.lastPosition = position;
        if (!Mathf.Approximately(ring.radius, radius)) BuildCircle(ring, radius);
        ApplySorting(ring.line);
    }

    private void SetAlpha(Ring ring, float alpha)
    {
        ring.alpha = alpha;
        if (ring.line == null) return;
        Color c = color;
        c.a = color.a * alpha;
        ring.line.startColor = c;
        ring.line.endColor = c;
        ring.line.enabled = alpha > 0f;
    }

    private void BuildCircle(Ring ring, float radius)
    {
        ring.radius = radius;
        // One point per dash: RepeatPerSegment maps the dash texture once onto each segment, so every
        // segment is a dash followed by its gap. loop closes the last segment back to the first point.
        int count = Mathf.Max(4, dashCount);
        ring.line.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            ring.line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    private void ApplySorting(LineRenderer line)
    {
        SpriteRenderer sprite = driver.animator != null ? driver.animator.GetComponent<SpriteRenderer>() : null;
        if (sprite == null) return;
        line.sortingLayerID = sprite.sortingLayerID;
        line.sortingOrder = sprite.sortingOrder + sortingOrderOffset;
    }

    private Ring CreateRing()
    {
        // Kept at the scene root: the player is scaled 1.5 and flips by rotating 180° on y, which would
        // shrink and mirror a child circle. The rings only copy the position instead.
        var go = new GameObject("GrappleRangeRing");
        // Same scene as the player, so the rings unload with it even if it is moved to DontDestroyOnLoad.
        SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.textureMode = LineTextureMode.RepeatPerSegment;
        line.alignment = LineAlignment.TransformZ;
        line.widthMultiplier = lineWidth;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = GetRingMaterial();
        line.enabled = false;
        return new Ring { line = line };
    }

    private Ring TakeRing()
    {
        while (pool.Count > 0)
        {
            Ring ring = pool.Pop();
            if (ring.line != null) return ring;
        }
        return CreateRing();
    }

    private void DestroyRing(Ring ring)
    {
        if (ring.line != null) Destroy(ring.line.gameObject);
    }

    private Material GetRingMaterial()
    {
        if (ringMaterial != null) return ringMaterial;

        // Sprite-Unlit-Default ships with the URP 2D renderer, so it is in every build and the circles are
        // not darkened by the scene's 2D lights.
        Material source = material;
        Shader shader = source == null ? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") : null;
        if (source == null && shader == null) shader = Shader.Find("Sprites/Default");
        if (source == null && shader == null && driver.RopeRenderer != null) source = driver.RopeRenderer.sharedMaterial;
        ringMaterial = source != null ? new Material(source) : new Material(shader);
        ringMaterial.name = "GrappleRangeRing (runtime)";
        ringMaterial.mainTexture = GetDashTexture();
        ringMaterial.mainTextureScale = Vector2.one;
        ringMaterial.mainTextureOffset = Vector2.zero;
        return ringMaterial;
    }

    private Texture2D GetDashTexture()
    {
        const int width = 32;
        dashTexture = new Texture2D(width, 1, TextureFormat.RGBA32, false);
        dashTexture.name = "GrappleRangeDash (runtime)";
        dashTexture.wrapMode = TextureWrapMode.Repeat;
        dashTexture.filterMode = FilterMode.Point;
        int filled = Mathf.Clamp(Mathf.RoundToInt(width * dashFill), 1, width - 1);
        for (int x = 0; x < width; x++)
        {
            dashTexture.SetPixel(x, 0, x < filled ? Color.white : Color.clear);
        }
        dashTexture.Apply();
        return dashTexture;
    }
}
