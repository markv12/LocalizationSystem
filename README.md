# Unity Localization System

A centralized, production-ready localization system for Unity games supporting **TextMeshPro**, **UI Toolkit**, automated **OpenAI Batch API translations**, and one-click **Noto font merging and SDF atlas baking**.

---

## Features

- **Standard Assets / Assembly-CSharp Compatible:** No `.asmdef` files required. Drop directly into `Assets/` (via Git Submodule, symlink, or clone) with zero compile-boundary friction.
- **Decoupled Architecture:** Integrates cleanly with any game's settings/save system via `Localizer.GetSavedLanguage` / `SetSavedLanguage` delegates (with built-in `PlayerPrefs` fallback).
- **Comprehensive Language Support:** Pre-configured for 30+ Steam languages including **Bahasa Melayu (Malay)**, Bahasa Indonesia, CJK, Cyrillic, Thai, and Arabic (RTL).
- **RTL & Arabic Text Shaping:** `RTLHelper` interfaces directly with your project's `RTLTMPro` installation (e.g. in `Assets/Standard Assets/RTLTMPro`).
- **Dynamic Database Categories:** `LocalizationMasterDatabase` supports custom entry categories (Items, Instruments, Dialogue, Scales) configured dynamically in the Inspector or synced via custom scripts.
- **Automated OpenAI Batch Translations:** REST-based client for OpenAI's Batch API (supporting models like `gpt-5.6-luna`, `gpt-5.5`, `gpt-4o`) with JSON schema enforcement, reasoning effort control, and automatic polling.
- **One-Click Noto Font Merging & SDF Baking:** Extracts characters in use from the database, runs `fontMerge.py` to produce a unified `NotoAll.ttf` from 7 Noto source font families, and automatically bakes TextMeshPro and UI Toolkit SDF atlases inside Unity.
- **UI Toolkit & TextMeshPro Integration:**
  - `Loc.Get`, `Loc.Format`, `Loc.Choices`: High-level C# string helpers with automatic RTL shaping and English fallbacks.
  - `UILocalization` & `UIDocumentLocalizer`: Automatic element tree walking based on UXML document name and element IDs.
  - `UxmlStringSync`: Scans UXML documents and extracts new/modified strings to CSV for translation.
  - `TextFieldLocalizer`, `FontLocalizer`, `SpriteLocalizer`: Components for TextMeshPro text and localized sprite swaps.
  - `SteamPageTranslatorWindow`: Editor window for translating Steam store page CSVs.

---

## Installation & Sharing Options (No ASMDEF)

Because this repository does not use `.asmdef` files, it compiles directly into your project's default `Assembly-CSharp` and `Assembly-CSharp-Editor` assemblies.

### Option 1: Git Submodule inside `Assets/` (Recommended for Git projects)
In your game repository root, add this repo as a submodule:
```bash
git submodule add https://github.com/<your-account>/LocalizationSystem.git Assets/LocalizationSystem
```
To update the shared system across all projects:
```bash
git submodule update --remote Assets/LocalizationSystem
```

### Option 2: Directory Junction / Symlink (Ideal for local multi-project dev)
On Windows, create a directory junction pointing from your game's `Assets` folder to your shared clone:
```powershell
cmd /c mklink /J "D:\YourGame\Assets\LocalizationSystem" "D:\LocalizationSystem"
```
Any edits made to the localization code in any project are instantly reflected across all linked projects.

---

## Prerequisites

