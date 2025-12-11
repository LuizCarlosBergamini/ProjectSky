using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class AudioController : MonoBehaviour
{
    private float _currentVolume = 1.0f;

    private Slider slider = null;

    private bool HasAudioMixer () => AudioManager.instance && AudioManager.instance.audioMixer != null && slider != null;

    public void Start()
    {
        slider = GetComponent<Slider>();

        Debug.Assert(slider != null, "Slider is Required in AudioController");
        Debug.Assert(slider != null, "AudioMixer Reference is Required in AudioController");

        if (HasAudioMixer())
        {
            slider.onValueChanged.AddListener(SetVolume);
            AudioManager.instance.audioMixer.GetFloat("Volume", out float currentVolume);
            slider.value = Mathf.Pow(10, currentVolume / 20);
        }
    }

    public void OnEnable()
    {
        if (HasAudioMixer())
        {
            slider.onValueChanged.AddListener(SetVolume);
            AudioManager.instance.audioMixer.GetFloat("Volume", out float currentVolume);
            slider.value = Mathf.Pow(10, currentVolume / 20);
        }
    }

    public void OnDisable()
    {
        if (HasAudioMixer())
        {
            slider.onValueChanged.RemoveListener(SetVolume);
        }
    }

    public void SetVolume(float volume)
    {
        var newVolume = volume / slider.maxValue;
        if (HasAudioMixer() && _currentVolume != newVolume)
        {
            _currentVolume = newVolume;
            AudioManager.instance.audioMixer.SetFloat("Volume", newVolume > 0 ? Mathf.Log10(volume) * 20 : -80);
        }
    }
}
