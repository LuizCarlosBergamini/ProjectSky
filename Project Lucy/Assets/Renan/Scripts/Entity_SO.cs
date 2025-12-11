using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Dialog/New Entity"), Serializable]
public class Entity_SO : ScriptableObject
{
    public string entityName;
    public Sprite entitySprite;
    public AnimationClip enitityAnimation;
}
