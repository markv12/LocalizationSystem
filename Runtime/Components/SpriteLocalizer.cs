using System;
using System.Collections.Generic;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[CreateAssetMenu(fileName = "SpriteLocalizer", menuName = "Localization/Sprite Localizer")]
[PreferBinarySerialization]
public class SpriteLocalizer : ScriptableObject {
    [SerializeField] public SpriteSet[] spriteSets;
    private readonly Dictionary<string, SpriteSet> spriteDictionary = new Dictionary<string, SpriteSet>(16);

    private static SpriteLocalizer instance;
    public static SpriteLocalizer Instance {
        get {
            if (instance == null) {
                instance = Resources.Load<SpriteLocalizer>("SpriteLocalizer");
                if (instance != null) {
                    instance.SetupDictionary();
                }
            }
            return instance;
        }
    }

    private void SetupDictionary() {
        spriteDictionary.Clear();
        if (spriteSets == null) return;
        for (int i = 0; i < spriteSets.Length; i++) {
            SpriteSet spriteSet = spriteSets[i];
            if (spriteSet?.englishSprite == null) continue;
            spriteDictionary[spriteSet.englishSprite.name] = spriteSet;
        }
    }

    public static bool GetLocalizedSpriteInfo(Sprite inputSprite, out Sprite outputSprite, out string outputString) {
        Localizer.EnsureLoaded();
        if (inputSprite == null || Instance == null || !Instance.spriteDictionary.TryGetValue(inputSprite.name, out SpriteSet spriteSet)) {
            outputSprite = inputSprite;
            outputString = null;
            return true;
        }
        return spriteSet.GetLocalizedSpriteInfo(Localizer.currentLanguage, out outputSprite, out outputString);
    }

    [Serializable]
    public class SpriteSet {
        public Sprite englishSprite;
        public Sprite blankSprite;
        public string locID;

        public bool GetLocalizedSpriteInfo(string sl, out Sprite outputSprite, out string outputString) {
            if (string.IsNullOrEmpty(sl) || sl.Equals(Localizer.DEFAULT_LANGUAGE, StringComparison.OrdinalIgnoreCase)) {
                outputSprite = englishSprite;
                outputString = null;
                return true;
            } else {
                outputSprite = blankSprite != null ? blankSprite : englishSprite;
                outputString = Localizer.GetText(locID);
                return false;
            }
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(SpriteLocalizer))]
public class SpriteLocalizerEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        SpriteLocalizer myScript = (SpriteLocalizer)target;
        if (GUILayout.Button("Find Texture Pairs")) {
            myScript.spriteSets = FindTexturePairs(myScript.spriteSets);
            EditorUtility.SetDirty(myScript);
        }
    }

    private SpriteLocalizer.SpriteSet[] FindTexturePairs(SpriteLocalizer.SpriteSet[] spriteSets) {
        Dictionary<string, SpriteLocalizer.SpriteSet> result = new Dictionary<string, SpriteLocalizer.SpriteSet>();
        if (spriteSets != null) {
            foreach (SpriteLocalizer.SpriteSet spriteSet in spriteSets) {
                if (spriteSet?.englishSprite != null) {
                    result[spriteSet.englishSprite.name] = spriteSet;
                }
            }
        }

        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D");
        Dictionary<string, Texture2D> textureDictionary = new Dictionary<string, Texture2D>();

        foreach (string guid in textureGuids) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            textureDictionary[fileName] = texture;
        }

        foreach (KeyValuePair<string, Texture2D> entry in textureDictionary) {
            string fileName = entry.Key;

            if (fileName.EndsWith("_blank")) {
                string baseName = fileName.Substring(0, fileName.Length - "_blank".Length);
                if (textureDictionary.ContainsKey(baseName) && !result.ContainsKey(baseName)) {
                    Sprite englishSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(textureDictionary[baseName]));
                    Sprite blankSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(entry.Value));
                    result.Add(baseName, new SpriteLocalizer.SpriteSet() {
                        englishSprite = englishSprite,
                        blankSprite = blankSprite,
                    });
                    Debug.Log($"[SpriteLocalizer] Found texture pair: {textureDictionary[baseName]} and {entry.Value}");
                }
            }
        }

        SpriteLocalizer.SpriteSet[] finalResult = new SpriteLocalizer.SpriteSet[result.Count];
        int index = 0;
        foreach (KeyValuePair<string, SpriteLocalizer.SpriteSet> entry in result) {
            finalResult[index] = entry.Value;
            index++;
        }
        return finalResult;
    }
}
#endif
