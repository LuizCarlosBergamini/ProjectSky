using System;
using HierarchicalStateMachine;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// A message over the arena after a boss falls (BossData_SO.endScreenMessage), with a Continuar button that closes it
/// so the player keeps playing. Lives on the HUD; BossFightUIController opens it once the defeat dialog is over.
/// Built in code the first time it opens, with the pause menu's look (dark sliced frame, faint border, Jersey font,
/// accent on the focused button), so the HUD prefab only carries this component and its three asset references.
/// While open the game is frozen like the pause: time stopped and Lucy's input off; Continuar gives both back.
/// </summary>
public class BossEndScreen : MonoBehaviour
{
    private static readonly Color FrameColor = new(0.035f, 0.035f, 0.04f, 0.98f);
    private static readonly Color TextColor = new(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color ButtonColor = new(0.18f, 0.18f, 0.2f, 1f);

    [Header("Visual (mesmo do menu de pause)")]
    [SerializeField] private TMP_FontAsset _font;
    [SerializeField] private Sprite _panelSprite;
    [SerializeField] private Sprite _borderSprite;

    [Tooltip("Texto do botao que fecha a mensagem.")]
    [SerializeField] private string _buttonText = "CONTINUAR";

    [Header("Ordem de desenho")]
    [Tooltip("Mesmo nivel do seletor de chefes e da tela de upgrades: abaixo do pause (55) e do fade (100).")]
    [SerializeField] private int _sortingOrder = 50;

    private GameObject _root;
    private TextMeshProUGUI _messageLabel;
    private Button _continueButton;
    private GameObject _fallbackEventSystem;

    private Action _onClosed;
    private PlayerStateDriver _player;
    private bool _playerInputWasEnabled;
    private float _previousTimeScale = 1f;
    private int _openedFrame = -1;

    public bool IsOpen { get; private set; }

    private void OnDisable()
    {
        // Leaving the scene with the message up must not keep the next scene frozen.
        if (IsOpen) Close();
    }

    /// <summary>Shows the message and freezes the game until Continuar is pressed.</summary>
    public void Open(string message, Action onClosed = null)
    {
        if (IsOpen) return;
        if (_root == null) Build();

        IsOpen = true;
        _openedFrame = Time.frameCount;
        _onClosed = onClosed;
        _messageLabel.text = message;

        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        GameObject playerObject = GameObject.FindWithTag("Player");
        _player = playerObject != null ? playerObject.GetComponent<PlayerStateDriver>() : null;
        _playerInputWasEnabled = _player != null && _player.IsInputEnabled;
        if (_player != null) _player.SetInputEnabled(false);

        // The levels have no EventSystem of their own (the pause brings its own only while it is open).
        if (FindAnyObjectByType<EventSystem>() == null) _fallbackEventSystem.SetActive(true);

        _root.SetActive(true);

        // Focused from the start, so Submit (Enter, gamepad A) closes it without the mouse.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null) eventSystem.SetSelectedGameObject(_continueButton.gameObject);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        _root.SetActive(false);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null) eventSystem.SetSelectedGameObject(null);
        _fallbackEventSystem.SetActive(false);

        Time.timeScale = _previousTimeScale;
        if (_player != null && _playerInputWasEnabled) _player.SetInputEnabled(true);
        _player = null;

        Action callback = _onClosed;
        _onClosed = null;
        callback?.Invoke();
    }

    private void HandleContinue()
    {
        // The press that closed the last dialog line may still be live on the frame this opens.
        if (Time.frameCount == _openedFrame) return;
        Close();
    }

    #region Build

    private void Build()
    {
        RectTransform canvasRect = CreateRect("BossEndScreen", transform);
        Stretch(canvasRect);
        Canvas canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = _sortingOrder;
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = CreateRect("Root", canvasRect);
        Stretch(root);
        _root = root.gameObject;

        // Darkens the frozen game and blocks clicks to anything under the panel.
        Image dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.65f), raycast: true);
        Stretch(dim.rectTransform);

        Image frame = CreateImage("Frame", root, _panelSprite, FrameColor, Image.Type.Sliced, raycast: true);
        RectTransform frameRect = frame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.sizeDelta = new Vector2(680f, 360f);
        Image frameBorder = CreateImage("Border", frameRect, _borderSprite, new Color(1f, 1f, 1f, 0.12f), Image.Type.Sliced);
        Stretch(frameBorder.rectTransform);

        _messageLabel = CreateText("Message", frameRect, "", 34f, TextColor, TextAlignmentOptions.Center);
        _messageLabel.textWrappingMode = TextWrappingModes.Normal;
        _messageLabel.enableAutoSizing = true;
        _messageLabel.fontSizeMin = 22f;
        _messageLabel.fontSizeMax = 34f;
        Stretch(_messageLabel.rectTransform, 44f, 44f, 36f, 120f);

        _continueButton = CreateButton("ContinueButton", frameRect, _buttonText);
        RectTransform buttonRect = (RectTransform)_continueButton.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.sizeDelta = new Vector2(340f, 56f);
        buttonRect.anchoredPosition = new Vector2(0f, 40f);
        _continueButton.navigation = new Navigation { mode = Navigation.Mode.None };
        _continueButton.onClick.AddListener(HandleContinue);

        // Runtime-only, so the package's default UI actions can be assigned directly.
        _fallbackEventSystem = new GameObject("FallbackEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        _fallbackEventSystem.transform.SetParent(transform, false);
        _fallbackEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        _fallbackEventSystem.SetActive(false);

        _root.SetActive(false);
    }

    private Button CreateButton(string name, Transform parent, string label)
    {
        Image image = CreateImage(name, parent, _panelSprite, ButtonColor, Image.Type.Sliced, raycast: true);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = new Color(0.95f, 0.72f, 0.3f, 1f); // accent: the focused button is obvious on a gamepad
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        button.colors = colors;

        TextMeshProUGUI text = CreateText("Label", image.transform, label, 32f, TextColor, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color,
        Image.Type type = Image.Type.Simple, bool raycast = false)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = sprite != null ? type : Image.Type.Simple;
        image.raycastTarget = raycast;
        return image;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    #endregion
}
