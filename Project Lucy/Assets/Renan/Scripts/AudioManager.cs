using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioManager : MonoBehaviour
{
    private AudioSource _source;
    public static AudioManager instance;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void PlayWithVariation(AudioClip clip)
    {
        _source.pitch = Random.Range(0.8f, 1.2f);
        _source.volume = Random.Range(0.3f, 0.4f);
        _source.PlayOneShot(clip);
    }
}
