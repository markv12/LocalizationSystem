using System;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class LanguageSelectionDropdown : AdvancedDropdown {
    private Action<string> _onSelected;

    public LanguageSelectionDropdown(AdvancedDropdownState state, Action<string> onSelected) : base(state) {
        _onSelected = onSelected;
        this.minimumSize = new Vector2(250, 400);
    }

    protected override AdvancedDropdownItem BuildRoot() {
        AdvancedDropdownItem root = new AdvancedDropdownItem("Languages");

        foreach (SteamLanguage lang in SteamLanguageList.All) {
            root.AddChild(new AdvancedDropdownItem(lang.displayName));
        }

        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item) {
        var (displayName, langCode) = SteamLanguageList.All.FirstOrDefault(l => l.displayName == item.name);
        if (!string.IsNullOrEmpty(langCode)) {
            _onSelected?.Invoke(langCode);
        }
    }

    public static string GetDisplayName(string langCode) {
        return SteamLanguageList.GetDisplayName(langCode);
    }
}

[CustomPropertyDrawer(typeof(LanguageListAttribute))]
public class LanguageListDrawer : PropertyDrawer {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        if (property.propertyType != SerializedPropertyType.String) {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        string currentVal = property.stringValue;
        string display = string.IsNullOrEmpty(currentVal) ? "Select Language..." : LanguageSelectionDropdown.GetDisplayName(currentVal);

        Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
        Rect buttonRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, position.height);

        EditorGUI.LabelField(labelRect, label);
        if (GUI.Button(buttonRect, display, EditorStyles.popup)) {
            var dropdown = new LanguageSelectionDropdown(new AdvancedDropdownState(), (code) => {
                property.stringValue = code;
                property.serializedObject.ApplyModifiedProperties();
            });
            dropdown.Show(buttonRect);
        }
    }
}

public class LanguageListAttribute : PropertyAttribute { }

public class OpenAIModelDropdown : AdvancedDropdown {
    private static readonly string[] _models = new[] {
        "gpt-5.6-luna",
        "gpt-5.6-terra",
        "gpt-5.6-sol",
        "gpt-5.5",
        "gpt-5.2",
        "gpt-4o",
        "gpt-4o-mini"
    };

    private Action<string> _onSelected;

    public OpenAIModelDropdown(AdvancedDropdownState state, Action<string> onSelected) : base(state) {
        _onSelected = onSelected;
        this.minimumSize = new Vector2(250, 300);
    }

    protected override AdvancedDropdownItem BuildRoot() {
        var root = new AdvancedDropdownItem("OpenAI Models");
        foreach (var model in _models) {
            root.AddChild(new AdvancedDropdownItem(model));
        }
        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item) {
        _onSelected?.Invoke(item.name);
    }
}

[CustomPropertyDrawer(typeof(OpenAIModelListAttribute))]
public class OpenAIModelListDrawer : PropertyDrawer {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        if (property.propertyType != SerializedPropertyType.String) {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        string display = string.IsNullOrEmpty(property.stringValue) ? "Select Model..." : property.stringValue;

        Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
        Rect buttonRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, position.height);

        EditorGUI.LabelField(labelRect, label);
        if (GUI.Button(buttonRect, display, EditorStyles.popup)) {
            var dropdown = new OpenAIModelDropdown(new AdvancedDropdownState(), (model) => {
                property.stringValue = model;
                property.serializedObject.ApplyModifiedProperties();
            });
            dropdown.Show(buttonRect);
        }
    }
}

public class OpenAIModelListAttribute : PropertyAttribute { }
