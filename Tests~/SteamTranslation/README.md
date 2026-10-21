# Steam content translation regression checks

Run from the repository root with an installed Unity editor:

```powershell
& './Tests~/SteamTranslation/RunUnityRegression.ps1' -UnityEditorPath 'C:/Program Files/Unity/Hub/Editor/6000.6.5f1/Editor/Unity.exe'
```

Optionally pass `-EventCsvPath 'C:/path/to/event_localization.csv'` to check a real Steam `field,language,value` event export. The optional fixture check expects this example's four fields and populated English title/body. The input is read without modification.

`-StoreCsvPath 'C:/path/to/storepage_all.csv'` checks the reported store export fixture: the previous behavior produced 94 requests, while the corrected scope produces only two Malay descriptions, even with Arabic in the shared target languages. A synthetic store fixture tests skipping system requirements and Arabic store fields, preserving existing translations, request previews, and safe importing of an already submitted batch. It also verifies that Arabic remains available for in-game translations and events. Fixture inputs are never modified.

The script creates an isolated, ignored `UnityProject` next to itself, rebuilds its generated `Assets` folder with the production sources, and runs the checks using Unity's real JsonUtility, asset database, and window lifecycle. No API requests are sent. Coverage includes CSV quoting and metadata, Korean aliases, existing and missing translations, empty fields, store translation scope, request previews, markup/URL/placeholder/name preservation, prompts, source snapshots, batch result merging, and database selection. It also checks that Steam and regular translation requests use the same model, effort, budget, target languages, and game guidance, and that shared settings persist in the master database.

Shared batch workflow checks use local substitutes for upload, creation, and download operations. They cover JSONL encoding, request limits, temporary-file cleanup on success and failure, request and batch error logging, unfinished and terminal batches, partial success, and preserving pending entries when output cannot be downloaded.

`UnityProject/result.txt` contains the assertion result; `UnityProject/regression.log` contains compiler diagnostics and expected warnings for deliberately rejected results. The only substitutes are the network operations and runtime `Localizer` resource constants, avoiding API calls and unrelated game/runtime dependencies in the isolated project.
