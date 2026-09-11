using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Debug = UnityEngine.Debug;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;
using TextCoreAtlasMode = UnityEngine.TextCore.Text.AtlasPopulationMode;

/// <summary>
/// One-click rebuild of the font used for every language the main font can't draw:
/// collects characters shipped in the database, merges them out of the Noto faces into
/// NotoAll.ttf (via fontMerge.py), then re-bakes the SDF atlases for TextMeshPro and UI Toolkit.
/// </summary>
public static class LocalizationFontBuilder {
    public const string TtfPath = "Assets/Fonts/NotoAll/NotoAll.ttf";
    public const string UiFontPath = "Assets/Fonts/NotoAll/NotoAll SDF.asset";
    public const string TmpFontPath = "Assets/Fonts/NotoAll/NotoAll SDFx.asset";

    private const string PanelTextSettingsPath = "Assets/UI Toolkit/PanelTextSettings.asset";
    private const string TmpSettingsPath = "Assets/Standard Assets/TextMesh Pro/Resources/TMP Settings.asset";
    private const string MaterialFolder = "Assets/Fonts/NotoAll";
    private const string CharactersPath = "Localization/FontCharacters.txt";
    private const string FoundCharactersPath = "Localization/FontCharactersFound.txt";

    private const int PointSize = 28;
    private const int Padding = 10;
    private const int AtlasSize = 4096;

    private const int Static = 0;
    private const int Dynamic = 1;

    private const string PythonPathKey = "Localization.PythonPath";

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int TextureWidthId = Shader.PropertyToID("_TextureWidth");
    private static readonly int TextureHeightId = Shader.PropertyToID("_TextureHeight");
    private static readonly int GradientScaleId = Shader.PropertyToID("_GradientScale");

    [MenuItem("Tools/Localization/Rebuild Localization Font")]
    private static void RebuildFromMenu() {
        LocalizationMasterDatabase db = Resources.Load<LocalizationMasterDatabase>("LocalizationMasterDatabase");
        if (db == null) {
            Debug.LogError("[Localization] LocalizationMasterDatabase not found in Resources.");
            return;
        }
        Rebuild(db);
    }

    public static void Rebuild(LocalizationMasterDatabase db) {
        try {
            EditorUtility.DisplayProgressBar("Rebuild Localization Font", "Collecting characters...", 0.1f);
            WriteText(CharactersPath, ToText(CollectCodePoints(db)));

            EditorUtility.DisplayProgressBar("Rebuild Localization Font", "Merging Noto fonts...", 0.3f);
            if (!RunMerge()) return;

            EditorUtility.DisplayProgressBar("Rebuild Localization Font", "Importing merged font...", 0.6f);
            AssetDatabase.ImportAsset(TtfPath, ImportAssetOptions.ForceUpdate);

            EditorUtility.DisplayProgressBar("Rebuild Localization Font", "Baking SDF atlases...", 0.7f);
            string foundFull = Path.GetFullPath(FoundCharactersPath);
            if (!File.Exists(foundFull)) {
                Debug.LogError($"[Localization] Could not find {foundFull} after merge.");
                return;
            }
            string characters = File.ReadAllText(foundFull);
            BakeFontAssets(characters);
        } finally {
            EditorUtility.ClearProgressBar();
        }
    }

    #region Characters

