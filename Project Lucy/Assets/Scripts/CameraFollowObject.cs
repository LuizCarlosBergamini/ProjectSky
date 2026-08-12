using System.Collections;
using HierarchicalStateMachine;
using UnityEngine;

public class CameraFollowObject : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform playerTransform;

    [Header("Flip Rotation Stats")]
    [SerializeField] private float flipDuration = 0.5f;
    [SerializeField] private float turnDelay = 0.3f;

    private Coroutine turnCouroutine;

    private PlayerStateDriver player;

    private bool isFacingRight;

    private void Awake()
    {
        player = playerTransform.GetComponent<PlayerStateDriver>();

        isFacingRight = player.isFacingRightLocal;
    }

    private void Update()
    {
        transform.position = playerTransform.position;
    }

    public void CallTurn()
    {
        Debug.Log("CallTurn called");   
        // Cancela qualquer animação de rotação anterior
        LeanTween.cancel(gameObject);

        // Sincroniza com o estado atual do player antes de rotacionar
        isFacingRight = player.isFacingRightLocal;

        // Executa a rotação
        LeanTween.rotateY(gameObject, DetermineEndRotation(), flipDuration).setEaseInOutSine();
    }

    private float DetermineEndRotation()
    {
        // Retorna a rotação baseada no estado ATUAL, sem inverter
        return isFacingRight ? 180f : 0f;
    }
}
