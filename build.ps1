param([switch]$RunTests)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$assetDirectory = Join-Path $projectRoot 'assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$iconPath = Join-Path $assetDirectory 'app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    & (Join-Path $assetDirectory 'generate-icon.ps1') -OutputPath $iconPath
}
$appSources = @('Program.cs', 'MainForm.cs', 'DragFeedbackGrid.cs', 'RenameEngine.cs') | ForEach-Object { Join-Path $projectRoot $_ }
$appPath = Join-Path $projectRoot 'FileNameNormalizer.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 "/out:$appPath" "/win32icon:$iconPath" "/resource:$iconPath,FileNameNormalizer.AppIcon" "/win32manifest:$(Join-Path $projectRoot 'app.manifest')" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $appSources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Output "Built: $appPath"
if ($RunTests -and (Test-Path -LiteralPath (Join-Path $projectRoot 'EngineTests.cs'))) {
    $testDirectory = Join-Path $projectRoot 'tests'
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
    $testExe = Join-Path $testDirectory 'EngineTests.exe'
    & $compiler /nologo /target:exe /platform:anycpu /optimize+ /utf8output /codepage:65001 "/out:$testExe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'RenameEngine.cs') (Join-Path $projectRoot 'EngineTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
}
