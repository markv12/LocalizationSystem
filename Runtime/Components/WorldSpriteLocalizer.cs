#if UNITY_EDITOR
using UnityEditor;
#endif
using TMPro;
using UnityEngine;

public class WorldSpriteLocalizer : LanguageChangedHandler {
    public SpriteRenderer mainRenderer;
    public TMP_Text mainText;
    private Sprite startSprite;

    protected override void Awake() {
        if (mainRenderer == null) mainRenderer = GetComponent<SpriteRenderer>();
        if (mainText == null) mainText = GetComponentInChildren<TMP_Text>(true);

        if (mainRenderer != null) {
            startSprite = mainRenderer.sprite;
        }
        if (mainText != null) {
            mainText.gameObject.SetActive(false);
        }
        base.Awake();
    }

    protected override void Refresh() {
        if (mainRenderer == null) return;

        if (SpriteLocalizer.GetLocalizedSpriteInfo(startSprite, out Sprite outputSprite, out string outputString)) {
            if (mainText != null) mainText.gameObject.SetActive(false);
            mainRenderer.sprite = outputSprite;
        } else {
            if (mainText != null) {
                RTLHelper.SetText(mainText, outputString);
                mainText.font = Localizer.CurrentLangFont;
                mainText.gameObject.SetActive(true);
            }
            mainRenderer.sprite = outputSprite;
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(WorldSpriteLocalizer))]
public class WorldSpriteLocalizerEditor : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        WorldSpriteLocalizer model = (WorldSpriteLocalizer)target;
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        if (GUILayout.Button("Get Component", GUILayout.Width(EditorGUIUtility.currentViewWidth * .45f))) {
            model.mainRenderer = model.GetComponent<SpriteRenderer>();
            model.mainText = model.GetComponentInChildren<TMP_Text>(true);
            EditorUtility.SetDirty(model);
        }
        GUILayout.EndHorizontal();
    }
}
#endif
