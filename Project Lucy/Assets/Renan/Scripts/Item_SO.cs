using UnityEngine;

[CreateAssetMenu(menuName = "Item/New Item")]
public class Item_SO : ScriptableObject
{
    public string itemId;
    public string itemName;
    [Tooltip("Texto mostrado no tooltip do item (ex: recompensas no seletor de chefe).")]
    [TextArea] public string itemDescription;
    public Sprite itemSprite;
    public AnimationClip itemAnimationClip;
}
