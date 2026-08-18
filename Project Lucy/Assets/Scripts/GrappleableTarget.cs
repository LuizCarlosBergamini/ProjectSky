using System.Collections.Generic;
using UnityEngine;

public class GrappleableTarget : MonoBehaviour
{
    private static readonly List<GrappleableTarget> ActiveTargets = new List<GrappleableTarget>();

    /// <summary>Every enabled target in the loaded scenes. Avoids a FindObjectsByType scan per shot.</summary>
    public static IReadOnlyList<GrappleableTarget> Active => ActiveTargets;

    [Min(0f)] public float grappleRadius = 1.5f;
    public Color gizmoColor = new Color(0.2f, 0.9f, 1f, 0.9f);

    private void OnEnable()
    {
        if (!ActiveTargets.Contains(this)) ActiveTargets.Add(this);
    }

    private void OnDisable()
    {
        ActiveTargets.Remove(this);
    }

    public Vector2 GetAnchorPoint()
    {
        return transform.position;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, grappleRadius);
    }
}
