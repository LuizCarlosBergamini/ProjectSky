using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;

public class MouseHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    [SerializeField] private float scaleHover = 1.1f;
    private float defaultScale;

    [Header("Interação com Áudio (Requer AudioSource)")]
    [SerializeField] private AudioClip hoverAudioClip;
    [SerializeField] private AudioClip clickAudioClip;

    [Header("Interação com Sprite (Reguer Image)")]
    [SerializeField] private Sprite hoverSprite;
    private Sprite initialSprite;

    private AudioSource _audioSource;
    private SpriteRenderer _spriteRenderer;

    public void Start()
    {
        defaultScale = transform.localScale.x;
        _audioSource = GetComponent<AudioSource>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        if (_spriteRenderer != null && hoverSprite != null)
        {
            initialSprite = _spriteRenderer.sprite;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (gameObject.activeInHierarchy)
        {
            if (_audioSource != null && clickAudioClip != null)
            {
                RandomizeAudioSettings();
                //_audioSource.pitch = 0.6f;
                //_audioSource.volume = 1f;
                _audioSource.PlayOneShot(clickAudioClip);
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (gameObject.activeInHierarchy)
        {
            transform.localScale = Vector2.one * scaleHover;

            if (_audioSource != null && hoverAudioClip != null)
            {
                RandomizeAudioSettings();
                _audioSource.PlayOneShot(hoverAudioClip);
            }

            if (_spriteRenderer != null && hoverSprite != null)
            {
                _spriteRenderer.sprite = hoverSprite;
            } 
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (gameObject.activeInHierarchy)
        {
            transform.localScale = Vector2.one * defaultScale;
            if (_spriteRenderer != null && hoverSprite != null)
            {
                _spriteRenderer.sprite = initialSprite;
            }
        }
    }

    private void RandomizeAudioSettings()
    {
        if (_audioSource != null)
        {
            _audioSource.pitch = Random.Range(0.0f, 1.0f) * .4f + 0.8f;
            _audioSource.volume = Random.Range(0.0f, 1.0f) * .4f + 0.3f;
        }
    }
}
