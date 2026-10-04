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

The database model picker supports **GPT-6 Astra**, **GPT-6.1 Sol**, **GPT-6 Sol**, **GPT-6 Luna**, the GPT-5.6 tiers, GPT-5.5, GPT-5.2, GPT-4o, and GPT-4o Mini. New databases default to `gpt-6-luna` with low reasoning effort; existing assets retain their saved model. The Steam Page Translator has its own model, effort, and output budget controls, defaulting to `gpt-6.1-sol` with medium effort.

Both workflows use text-only **Chat Completions through the Batch API**, which the [official GPT-6 guide](https://developers.openai.com/api/docs/guides/latest-model) supports. The database workflow uses strict JSON Schema; Steam translations return plain text. Requests omit reasoning parameters for GPT-4o models and validate supported reasoning efforts before upload. `None` is unavailable for Astra and GPT-6.1 Sol; `Max` is unavailable for GPT-5.5 and GPT-5.2. Unknown model IDs are rejected until their capabilities are added to `OpenAITranslationModels`.

**Output Token Budget** defaults to 16,000 tokens per request and includes reasoning tokens. Increase it if a response exhausts its budget (up to 128,000 for the supported reasoning models or 16,384 for GPT-4o models). Refusals, truncated responses, invalid JSON, duplicate result keys, and changed formatting placeholders are skipped. Successful partial results from completed, expired, or cancelled batches are imported; unresolved database translations return to Untranslated/Dirty for retry. Failed downloads leave pending translations intact for another status check.

New database submissions store the request ID and source hash to reject stale results and preserve approved translations. The Steam window stores source hashes for its latest submitted batch and preserves CSV column order and row metadata. Steam batch IDs entered manually lack submission snapshots, so source changes cannot be detected for those results. Steam CSVs must have unique `key`/`language` pairs; keys cannot contain `|` and must fit the Batch API's 64-character custom ID limit with the language suffix. API model availability still depends on your project's access and limits.

Per translation, pending batch ID, request ID, and submitted source hash share a single hidden `BatchTracking` string. Its versioned, length-prefixed encoding supports underscores and delimiters without ambiguity. The field is cleared after a successful import, terminal-batch recovery, CSV replacement, or manual reset; serialization also clears leftover tracking on any non-pending translation. The single **Reset Pending Translations** recovery button clears tracking for the selected category or all entries, including languages removed from the target list. Pending entries with previous text become Dirty; those without text become Untranslated. Completed and approved translations retain their text and status. The separate `SourceHash` remains because it identifies the source of the accepted translation.

Upgrades assume there are no active batches. Previous batch-tracking fields are ignored; translation text, status, and accepted-source hashes remain intact. No batch migration or asset-format conversion is needed.

Offline regression checks and optional compilation against installed Unity assemblies are documented in `Tests~/OpenAITranslation/README.md`.

---

## Rebuilding Localization Fonts

When new characters are introduced:
1. In `LocalizationMasterDatabase`, click **Rebuild Localization Font**.
2. The tool extracts all characters in use across all languages, merges the necessary Noto font subsets, and bakes the SDF font asset automatically.
