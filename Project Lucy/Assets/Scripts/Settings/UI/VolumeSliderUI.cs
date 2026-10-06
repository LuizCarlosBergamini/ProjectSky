using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds one Slider to one volume channel of SettingsManager: shows the saved value when enabled and applies every
/// change immediately, while dragging. The slider's range can be anything; its normalized value is what is stored.
/// </summary>
[RequireComponent(typeof(Slider))]
public class VolumeSliderUI : MonoBehaviour
{
    [SerializeField] private VolumeChannel _channel = VolumeChannel.Master;
    [SerializeField] private Slider _slider;

    [Tooltip("Mostra o valor em porcentagem. Opcional.")]
    [SerializeField] private TextMeshProUGUI _valueLabel;

    private bool _refreshing;

    public VolumeChannel Channel => _channel;
    public Slider Slider => _slider;

    private void Awake()
    {
        if (_slider == null) _slider = GetComponent<Slider>();
    }

    private void OnEnable()
    {
        _slider.onValueChanged.AddListener(HandleValueChanged);
        SettingsManager.OnSettingsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        _slider.onValueChanged.RemoveListener(HandleValueChanged);
        SettingsManager.OnSettingsChanged -= Refresh;
    }

    public void Refresh()
    {
        SettingsManager manager = SettingsManager.instance;
        _slider.interactable = manager != null;
        if (manager == null) return;

        // Setting the slider fires onValueChanged; that echo must not write back (and mark the settings dirty).
        _refreshing = true;
        _slider.normalizedValue = manager.GetVolume(_channel);
        _refreshing = false;

        UpdateLabel();
    }

    private void HandleValueChanged(float value)
    {
        UpdateLabel();
        if (_refreshing || SettingsManager.instance == null) return;
        SettingsManager.instance.SetVolume(_channel, _slider.normalizedValue);
    }

    private void UpdateLabel()
    {
        if (_valueLabel != null) _valueLabel.text = $"{Mathf.RoundToInt(_slider.normalizedValue * 100f)}%";
    }
}
