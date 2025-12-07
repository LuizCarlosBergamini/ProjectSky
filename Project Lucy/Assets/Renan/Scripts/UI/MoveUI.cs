using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class MoveUI : MonoBehaviour
{

    public Vector2 visiblePosition;
    public Vector2 hiddenPosition;
    public float transitionDuration = 0.5f;
    public bool unscaledTime = true;

    private RectTransform _rectTransform = null;

    public void OnEnable()
    {
        _rectTransform = GetComponent<RectTransform>();
        Debug.Assert(_rectTransform != null, "RectTransform Component is required to MoveUI Script");
    }

    public IEnumerator Hide(int delay = 0)
    {
        yield return new WaitForSeconds(delay/1000f);
        StartCoroutine(_rectTransform.Move(hiddenPosition, transitionDuration, unscaledTime));
    }

    public void Hide()
    {
        StartCoroutine(Hide(0));
    }

    public IEnumerator Show(int delay = 0)
    {
        yield return new WaitForSeconds(delay / 1000f);
        StartCoroutine(_rectTransform.Move(visiblePosition, transitionDuration, unscaledTime));
    }

    public void Show()
    {
        StartCoroutine(Show(0));
    }
}
