param(
    [string]$UnityEditorPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.5f1/Editor/Unity.exe',
    [string]$EventCsvPath = '',
    [string]$StoreCsvPath = ''
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $UnityEditorPath)) { throw "Unity editor not found: $UnityEditorPath" }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$projectPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'UnityProject'))
$assetsPath = Join-Path $projectPath 'Assets'
# Rebuild only this harness's generated Assets folder so removed sources cannot linger.
if (Test-Path -LiteralPath $assetsPath) {
    $resolvedAssets = (Resolve-Path -LiteralPath $assetsPath).ProviderPath
    if ($resolvedAssets -ne [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'UnityProject/Assets'))) { throw 'Unexpected generated Assets path.' }
    Remove-Item -LiteralPath $resolvedAssets -Recurse -Force
}
$editorPath = Join-Path $projectPath 'Assets/Editor'
New-Item -ItemType Directory -Force -Path $editorPath, (Join-Path $projectPath 'Packages'), (Join-Path $projectPath 'ProjectSettings') | Out-Null
[IO.File]::WriteAllText((Join-Path $projectPath 'Packages/manifest.json'), '{"dependencies":{}}')
$version = (Get-Item -LiteralPath $UnityEditorPath).VersionInfo.ProductVersion
[IO.File]::WriteAllText((Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: $version`n")
$sourceFiles = @(
    'Runtime/Core/CSVParser.cs', 'Runtime/Core/SteamLanguageList.cs', 'Runtime/Core/LanguageData.cs', 'Runtime/Core/LanguageListAsset.cs',
    'Runtime/Core/Localizer.Categories.cs', 'Runtime/Core/LocalizedPhraseBag.cs',
    'Editor/Database/LocalizationMasterDatabase.cs', 'Editor/Database/LanguageListDrawer.cs',
    'Editor/OpenAI/OpenAIJson.cs', 'Editor/OpenAI/OpenAITranslationModels.cs', 'Editor/OpenAI/OpenAIBatchAPI.cs',
    'Editor/OpenAI/SteamContentCsv.cs', 'Editor/OpenAI/SteamContentTranslation.cs',
    'Editor/OpenAI/SteamContentTranslatorWindow.cs', 'Editor/Processing/BatchHandler.cs',
    'Editor/Processing/LocalizationProcessor.cs', 'Editor/Processing/LocalizationCategoryBaker.cs', 'Editor/Processing/EditorMainThreadDispatcher.cs'
)
foreach ($source in $sourceFiles) { Copy-Item -LiteralPath (Join-Path $repoRoot $source) -Destination $editorPath -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SteamTranslationRegression.cs') -Destination $editorPath -Force
$report = Join-Path $projectPath 'result.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$log = Join-Path $projectPath 'regression.log'
$arguments = "-batchmode -nographics -projectPath `"$projectPath`" -executeMethod SteamTranslationRegression.Run -logFile `"$log`""
if ($EventCsvPath) { $arguments += " -steamEventCsv `"$EventCsvPath`"" }
if ($StoreCsvPath) { $arguments += " -steamStoreCsv `"$StoreCsvPath`"" }
$process = Start-Process -FilePath $UnityEditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) {
    Get-Content -LiteralPath $log -Tail 100
    throw "Unity regression failed (exit $($process.ExitCode)). See $log"
}
