using UnityEngine;

// Cycles a SpriteRenderer through a list of frames; used for scenery like campfires, torches and portals.
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFlipbook : MonoBehaviour
{
    public Sprite[] frames;
    public float framesPerSecond = 10f;
    public bool randomStart = true;

    private SpriteRenderer spriteRenderer;
    private float time;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (randomStart && frames != null && frames.Length > 0)
            time = Random.Range(0f, frames.Length / framesPerSecond);
    }

    void Update()
    {
        if (frames == null || frames.Length == 0 || framesPerSecond <= 0f) return;
        time += Time.deltaTime;
        int index = (int)(time * framesPerSecond) % frames.Length;
        spriteRenderer.sprite = frames[index];
    }
}
