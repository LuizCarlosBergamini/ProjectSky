using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Master volume slider of the old Menu scene. Goes through SettingsManager (like VolumeSliderUI) so it drives the
/// same mixer parameter and the value is saved; it used to write the mixer's old "Volume" parameter directly.
/// </summary>
[RequireComponent(typeof(Slider))]
public class AudioController : MonoBehaviour
{
    private Slider slider = null;
    private bool _refreshing;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        Debug.Assert(slider != null, "Slider is Required in AudioController");
    }

    public void OnEnable()
    {
        slider.onValueChanged.AddListener(SetVolume);

        if (SettingsManager.instance != null)
        {
            _refreshing = true;
            slider.normalizedValue = SettingsManager.instance.GetVolume(VolumeChannel.Master);
            _refreshing = false;
        }
    }

    public void OnDisable()
    {
        slider.onValueChanged.RemoveListener(SetVolume);
        if (SettingsManager.instance != null) SettingsManager.instance.Save();
    }

    public void SetVolume(float volume)
    {
        if (_refreshing || SettingsManager.instance == null) return;
        SettingsManager.instance.SetVolume(VolumeChannel.Master, slider.normalizedValue);
    }
}
