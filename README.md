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
- **RTLTMPro** (required; install [RTLTMPro](https://github.com/pnarimani/RTLTMPro) before this package)
- **Python 3 + fonttools** (optional, only needed when rebuilding merged Noto font atlases: `pip install fonttools`)

Translation tools use Unity's built-in `JsonUtility`; no external JSON library is required.

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

Runtime language, font, and sprite caches reset at each Play Mode entry, including when domain reload is disabled. Language-change subscriptions reconnect when components are enabled. The persisted language and custom save callbacks are preserved; assign callbacks to a persistent save service or reassign them during project startup if they capture scene objects.

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

Text lookup and UI components fall back to the baked English entry when a key is missing from the selected language. `TryGetText` remains a selected-language-only lookup; use `TryGetTextOrEnglish` when you want fallback without displaying a raw key.

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

The database model picker supports **GPT-6 Astra**, **GPT-6.1 Sol**, **GPT-6.1 Terra**, **GPT-6 Sol**, **GPT-6 Luna**, the GPT-5.6 tiers, GPT-5.5, GPT-5.2, GPT-4o, and GPT-4o Mini. Regular translations and the Steam Content Translator share the master database's model, reasoning effort, output budget, and target languages. New databases default to `gpt-6-luna` with low reasoning effort; existing assets retain their saved model. Game Title, Game Genre, Game Tone, Global Context, and Protected Names are also shared. The game title and protected names (one per line) are preserved exactly in both AI translation workflows.

[GPT-6.1 Terra](https://developers.openai.com/api/docs/models/gpt-6.1-terra) uses model ID `gpt-6.1-terra` and supports reasoning efforts from `None` through `Max` with the existing text-only batch workflows.

Both workflows use text-only **Chat Completions through the Batch API**, which the [official GPT-6 guide](https://developers.openai.com/api/docs/guides/latest-model) supports. The database workflow uses strict JSON Schema; Steam translations return plain text. Requests omit reasoning parameters for GPT-4o models and validate supported reasoning efforts before upload. `None` is unavailable for Astra and GPT-6.1 Sol; `Max` is unavailable for GPT-5.5 and GPT-5.2. Unknown model IDs are rejected until their capabilities are added to `OpenAITranslationModels`.

**Max Completion Tokens** is the shared output budget, defaults to 16,000 tokens per request, and includes reasoning tokens. Increase it if a response exhausts its budget (up to 128,000 for the supported reasoning models or 16,384 for GPT-4o models). Refusals, truncated responses, invalid JSON, duplicate result keys, changed protected names, and changed formatting placeholders are skipped. Successful partial results from completed, expired, or cancelled batches are imported; unresolved database translations return to Untranslated/Dirty for retry. Failed downloads leave pending translations intact for another status check.

New database submissions store the request ID and source hash to reject stale results and preserve approved translations. The Steam window stores source hashes and request IDs for its latest submitted batch and preserves CSV column order, row order, language spelling, and row metadata. Steam batch IDs entered manually lack submission snapshots, so source changes cannot be detected for those results. Steam CSVs must have unique `key`/`language` (store) or `field`/`language` (event) pairs; keys cannot contain `|` and must fit the Batch API's 64-character custom ID limit with the language suffix. API model availability still depends on your project's access and limits.

Per translation, pending batch ID, request ID, and submitted source hash share a single hidden `BatchTracking` string. Its versioned, length-prefixed encoding supports underscores and delimiters without ambiguity. The field is cleared after a successful import, terminal-batch recovery, CSV replacement, or manual reset; serialization also clears leftover tracking on any non-pending translation. The single **Reset Pending Translations** recovery button clears tracking for the selected category or all entries, including languages removed from the target list. Pending entries with previous text become Dirty; those without text become Untranslated. Completed and approved translations retain their text and status. The separate `SourceHash` remains because it identifies the source of the accepted translation.

Upgrades assume there are no active batches. Previous batch-tracking fields are ignored; translation text, status, and accepted-source hashes remain intact. No batch migration or asset-format conversion is needed.

Offline regression checks and optional compilation against installed Unity assemblies are documented in `Tests~/OpenAITranslation/README.md`.

### Steam Store Pages and Events

Open **Window → Localization → Steam Content Translator**.

1. Choose **Store Page** for a CSV with `key,language,text` columns, or **Event** for Steam's CSV export with `field,language,value` columns. The other event CSV layouts and XML are not supported.
2. Select the source CSV. Fields with empty English text are left empty; existing target translations are preserved. Store exports skip all `app[content][sysreqs]` fields and skip Arabic requests/imports because Arabic is not available as a Steam UI language ([Steamworks language support](https://partner.steamgames.com/doc/store/localization/languages)). Arabic remains available for in-game translations and events; it is not removed from the shared language selection. Other selected **Target Languages** can have missing rows added, including languages absent from the source CSV. Use the request-count preview to check which languages will be translated.
3. Select your **Master Database**. Steam uses the same game details, protected names, model, reasoning effort, **Max Completion Tokens**, and **Target Languages** as regular translations. Edit these fields in the translator or the database Inspector; changes in the translator save automatically to that database asset and apply to both workflows. Select target languages explicitly; there is no separate Steam settings section.
4. For events, optionally add **Event Notes** specific to this announcement. Notes stay with the window's selected source and clear when its path changes; they are excluded from the shared settings. Each field is translated with the full English event as reference.
5. Review the automatic request-count preview, grouped by language, then **Submit Batch**. Use **Check/Process Batch** when results are ready. Choose an output filename (default: `<source>_translated.csv` beside the original). Output uses UTF-8 with a BOM, and the source file cannot be overwritten. Result importing uses the same field/language scope, including for previously submitted batches, so system requirements and unselected languages remain untouched.

BBCode tags and their order, URLs, image references, placeholders, the game title, and protected names are validated before accepting results. Rejected results are reported in the Console. Event `korean` and project `koreana` codes map to the same language; original CSV spellings are retained, and new Korean event rows use `korean`. Steam source snapshots include the shared game title, genre, tone, Global Context, protected names, event notes, and the full English event, rejecting results if that guidance changes while a batch is pending. Keep the original CSV path, content type, notes, and guidance until processing completes. Steam CSV paths, content type, event notes, batch IDs, and submission snapshots remain window state; shared translation settings persist in the master database.

Unity regression checks, including an optional check against a real event export, are documented in `Tests~/SteamTranslation/README.md`.

---

## Rebuilding Localization Fonts

When new characters are introduced:
1. In `LocalizationMasterDatabase`, click **Rebuild Localization Font**.
2. The tool extracts all characters in use across all languages, merges the necessary Noto font subsets, and bakes the SDF font asset automatically.


## Random text from additional categories

Additional Categories in the master database are baked into one shared
`Resources/LocalizationCategories.json` catalogue. Per-language assets remain
flat key/value arrays. Every bake regenerates both the catalogue and language
assets, including automatic bakes when a master database asset is imported or
moved (regardless of filename), after an editor reload, and before player builds.
Unchanged output assets are preserved.

```csharp
if (Localizer.TryGetRandomKey("Crowd.Reactions", out string key)) {
    string line = Localizer.GetText(key);
}

// All format strings in this category must share the same argument meanings.
if (Localizer.TryGetRandomText("Hints.WithCount", out string hint, count)) {
    ShowHint(hint);
}

// For no repeats within a cycle, keep one bag per consumer/category.
var bag = new LocalizedPhraseBag("Crowd.Reactions");
if (bag.TryNext(out string phraseKey)) ShowLine(Localizer.GetText(phraseKey));
```

`GetCategoryKeys` returns a read-only list. Unknown/empty categories return an
empty list; Try methods return false. Selection uses the same IDs for every
language, preserving normal English fallback. Keys must be globally unique and
category names must be unique (case-insensitively). A category's entries should
use the same formatting argument contract; translations may reorder numbered
placeholders. Keep grammar-dependent substitutions in complete translated lines.
