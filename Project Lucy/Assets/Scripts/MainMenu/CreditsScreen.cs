using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The credits screen of the main menu: the game's title and who made it. Opens over the menu, focus goes to the
/// Back button, and Back or UI/Cancel (Esc, gamepad B) closes it. The team list lives in the Inspector, so a new
/// name or role needs no code. The menu decides what happens on close through the Closed event.
/// </summary>
public class CreditsScreen : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public string name;
        public string role;
    }

    [Header("Referencias")]
    [Tooltip("Tudo que aparece com os creditos abertos. Este objeto fica sempre ativo.")]
    [SerializeField] private GameObject _root;
    [Tooltip("Texto que recebe a lista da equipe (nome e funcao).")]
    [SerializeField] private TextMeshProUGUI _entriesLabel;
    [SerializeField] private Button _backButton;

    [Header("Equipe")]
    [SerializeField] private Entry[] _entries =
    {
        new() { name = "Luiz Carlos Bergamini", role = "Desenvolvedor" },
        new() { name = "João Otávio Granzotto", role = "Artista" },
        new() { name = "Gabriel Henry Moura", role = "Sonoplasta" },
    };

    [Header("Input")]
    [Tooltip("Voltar pelo teclado/controle: UI/Cancel (Esc, botao B do controle).")]
    [SerializeField] private InputActionReference _cancelAction;

    private int _openedFrame = -1;

    public event Action Closed;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (_root != null) _root.SetActive(false);
        if (_backButton != null) _backButton.onClick.AddListener(Close);
        RefreshEntries();
    }

    private void OnDestroy()
    {
        if (_backButton != null) _backButton.onClick.RemoveListener(Close);
    }

    private void OnValidate()
    {
        // Lets the Inspector list preview in the Editor right away.
        RefreshEntries();
    }

    private void Update()
    {
        if (!IsOpen) return;

        // The frame the screen opens on may still carry the press that opened it.
        if (Time.frameCount != _openedFrame && _cancelAction != null && _cancelAction.action != null &&
            _cancelAction.action.WasPressedThisFrame())
        {
            Close();
            return;
        }

        // Clicking empty space clears the EventSystem selection; give keyboard/gamepad a button back.
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == null) SelectBack(eventSystem);
    }

    public void Open()
    {
        if (IsOpen) return;

        IsOpen = true;
        _openedFrame = Time.frameCount;
        RefreshEntries();
        if (_root != null) _root.SetActive(true);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null) SelectBack(eventSystem);
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        if (_root != null) _root.SetActive(false);
        Closed?.Invoke();
    }

    /// <summary>One block per person: the name, and the role under it in the accent colour.</summary>
    public static string FormatEntries(Entry[] entries)
    {
        if (entries == null) return "";

        StringBuilder text = new();
        foreach (Entry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.name)) continue;
            if (text.Length > 0) text.Append("\n\n");

            text.Append(entry.name.Trim());
            if (!string.IsNullOrWhiteSpace(entry.role))
            {
                text.Append("\n<size=75%><color=#F2B84D>").Append(entry.role.Trim()).Append("</color></size>");
            }
        }

        return text.ToString();
    }

    /// <summary>Writes the Inspector list into the label.</summary>
    public void RefreshEntries()
    {
        if (_entriesLabel != null) _entriesLabel.text = FormatEntries(_entries);
    }

    private void SelectBack(EventSystem eventSystem)
    {
        if (_backButton != null) eventSystem.SetSelectedGameObject(_backButton.gameObject);
    }
}
