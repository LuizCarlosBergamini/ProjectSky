using UnityEngine;

[CreateAssetMenu(menuName = "Item/New Item")]
public class Item_SO : ScriptableObject
{
    public string itemId;
    public string itemName;
    public Sprite itemSprite;
    public AnimationClip itemAnimationClip;
}