    public static SortedSet<int> CollectCodePoints(LocalizationMasterDatabase db) {
        SortedSet<int> codePoints = new SortedSet<int>();
        for (char c = ' '; c <= '~'; c++)
            codePoints.Add(c);

        // Latin-1 Supplement (0x00A0 - 0x00FF: ¡, ¿, accented letters, symbols)
        for (int i = 0x00A0; i <= 0x00FF; i++) codePoints.Add(i);

        // Latin Extended-A (0x0100 - 0x017F: Central/Eastern Europe, Turkish)
        for (int i = 0x0100; i <= 0x017F; i++) codePoints.Add(i);

        // Latin Extended-B diacritics & Romanian comma-below
        codePoints.Add(0x0218); // 'Ș'
        codePoints.Add(0x0219); // 'ș'
        codePoints.Add(0x021A); // 'Ț'
        codePoints.Add(0x021B); // 'ț'
        codePoints.Add(0x1E9E); // Capital sharp S 'ẞ'

        // Vietnamese precomposed Latin (0x1EA0 - 0x1EF9)
        for (int i = 0x1EA0; i <= 0x1EF9; i++) codePoints.Add(i);

        // Greek and Coptic (0x0370 - 0x03FF)
        for (int i = 0x0370; i <= 0x03FF; i++) codePoints.Add(i);

        // Cyrillic (0x0400 - 0x04FF: Russian, Ukrainian, Bulgarian)
        for (int i = 0x0400; i <= 0x04FF; i++) codePoints.Add(i);

        // Arabic presentation forms needed by RTLHelper
        for (int i = 0x0600; i <= 0x06FF; i++) codePoints.Add(i);
        for (int i = 0xFB50; i <= 0xFDFF; i++) codePoints.Add(i);
        for (int i = 0xFE70; i <= 0xFEFF; i++) codePoints.Add(i);

        // Common symbols and typographic punctuation
        codePoints.Add(0x25B2); // '▲'
        codePoints.Add(0x2500); // '─'
        codePoints.Add(0x2014); // '—'
        codePoints.Add(0x2013); // '–'
        codePoints.Add(0x2026); // '…'
        codePoints.Add(0x201C); // '“'
        codePoints.Add(0x201D); // '”'
        codePoints.Add(0x201E); // '„'
        codePoints.Add(0x2018); // '‘'
        codePoints.Add(0x2019); // '’'
        codePoints.Add(0x20AC); // '€'
        codePoints.Add(0x00AB); // '«'
        codePoints.Add(0x00BB); // '»'

        // Required glyphs from SteamLanguageList definitions
        foreach (string glyphSet in SteamLanguageList.AllRequiredCharacterSets) {
            Add(glyphSet, codePoints);
        }

        if (db != null) {
            foreach (LocalizationEntry entry in db.AllEntries) {
                Add(entry.EnglishText, codePoints);
                foreach (LanguageTranslation translation in entry.Translations)
                    Add(translation.TranslatedText, codePoints);
            }
        }

        foreach (var lang in SteamLanguageList.All)
            Add(lang.displayName, codePoints);

        return codePoints;
    }

