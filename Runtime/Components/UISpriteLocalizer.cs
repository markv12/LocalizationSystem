#if UNITY_EDITOR
using UnityEditor;
#endif
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UISpriteLocalizer : LanguageChangedHandler {
    public Image mainImage;
    public Button mainButton;
    public TMP_Text mainText;
    private Sprite startSprite;
    private Sprite startHighlightSprite;

    protected override void Awake() {
        if (mainImage == null) mainImage = GetComponent<Image>();
        if (mainButton == null) mainButton = GetComponent<Button>();
        if (mainText == null) mainText = GetComponentInChildren<TMP_Text>(true);

        if (mainImage != null) {
            startSprite = mainImage.sprite;
        }
        if (HasSpriteButton) {
            startHighlightSprite = mainButton.spriteState.highlightedSprite;
        }
        if (mainText != null) {
            mainText.gameObject.SetActive(false);
        }
        base.Awake();
    }

    protected override void Refresh() {
        if (mainImage == null) return;

        if (SpriteLocalizer.GetLocalizedSpriteInfo(startSprite, out Sprite outputSprite, out string outputString)) {
            if (mainText != null) mainText.gameObject.SetActive(false);
            SetSprite(outputSprite);
        } else {
            if (mainText != null) {
                RTLHelper.SetText(mainText, outputString);
                mainText.font = Localizer.CurrentLangFont;
                mainText.gameObject.SetActive(true);
            }
            SetSprite(outputSprite);
        }
    }

    private void SetSprite(Sprite mainSprite) {
        if (mainImage != null) {
            mainImage.sprite = mainSprite;
        }
        if (HasSpriteButton) {
            SpriteState ss = mainButton.spriteState;
            ss.disabledSprite = mainSprite;
            SpriteLocalizer.GetLocalizedSpriteInfo(startHighlightSprite, out Sprite outputSprite, out string _);
            ss.highlightedSprite = outputSprite;
            ss.pressedSprite = outputSprite;
            ss.selectedSprite = outputSprite;
            mainButton.spriteState = ss;
        }
    }

    private bool HasSpriteButton => mainButton != null && mainButton.transition == Selectable.Transition.SpriteSwap;
}

#if UNITY_EDITOR
[CustomEditor(typeof(UISpriteLocalizer))]
public class UISpriteLocalizerEditor : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        UISpriteLocalizer model = (UISpriteLocalizer)target;
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        if (GUILayout.Button("Get Component", GUILayout.Width(EditorGUIUtility.currentViewWidth * .45f))) {
            model.mainImage = model.GetComponent<Image>();
            model.mainButton = model.GetComponent<Button>();
            model.mainText = model.GetComponentInChildren<TMP_Text>(true);
            EditorUtility.SetDirty(model);
        }
        GUILayout.EndHorizontal();
    }
}
#endif
