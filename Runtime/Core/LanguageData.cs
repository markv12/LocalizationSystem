using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LanguageData", menuName = "Localization/Language Data")]
public class LanguageData : ScriptableObject {
    public string LanguageCode;
    public string[] Keys;
    public string[] Values;

    public Dictionary<string, string> ToDictionary() {
        var dict = new Dictionary<string, string>();
        if (Keys != null && Values != null) {
            int count = Mathf.Min(Keys.Length, Values.Length);
            for (int i = 0; i < count; i++) {
                dict[Keys[i]] = Values[i];
            }
        }
        return dict;
    }
}
