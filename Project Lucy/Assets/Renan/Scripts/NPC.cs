using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider2D))]
public class NPC : MonoBehaviour
{
    [SerializeField] private Dialog_SO _dialog;

    public void SetDialog(Dialog_SO dialog)
    {
        _dialog = dialog;
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        DialogManager.instance.StartDialog(_dialog);
    }
}
