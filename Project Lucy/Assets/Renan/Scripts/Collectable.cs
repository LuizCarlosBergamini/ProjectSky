using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Collectable : MonoBehaviour
{
    public Item_SO item;
    [SerializeField] private AudioClip _collectClip;

    [SerializeField] private Animator _animator;
    [SerializeField] private SpriteRenderer _spriteRenderer;

    private AnimatorOverrideController _overrideController;

    private void Awake()
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
        }
    }

    private void Start()
    {
        if (item != null)
        {
            if (_spriteRenderer != null && item.itemAnimationClip == null)
            {
                _spriteRenderer.sprite = item.itemSprite;
            } else if (_animator != null && item.itemAnimationClip != null) {
                _overrideController["BaseClip"] = item.itemAnimationClip;
                _animator.runtimeAnimatorController = _overrideController;
                _animator.Play("BaseClip", 0, 0f);
            }
        }
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") && InventoryManager.instance != null)
        {
            InventoryManager.instance.AddItem(item);
            Destroy(gameObject);
            if (_collectClip != null && AudioManager.instance != null)
            {
                AudioManager.instance.PlayWithVariation(_collectClip);
            }
        }
    }
}
