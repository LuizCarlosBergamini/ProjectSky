using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// "Turned to stone" on the player: no input, frozen animation, grey tint, for a few seconds. Added on
    /// demand by an enemy's gaze attack, but it lives on the player so it always undoes itself - on time,
    /// or when disabled/destroyed - even if the enemy that caused it dies first.
    /// </summary>
    [DisallowMultipleComponent]
    public class PetrifyStatus : MonoBehaviour
    {
        [SerializeField] private Color stoneTint = new Color(0.55f, 0.55f, 0.6f, 1f);

        private PlayerStateDriver player;
        private Animator playerAnimator;
        private SpriteRenderer[] renderers;
        private Color[] originalColors;
        private float originalAnimatorSpeed = 1f;
        private float remaining;
        private float immunity;
        private float immuneUntil;
        private bool active;

        public bool IsPetrified => active;

        /// <summary>True while target carries an active petrification.</summary>
        public static bool IsActiveOn(GameObject target)
        {
            return target != null && target.TryGetComponent(out PetrifyStatus status) && status.active;
        }

        /// <summary>
        /// Petrifies target for duration seconds, followed by immunitySeconds of immunity.
        /// False when it is already petrified, still immune, or duration is zero.
        /// </summary>
        public static bool TryApply(GameObject target, float duration, float immunitySeconds)
        {
            if (target == null || duration <= 0f) return false;

            if (!target.TryGetComponent(out PetrifyStatus status)) status = target.AddComponent<PetrifyStatus>();
            return status.Apply(duration, immunitySeconds);
        }

        private bool Apply(float duration, float immunitySeconds)
        {
            if (active || Time.time < immuneUntil) return false;

            player = GetComponent<PlayerStateDriver>();
            playerAnimator = player != null && player.animator != null ? player.animator : GetComponentInChildren<Animator>();
            renderers = GetComponentsInChildren<SpriteRenderer>();
            originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                originalColors[i] = renderers[i].color;
                renderers[i].color = originalColors[i] * stoneTint;
            }

            if (playerAnimator != null)
            {
                originalAnimatorSpeed = playerAnimator.speed;
                playerAnimator.speed = 0f;
            }

            if (player != null) player.SetInputEnabled(false);

            remaining = duration;
            immunity = immunitySeconds;
            active = true;
            return true;
        }

        private void Update()
        {
            if (!active) return;

            remaining -= Time.deltaTime;
            if (remaining <= 0f) Release();
        }

        private void OnDisable()
        {
            if (active) Release();
        }

        private void Release()
        {
            active = false;
            immuneUntil = Time.time + immunity;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null) renderers[i].color = originalColors[i];
            }

            if (playerAnimator != null) playerAnimator.speed = originalAnimatorSpeed;
            if (player != null && player.IsAlive()) player.SetInputEnabled(true);
        }
    }
}
