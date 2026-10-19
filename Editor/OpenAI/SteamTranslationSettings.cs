using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "SteamTranslationSettings", menuName = "Localization/Steam Translation Settings")]
public class SteamTranslationSettings : ScriptableObject {
    public string GameTitle = "";
    [TextArea(3, 10)] public string GameContext = "";
    [Tooltip("One name per line. These names and the game title are preserved exactly.")]
    [TextArea(2, 6)] public string ProtectedNames = "";
    [OpenAIModelList] public string Model = "gpt-6.1-sol";
    public ReasoningEffort Effort = ReasoningEffort.Medium;
    public int OutputTokenBudget = OpenAITranslationModels.MaxCompletionTokens;
    [LanguageList] public List<string> TargetLanguages = SteamLanguageList.All
        .Where(l => l.langCode != "english").Select(l => l.langCode).ToList();

    public static SteamTranslationSettings Find() {
        return AssetDatabase.FindAssets("t:SteamTranslationSettings")
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, System.StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<SteamTranslationSettings>).FirstOrDefault(s => s != null);
    }
}
