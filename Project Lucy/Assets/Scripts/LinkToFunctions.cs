using UnityEngine;

public class LinkToFunctions : MonoBehaviour
{
    [SerializeField] private PlayerMovement playerScript;

    void StartAttacking()
    {
        StartCoroutine(playerScript.DamageWhileSlashIsActive());
    }

    void StopAttacking()
    {
        playerScript.ShouldBeDamagingToFalse();
    }
}