    private static void Add(string text, SortedSet<int> codePoints) {
        if (string.IsNullOrEmpty(text)) return;
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (char.IsControl(c)) continue;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) {
                codePoints.Add(char.ConvertToUtf32(c, text[i + 1]));
                i++;
            } else {
                codePoints.Add(c);
            }
        }
    }

    public static string ToText(IEnumerable<int> codePoints) {
        StringBuilder sb = new StringBuilder();
        foreach (int codePoint in codePoints)
            sb.Append(char.ConvertFromUtf32(codePoint));
        return sb.ToString();
    }

    private static void WriteText(string relativePath, string contents) {
        string full = Path.GetFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, contents, new UTF8Encoding(false));
    }

    #endregion

    #region Merge

    private static string ResolveToPhysicalPath(string assetPath) {
        if (string.IsNullOrEmpty(assetPath)) return null;
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
        if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath)) {
            string relPath = assetPath.Substring(packageInfo.assetPath.Length).TrimStart('/', '\\');
            return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, relPath));
        }
        return Path.GetFullPath(assetPath);
    }

    private static string FindMergeScriptPath() {
        // 1. Prefer the script inside this package (works for git, local, and registry packages)
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalizationFontBuilder).Assembly);
        if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath)) {
            string scriptInPackage = Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, "Editor", "Fonts", "FontMerge", "fontMerge.py"));
            if (File.Exists(scriptInPackage)) return scriptInPackage;
        }

        // 2. Check by package asset GUID
        string guidPath = AssetDatabase.GUIDToAssetPath("77491771ae8b415882f07ad2a9b8aa12");
        if (!string.IsNullOrEmpty(guidPath)) {
            string physical = ResolveToPhysicalPath(guidPath);
            if (File.Exists(physical)) return physical;
        }

        // 3. Search project assets, prioritizing the package or FontMerge directory
        string[] guids = AssetDatabase.FindAssets("fontMerge");
        string fallback = null;
        foreach (var guid in guids) {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.EndsWith("fontMerge.py", StringComparison.OrdinalIgnoreCase)) continue;
            string physical = ResolveToPhysicalPath(p);
            if (!File.Exists(physical)) continue;

            if (p.Contains("com.markv12.localizationsystem") || p.IndexOf("FontMerge", StringComparison.OrdinalIgnoreCase) >= 0) {
                return physical;
            }
            if (fallback == null) fallback = physical;
        }

        return fallback;
    }

    private static bool RunMerge() {
        string mergeScript = FindMergeScriptPath();
        if (string.IsNullOrEmpty(mergeScript) || !File.Exists(mergeScript)) {
            Debug.LogError($"[Localization] fontMerge.py script not found at '{mergeScript ?? "(null)"}'.");
            return false;
        }

        string fontsDir = Path.GetDirectoryName(mergeScript);
        string foundFull = Path.GetFullPath(FoundCharactersPath);
        string ttfFull = Path.GetFullPath(TtfPath);

        if (File.Exists(foundFull)) {
            File.Delete(foundFull);
        }

        string args = $"\"{mergeScript}\"" +
                      $" --chars \"{Path.GetFullPath(CharactersPath)}\"" +
                      $" --out \"{ttfFull}\"" +
                      $" --found \"{foundFull}\"" +
                      $" --fonts_dir \"{fontsDir}\"";

        for (int attempt = 0; attempt < 2; attempt++) {
            string python = EditorPrefs.GetString(PythonPathKey, "python");
            string output;
            int exitCode;
            try {
                exitCode = Run(python, args, out output);
            } catch (Exception e) {
                if (attempt == 0 && AskForPython($"Could not start '{python}': {e.Message}")) continue;
                Debug.LogError($"[Localization] Could not start Python: {e.Message}");
                return false;
            }

            if (exitCode == 0 && File.Exists(foundFull)) {
                Debug.Log($"[Localization] Merged {TtfPath}\n{output}");
                return true;
            }

            if (output.Contains("No module named")) {
                Debug.LogError($"[Localization] fontMerge.py needs fontTools. Run:\n  \"{python}\" -m pip install fonttools\n{output}");
                return false;
            }

            bool notInstalled = output.Contains("was not found") || output.Contains("Microsoft Store");
            if (attempt == 0 && notInstalled && AskForPython($"'{python}' is not a working Python interpreter.")) continue;

            Debug.LogError($"[Localization] fontMerge.py failed (exit {exitCode}):\n{output}");
            return false;
        }
        return false;
    }

    private static int Run(string fileName, string arguments, out string output) {
        ProcessStartInfo info = new ProcessStartInfo(fileName, arguments) {
            WorkingDirectory = Path.GetFullPath("."),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        StringBuilder log = new StringBuilder();
        using (Process process = new Process { StartInfo = info }) {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            output = log.ToString();
            return process.ExitCode;
        }
    }

    private static bool AskForPython(string message) {
        EditorUtility.ClearProgressBar();
        if (!EditorUtility.DisplayDialog("Python Required", $"{message}\n\nLocate python.exe?", "Locate...", "Cancel"))
            return false;
        string path = EditorUtility.OpenFilePanel("Select Python Interpreter", "", "exe");
        if (string.IsNullOrEmpty(path)) return false;
        EditorPrefs.SetString(PythonPathKey, path);
        return true;
    }

    #endregion

    #region SDF Atlases

    private static void BakeFontAssets(string characters) {
        Font source = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
        if (source == null) {
            Debug.LogError($"[Localization] {TtfPath} not found.");
            return;
        }

        FontEngine.InitializeFontEngine();
        if (FontEngine.LoadFontFace(source, PointSize, 0) != FontEngineError.Success) {
            Debug.LogError($"[Localization] Could not load {TtfPath}. Is 'Include Font Data' enabled on the font importer?");
            return;
        }

        TextCoreFontAsset uiFont = BakeUiFont(source, characters);
        TMP_FontAsset tmpFont = BakeTmpFont(source, characters);
        AssetDatabase.SaveAssets();

        if (uiFont != null) AddFallback(PanelTextSettingsPath, "m_FallbackFontAssets", uiFont);
        if (tmpFont != null) AddFallback(TmpSettingsPath, "m_fallbackFontAssets", tmpFont);
    }

    private static TextCoreFontAsset BakeUiFont(Font source, string characters) {
        TextCoreFontAsset asset = AssetDatabase.LoadAssetAtPath<TextCoreFontAsset>(UiFontPath);
        if (asset == null) {
            asset = TextCoreFontAsset.CreateFontAsset(source, PointSize, Padding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, TextCoreAtlasMode.Dynamic, false);
            if (asset == null) {
                Debug.LogError($"[Localization] Could not create {UiFontPath}.");
                return null;
            }
            CreateAssetWithSubAssets(asset, UiFontPath, asset.material, asset.atlasTextures[0]);
        }

        ConfigureForBake(asset, source);
        asset.ClearFontAssetData(true);
        asset.TryAddCharacters(characters, out string missing, false);
        asset.ReadFontAssetDefinition();
        int bakedCount = characters.Length - (missing?.Length ?? 0);
        FinishBake(asset, asset.atlasTextures[0], missing, UiFontPath, bakedCount, isUiFont: true);
        ApplyMaterial(asset.material, asset.atlasTextures[0]);
        return asset;
    }

    private static TMP_FontAsset BakeTmpFont(Font source, string characters) {
        TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontPath);
        if (asset == null) {
            asset = TMP_FontAsset.CreateFontAsset(source, PointSize, Padding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, false);
            if (asset == null) {
                Debug.LogError($"[Localization] Could not create {TmpFontPath}.");
                return null;
            }
            CreateAssetWithSubAssets(asset, TmpFontPath, asset.material, asset.atlasTextures[0]);
        }

        ConfigureForBake(asset, source);
        asset.ClearFontAssetData(true);
        asset.TryAddCharacters(characters, out string missing, false);
        asset.creationSettings = new FontAssetCreationSettings {
            sourceFontFileName = Path.GetFileName(TtfPath),
            sourceFontFileGUID = AssetDatabase.AssetPathToGUID(TtfPath),
            faceIndex = 0,
            pointSize = PointSize,
            pointSizeSamplingMode = 0,
            padding = Padding,
            paddingMode = 0,
            packingMode = 0,
            atlasWidth = AtlasSize,
            atlasHeight = AtlasSize,
            characterSetSelectionMode = 7,
            characterSequence = characters,
            renderMode = (int)GlyphRenderMode.SDFAA,
            includeFontFeatures = false,
        };
        asset.ReadFontAssetDefinition();
        FinishBake(asset, asset.atlasTextures[0], missing, TmpFontPath, asset.characterTable.Count);

        ApplyMaterial(asset.material, asset.atlasTextures[0]);
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder })) {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material != null && material.HasProperty(GradientScaleId)) ApplyMaterial(material, asset.atlasTextures[0]);
        }
        return asset;
    }

    private static void CreateAssetWithSubAssets(ScriptableObject asset, string path, Material material, Texture2D atlas) {
        string dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string name = Path.GetFileNameWithoutExtension(path);
        asset.name = name;
        AssetDatabase.CreateAsset(asset, path);
        if (atlas != null) {
            atlas.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);
        }
        if (material != null) {
            material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(material, asset);
        }
        Debug.Log($"[Localization] Created {path}.");
    }

    private static void ConfigureForBake(ScriptableObject asset, Font source) {
        SetField(asset, "m_FaceInfo", FontEngine.GetFaceInfo());
        SetField(asset, "m_SourceFontFileGUID", AssetDatabase.AssetPathToGUID(TtfPath));
        SetField(asset, "m_SourceFontFile_EditorRef", source);
        SetField(asset, "m_SourceFontFile", source);
        SetField(asset, "m_SourceFontFilePath", string.Empty);
        SetField(asset, "m_AtlasWidth", AtlasSize);
        SetField(asset, "m_AtlasHeight", AtlasSize);
        SetField(asset, "m_AtlasPadding", Padding);
        SetField(asset, "m_AtlasRenderMode", GlyphRenderMode.SDFAA);
        SetField(asset, "m_IsMultiAtlasTexturesEnabled", false);
        SetField(asset, "m_GetFontFeatures", false);
        SetField(asset, "m_ClearDynamicDataOnBuild", false);
        SetEnumField(asset, "m_AtlasPopulationMode", Dynamic);
    }

    private static void FinishBake(ScriptableObject asset, Texture2D atlas, string missing, string path, int baked, bool isUiFont = false) {
        if (!isUiFont) {
            SetEnumField(asset, "m_AtlasPopulationMode", Static);
            SetField(asset, "m_SourceFontFile", null);
        }

        EditorUtility.SetDirty(asset);
        if (atlas != null) {
            SerializedObject texture = new SerializedObject(atlas);
            texture.FindProperty("m_IsReadable").boolValue = isUiFont;
            texture.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(atlas);
        }

        if (!string.IsNullOrEmpty(missing)) {
            Debug.LogError($"[Localization] {path}: {baked} characters baked, but {missing.Length} did not fit in {AtlasSize}x{AtlasSize}.");
        } else {
            Debug.Log($"[Localization] Rebuilt {path} with {baked} characters.");
        }
    }

    private static void ApplyMaterial(Material material, Texture atlas) {
        if (material == null) return;
        material.SetTexture(MainTexId, atlas);
        material.SetFloat(TextureWidthId, AtlasSize);
        material.SetFloat(TextureHeightId, AtlasSize);
        if (material.HasProperty(GradientScaleId)) material.SetFloat(GradientScaleId, Padding + 1);
        EditorUtility.SetDirty(material);
    }

    #endregion

    #region Fallbacks

    private static void AddFallback(string settingsPath, string propertyName, UnityEngine.Object fontAsset) {
        UnityEngine.Object settings = AssetDatabase.LoadMainAssetAtPath(settingsPath);
        if (settings == null) return;

        SerializedObject so = new SerializedObject(settings);
        SerializedProperty list = so.FindProperty(propertyName);
        if (list == null || !list.isArray) return;

        bool present = false;
        for (int i = list.arraySize - 1; i >= 0; i--) {
            UnityEngine.Object entry = list.GetArrayElementAtIndex(i).objectReferenceValue;
            if (entry == fontAsset) present = true;
            else if (entry == null) list.DeleteArrayElementAtIndex(i);
        }
        if (present && !so.hasModifiedProperties) return;

        if (!present) {
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fontAsset;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Localization] {fontAsset.name} set as a fallback in {settingsPath}.");
    }

    #endregion

    #region Reflection

    private static void SetField(object target, string name, object value) {
        FieldInfo field = FindField(target, name);
        if (field == null) return;
        field.SetValue(target, value);
    }

    private static void SetEnumField(object target, string name, int value) {
        FieldInfo field = FindField(target, name);
        if (field == null) return;
        field.SetValue(target, Enum.ToObject(field.FieldType, value));
    }

    private static FieldInfo FindField(object target, string name) {
        for (Type type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null) return field;
        }
        return null;
    }

    #endregion
}
