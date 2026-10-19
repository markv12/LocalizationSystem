# Steam content translation regression checks

Run from the repository root with an installed Unity editor:

```powershell
& './Tests~/SteamTranslation/RunUnityRegression.ps1' -UnityEditorPath 'C:/Program Files/Unity/Hub/Editor/6000.6.5f1/Editor/Unity.exe'
```

Optionally pass `-EventCsvPath 'C:/path/to/event_localization.csv'` to check a real Steam `field,language,value` event export. The optional fixture check expects this example's four fields and populated English title/body. The input is read without modification.

The script creates an isolated, ignored `UnityProject` next to itself, copies the production editor sources, and runs the checks using Unity's real JsonUtility, asset database, and window lifecycle. No API requests are sent. Coverage includes CSV quoting and metadata, Korean aliases, existing and missing translations, empty fields, invalid inputs, markup/URL/placeholder/name preservation, prompts, source snapshots, batch result merging, and saved settings restored by a new window.

`UnityProject/result.txt` contains the assertion result; `UnityProject/regression.log` contains compiler diagnostics and expected warnings for deliberately rejected results. The only substitute is the runtime `Localizer.DEFAULT_LANGUAGE` constant, avoiding unrelated game/runtime dependencies in the isolated project.
