using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HierarchicalStateMachine
{
    /// <summary>
    /// Chooses which attack EnemyAttack runs next. Every attack that is valid right now (off cooldown,
    /// unlocked by the phase, the player inside its range and height band, line of sight when it needs it)
    /// gets a score; the best ones share a small weighted draw. Distance is the main driver: the range fit
    /// favours attacks whose band is centred on the current distance.
    /// </summary>
    public class EnemyAttackSelector
    {
        private readonly EnemyContext ctx;
        private readonly EnemyAttackSet set;
        private readonly List<EnemyAttackMove> candidates = new();
        private readonly List<float> scores = new();
        private readonly StringBuilder log = new();

        private EnemyAttackMove last;
        private EnemyAttackMove beforeLast;

        public EnemyAttackSelector(EnemyContext ctx, EnemyAttackSet set)
        {
            this.ctx = ctx;
            this.set = set;
        }

        /// <summary>The best valid attack right now, or null when none is (the caller then repositions).</summary>
        public EnemyAttackMove Pick(IReadOnlyList<EnemyAttackMove> moves)
        {
            candidates.Clear();
            scores.Clear();
            if (ctx.LogDecisions) BeginLog();

            float best = 0f;
            EnemyAttackMove opener = null;
            for (int i = 0; i < moves.Count; i++)
            {
                EnemyAttackMove move = moves[i];
                bool valid = Evaluate(move, out float score, out string reason);
                if (ctx.LogDecisions) AppendLog(move, valid, score, reason);
                if (!valid) continue;

                candidates.Add(move);
                scores.Add(score);
                best = Mathf.Max(best, score);
                if (ctx.PendingOpener != null && move.Definition == ctx.PendingOpener) opener = move;
            }

            if (candidates.Count == 0) return null;

            EnemyAttackMove chosen = opener != null ? opener : DrawAmongBest(best);
            if (opener != null) ctx.PendingOpener = null;

            if (ctx.LogDecisions)
            {
                log.Append(" => ").Append(chosen.Definition.DisplayName);
                if (opener != null) log.Append(" (abertura da fase)");
                Debug.Log(log.ToString());
            }

            return chosen;
        }

        /// <summary>Remembers the last two attacks for the repetition penalty.</summary>
        public void RecordUse(EnemyAttackMove move)
        {
            if (move == null) return;
            beforeLast = last;
            last = move;
        }

        /// <summary>
        /// Distance the enemy should hold while no attack is valid: the nearest point of the band of the
        /// attack that comes off cooldown first. Moving only into that band (never to its centre) keeps the
        /// repositioning short and readable.
        /// </summary>
        public float PreferredDistance(IReadOnlyList<EnemyAttackMove> moves, float currentDistance)
        {
            EnemyAttackMove next = null;
            for (int i = 0; i < moves.Count; i++)
            {
                EnemyAttackMove move = moves[i];
                if (ctx.Phase < move.Definition.minPhase) continue;
                if (next == null || IsSooner(move, next)) next = move;
            }

            if (next == null) return Mathf.Max(currentDistance, ctx.StopDistance);

            EnemyAttackDefinition d = next.Definition;
            float margin = Mathf.Min(set.rangeMargin, (d.maxRange - d.minRange) * 0.5f);
            float desired = Mathf.Clamp(currentDistance, d.minRange + margin, d.maxRange - margin);
            return Mathf.Max(desired, ctx.StopDistance);
        }

        private static bool IsSooner(EnemyAttackMove a, EnemyAttackMove b)
        {
            float ca = a.CooldownRemaining;
            float cb = b.CooldownRemaining;
            if (!Mathf.Approximately(ca, cb)) return ca < cb;
            return a.Definition.scoring.weight > b.Definition.scoring.weight;
        }

        /// <summary>Validity filter plus score. Public so debug tools can show the numbers.</summary>
        public bool Evaluate(EnemyAttackMove move, out float score, out string reason)
        {
            EnemyAttackDefinition d = move.Definition;
            score = 0f;

            if (ctx.Phase < d.minPhase) { reason = "fase " + d.minPhase; return false; }
            if (!move.IsReady) { reason = "recarga " + move.CooldownRemaining.ToString("0.0") + "s"; return false; }

            float distance = ctx.HorizontalDistance;
            if (distance < d.minRange || distance > d.maxRange) { reason = "fora do alcance"; return false; }
            if (ctx.TargetHeight > d.maxTargetHeight || ctx.TargetHeight < d.minTargetHeight) { reason = "altura"; return false; }
            if (d.requiresLineOfSight && !ctx.TargetInLineOfSight) { reason = "sem visao"; return false; }

            AttackScoring s = d.scoring;
            score = s.weight * RangeFit(d, distance);

            if (ctx.Phase > 0) score *= s.enragedMultiplier;
            if (!ctx.TargetGrounded) score *= s.targetAirborne;
            if (ctx.TargetAttacking) score *= s.targetAttacking;
            if (ctx.TargetRetreating) score *= s.targetRetreating;
            if (ctx.TargetGrappling) score *= s.targetGrappling;
            if (ctx.TargetPetrified) score *= s.targetPetrified;
            score *= ctx.TargetFacingSelf ? s.targetFacing : s.targetFacingAway;

            if (s.lingerBonusPerSecond > 0f)
                score *= Mathf.Min(1f + s.lingerBonusPerSecond * ctx.TimeTargetClose, s.maxLingerMultiplier);

            if (move == last) score *= set.repeatPenalty;
            else if (move == beforeLast) score *= set.secondRepeatPenalty;

            reason = null;
            if (score > 0f) return true;

            reason = "nota 0";
            return false;
        }

        // 1 at the centre of the band, easing down to rangeEdgeScore at either edge.
        private float RangeFit(EnemyAttackDefinition d, float distance)
        {
            float half = (d.maxRange - d.minRange) * 0.5f;
            if (half <= 0.0001f) return 1f;

            float t = Mathf.Clamp01(Mathf.Abs(distance - d.MidRange) / half);
            return Mathf.Lerp(1f, set.rangeEdgeScore, t * t);
        }

        // Small weighted draw among the attacks close to the best score: the boss stays readable (a clearly
        // better option always wins) without being perfectly predictable.
        private EnemyAttackMove DrawAmongBest(float best)
        {
            float threshold = best * (1f - set.tiebreakWindow);
            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (scores[i] >= threshold) total += scores[i];
            }

            float roll = Random.value * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (scores[i] < threshold) continue;
                roll -= scores[i];
                if (roll <= 0f) return candidates[i];
            }

            // Floating-point leftovers: fall back to the single best.
            int bestIndex = 0;
            for (int i = 1; i < candidates.Count; i++)
            {
                if (scores[i] > scores[bestIndex]) bestIndex = i;
            }
            return candidates[bestIndex];
        }

        private void BeginLog()
        {
            log.Clear();
            log.Append(ctx.self != null ? ctx.self.name : "Enemy")
               .Append(" | dist ").Append(ctx.HorizontalDistance.ToString("0.0"))
               .Append(" alt ").Append(ctx.TargetHeight.ToString("0.0"))
               .Append(" fase ").Append(ctx.Phase)
               .Append(ctx.TargetGrounded ? "" : " [no ar]")
               .Append(ctx.TargetAttacking ? " [atacando]" : "")
               .Append(ctx.TargetPetrified ? " [petrificado]" : "")
               .Append(" |");
        }

        private void AppendLog(EnemyAttackMove move, bool valid, float score, string reason)
        {
            log.Append(' ').Append(move.Definition.DisplayName).Append(": ")
               .Append(valid ? score.ToString("0.00") : "-- (" + reason + ")")
               .Append(" |");
        }
    }
}
