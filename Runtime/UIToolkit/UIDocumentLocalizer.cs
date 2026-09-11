using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Translates a UIDocument's static UXML text. Attachable directly or added dynamically at runtime.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class UIDocumentLocalizer : MonoBehaviour {
    private UIDocument _document;
    private string _prefix;

    private void OnEnable() {
        _document = GetComponent<UIDocument>();
        _prefix = UILocalization.DocumentPrefix(_document.visualTreeAsset != null ? _document.visualTreeAsset.name : null);
        Apply();
        Localizer.LanguageChangedEvent += Apply;
    }

    private void OnDisable() {
        Localizer.LanguageChangedEvent -= Apply;
    }

    private void Start() => Apply();

    public void Apply() {
        if (_document == null) return;
        UILocalization.Apply(_document.rootVisualElement, _prefix);
    }
}
