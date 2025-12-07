using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MenuController : MonoBehaviour
{
    public static MenuController instance;
    [SerializeField] List<GameObject> menuElements;

    void Start()
    {
        if (instance != null)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
    }

    public IEnumerator Hide(int delay)
    {

        yield return new WaitForSeconds(delay / 1000f);
        menuElements.ForEach(element =>
        {
            if (element.TryGetComponent(out MoveUI move_ui))
                move_ui.Hide();

        });
    }

    public IEnumerator Show(int delay)
    {
        yield return new WaitForSeconds(delay / 1000f);
        menuElements.ForEach(element =>
        {
            if (element.TryGetComponent(out MoveUI move_ui))
                move_ui.Show();

        });
    }
}