- **TextMeshPro** (included with modern Unity / `com.unity.ugui`).
- **RTLTMPro** ([github.com/pnarimani/RTLTMPro](https://github.com/pnarimani/RTLTMPro)), typically placed in `Assets/Standard Assets/RTLTMPro`.
- **Python 3 with fonttools** (only for rebuilding Noto fonts):
  ```bash
  python -m pip install fonttools
  ```

---

## Getting Started

### 1. Create the Master Database
1. In your project's `Assets/Resources` folder, create a database via **Assets → Create → Localization → Master Database**. Name it `LocalizationMasterDatabase`.
2. Configure **Game Genre**, **Project Tone**, and **Global Context** to give the AI translator accurate context.
3. Click **Add All Languages** to populate the target Steam languages.

### 2. Configure Fonts & Settings
1. Create a settings asset in `Assets/Resources` via **Assets → Create → Localization → Font Localization Settings**. Name it `FontLocalizationSettings`.
2. Assign your default English font and fallback/other fonts.
3. If using Noto font generation:
   - Ensure `python -m pip install fonttools` is installed.
   - In `LocalizationMasterDatabase`, click **Rebuild Localization Font**.

### 3. Language Storage Hook (Optional)
By default, the system stores and retrieves the active language in `PlayerPrefs` (`"SelectedLanguage"`). To connect it to your game's own settings manager:
```csharp
void Awake() {
    Localizer.GetSavedLanguage = () => GameSettings.Language;
    Localizer.SetSavedLanguage = (lang) => { GameSettings.Language = lang; };
}
```

---

## Code Usage Examples

### C# UI & Logic
```csharp
// Simple string lookup with RTL shaping and English fallback
string title = Loc.Get("menu.title");

// Formatted string ({0}, {1}) with translator error resilience
string welcome = Loc.Format("menu.welcome", playerName);

// Dropdown options
List<string> options = Loc.Choices("opt.easy", "opt.medium", "opt.hard");

// Changing language at runtime
Localizer.LoadLanguage("japanese");
```

### TextMeshPro UI
- Attach `TextFieldLocalizer` to any GameObject with a `TMP_Text` component. Set the `locKey`.
- Attach `FontLocalizer` to swap fonts or material outline presets when language changes.

### UI Toolkit (UXML)
- Name your elements in UI Builder (`resume-button`, `settings-button`).
- Add `UIDocumentLocalizer` to the GameObject with the `UIDocument`. Text is automatically keyed as `<document-name>.<element-name>`.
- Add USS class `no-loc` to elements that should never be translated.

---

## Translation Workflow with OpenAI Batch API

### 1. Configure OpenAI Credentials
Create a `.env` file in your Unity project root (next to `Assets/` and `ProjectSettings/`):

```env
# Required: Your OpenAI API key
OPENAI_API_KEY=sk-proj-...

# Optional / Recommended: Required if using legacy user keys (sk-...) or project scoping
OPENAI_PROJECT_ID=proj_...

# Optional: Required if your account belongs to multiple OpenAI organizations
OPENAI_ORG_ID=org-...
```

> **Note on OpenAI Project & Organization IDs:**
> - If you use a modern **Project API key** (`sk-proj-...`), the project is usually baked into the key, but specifying `OPENAI_PROJECT_ID` ensures explicit scoping.
> - If you use a legacy **User API key** (`sk-...`), OpenAI Batch requires `OPENAI_PROJECT_ID` to associate uploaded files with batch jobs; otherwise you may encounter the error: `Cannot find file, or organization does not have access to it`.
> - If your OpenAI account belongs to multiple organizations, specify `OPENAI_ORG_ID` (aliases `OPENAI_ORGANIZATION` and `OPENAI_PROJECT` are also supported).

### 2. Translating Strings in Unity
1. Open **Window → Localization → Localization Dashboard**.
2. Click **Prepare Translation Batch** (hashes English text + context to detect new or dirty strings).
3. Click **Send to AI** (uploads the `.jsonl` payload and creates the batch job).
4. Once completed by OpenAI, click **Check API Status** to automatically download results and update `LocalizationMasterDatabase`.
5. Click **Bake** to compile the strings into runtime assets in `Assets/Resources/Languages/`.

---

## Repository Structure

```text
LocalizationSystem/
├── Runtime/
│   ├── Core/
│   │   ├── Localizer.cs                 # Core runtime engine (partial, decoupled)
│   │   ├── LanguageData.cs              # Runtime string database asset
│   │   ├── LanguageListAsset.cs         # Baked language list asset
│   │   ├── SteamLanguageList.cs         # 30+ Steam languages including Malay & ISO helpers
│   │   ├── RTLHelper.cs                 # Arabic & RTL text shaper (calls project's RTLTMPro)
│   │   ├── Loc.cs                       # High-level C# UI helper
│   │   └── CSVParser.cs                 # RFC-compliant CSV reader & writer
│   ├── Fonts/
│   │   ├── FontLocalizationSettings.cs  # Multi-font, outline material & language split configuration
│   │   ├── FontLocalizer.cs             # TMPro font and material applicator
│   │   └── LanguageChangedHandler.cs    # Base class for reactive localization components
│   ├── Components/
│   │   ├── TextFieldLocalizer.cs        # TMPro text localizer
│   │   ├── SpriteLocalizer.cs           # Localized sprite replacement & overlay
│   │   ├── UISpriteLocalizer.cs         # uGUI image localizer
│   │   └── WorldSpriteLocalizer.cs      # 2D world sprite renderer localizer
│   └── UIToolkit/
│       ├── UILocalization.cs            # UI Toolkit element tree walker
│       ├── UIDocumentLocalizer.cs       # Auto-localizing MonoBehaviour for UIDocuments
│       └── UIToolkitLocalizer.cs        # C# dynamic UI element styler
├── Editor/
│   ├── Database/
│   │   ├── LocalizationMasterDatabase.cs       # Dynamic category database
│   │   ├── LocalizationMasterDatabaseEditor.cs # Inspector tools
│   │   ├── LocalizationEntryEditorWindow.cs    # Single-entry modal editor
│   │   ├── LocalizationDashboard.cs            # Matrix status dashboard
│   │   └── LanguageListDrawer.cs               # Advanced dropdown drawers
│   ├── Processing/
│   │   ├── LocalizationProcessor.cs     # Batch prep, MD5 hashing, baking to Resources
│   │   ├── LocalizationCsv.cs           # CSV schema definitions
│   │   ├── LocalizationCsvImporter.cs   # Auto-importer for drop CSVs
│   │   ├── BatchHandler.cs              # OpenAI batch request preparation & processing
│   │   └── EditorMainThreadDispatcher.cs# Thread-safe main thread queue
│   ├── OpenAI/
│   │   ├── OpenAIBatchAPI.cs            # REST client for OpenAI Files and Batches
│   │   └── SteamPageTranslatorWindow.cs # Steam store page translation tool
│   ├── Fonts/
│   │   ├── LocalizationFontBuilder.cs   # Automated font harvester and SDF baker
│   │   └── FontMerge/
│   │       ├── fontMerge.py             # python fontTools subsetter
│   │       └── *.ttf                    # 7 source Noto TTF files
│   └── UIToolkit/
│       └── UxmlStringSync.cs            # Syncs strings from .uxml files to database
└── Samples~/
    └── YarnSpinner/
        └── LocalizerLineProvider.cs     # Yarn Spinner dialogue bridge
```
