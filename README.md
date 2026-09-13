# Unity Localization System

A lightweight, centralized localization system for Unity supporting **TextMeshPro**, **UI Toolkit**, automated **OpenAI batch translations**, and **SDF font atlas baking**.

---

## Installation

Add the package via Unity Package Manager using the Git URL:
```text
https://github.com/markv12/LocalizationSystem.git
```
Or add directly to `Packages/manifest.json`:
```json
"com.markv12.localizationsystem": "https://github.com/markv12/LocalizationSystem.git"
```

### Dependencies
- **TextMeshPro** (`com.unity.ugui`)
- **RTLTMPro** (optional, for Arabic / RTL support: [github.com/pnarimani/RTLTMPro](https://github.com/pnarimani/RTLTMPro))
- **Python 3 + fonttools** (optional, only needed when rebuilding merged Noto font atlases: `pip install fonttools`)

---

## Setup

### 1. Create Assets
1. **Master Database**: **Assets → Create → Localization → Master Database** (can be placed anywhere in your project, e.g. `Assets/Localization/`).
   - Add target languages and project context for AI translation.
2. **Font Settings**: **Assets → Create → Localization → Font Localization Settings** (create in `Assets/Resources/` named `FontLocalizationSettings`).
   - Assign fonts for your languages (Regular, Title, Paragraph).

### 2. Connect Saved Language (Optional)
By default, the active language is saved in `PlayerPrefs` (`"SelectedLanguage"`). To hook into your existing settings or save system:
```csharp
Localizer.GetSavedLanguage = () => MySettings.Language;
Localizer.SetSavedLanguage = (lang) => MySettings.Language = lang;
```

---

## How to Use

### In the Inspector (Components)

- **TextMeshPro Text (`TextFieldLocalizer`)**:
  Attach to any GameObject with a `TMP_Text` component.
  - `locKey`: The localization key to display (falls back to existing text if blank).
  - `type`: Select font variant (`Regular`, `Title`, `Paragraph`).
  - `outline`: Select outline style (`None`, `Black`, `White`). Outlines are generated dynamically as true outer outlines without manual material presets.

- **Font / Outline Only (`FontLocalizer`)**:
  Attach to any `TMP_Text` to swap fonts and outline styling per language without managing string keys.

- **Localized Sprites (`UISpriteLocalizer` / `WorldSpriteLocalizer`)**:
  Attach to a UI `Image` or 2D `SpriteRenderer` to swap sprite graphics or overlay localized text per language.

- **UI Toolkit (`UIDocumentLocalizer`)**:
  Attach to a GameObject with a `UIDocument`. Elements are automatically matched by element name: `<document-name>.<element-name>`.
  - Add USS class `no-loc` to elements that should never be translated.

### In C# Scripts

```csharp
// Simple text lookup (includes automatic RTL shaping and English fallback)
string text = Loc.Get("menu.play");

// Formatted strings with arguments
string score = Loc.Format("game.score", currentScore);

// Dropdown options
List<string> options = Loc.Choices("opt.easy", "opt.normal", "opt.hard");

// Change language at runtime
Localizer.LoadLanguage("japanese");

// Listen for language changes
Localizer.LanguageChangedEvent += OnLanguageChanged;
```

---

## Translating & Baking Strings

Open **Window → Localization → Localization Dashboard**:

1. **Add Entries**: Add strings and keys directly or import CSVs.
2. **AI Translation (OpenAI Batch API)**:
   - Create a `.env` file in the project root with `OPENAI_API_KEY=sk-...`.
   - Click **Prepare Translation Batch** to generate the request payload.
   - Click **Send to AI** to start the batch job.
   - Click **Check API Status** to retrieve finished translations into the database.
3. **Bake to Resources**:
   - Click **Bake** to compile the strings into runtime assets under `Assets/Resources/Languages/`.

---

## Rebuilding Localization Fonts

When new characters are introduced:
1. In `LocalizationMasterDatabase`, click **Rebuild Localization Font**.
2. The tool extracts all characters in use across all languages, merges the necessary Noto font subsets, and bakes the SDF font asset automatically.
