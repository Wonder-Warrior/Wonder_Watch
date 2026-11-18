$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$srcFile = Join-Path $scriptDir "src\Program.cs"
$manifestFile = Join-Path $scriptDir "src\App.manifest"
$iconFile = Join-Path $repoRoot "favicon.ico"
$libDir = Join-Path $scriptDir "lib"
$extDir = Join-Path $scriptDir "extensions\ublock-origin"
$outExe = Join-Path $scriptDir "WonderWatch.exe"
$zipOut = Join-Path $scriptDir "WonderWatch-Desktop-win-x64.zip"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "C# compiler not found at $csc"
}

if (-not (Test-Path (Join-Path $extDir "manifest.json"))) {
    throw "Bundled uBlock Origin extension not found at $extDir"
}

Write-Host "Copying WebView2 runtime libraries..."
Copy-Item (Join-Path $libDir "Microsoft.Web.WebView2.Core.dll") $scriptDir -Force
Copy-Item (Join-Path $libDir "Microsoft.Web.WebView2.WinForms.dll") $scriptDir -Force
Copy-Item (Join-Path $libDir "WebView2Loader.dll") $scriptDir -Force

Write-Host "Compiling WonderWatch.exe..."
$cscArgs = @(
    "/nologo",
    "/optimize+",
    "/platform:x64",
    "/target:winexe",
    "/out:$outExe",
    "/win32manifest:$manifestFile",
    "/r:$(Join-Path $scriptDir 'Microsoft.Web.WebView2.Core.dll')",
    "/r:$(Join-Path $scriptDir 'Microsoft.Web.WebView2.WinForms.dll')",
    "/r:System.Windows.Forms.dll",
    "/r:System.Drawing.dll"
)
if (Test-Path $iconFile) {
    $cscArgs += "/win32icon:$iconFile"
}
$cscArgs += $srcFile

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Packaging WonderWatch-Desktop-win-x64.zip..."
if (Test-Path $zipOut) {
    Remove-Item $zipOut -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$stageDir = Join-Path $env:TEMP ("ww_stage_" + [Guid]::NewGuid().ToString("N"))
$appRoot = Join-Path $stageDir "WonderWatch-Desktop"
New-Item -ItemType Directory -Force -Path $appRoot | Out-Null

Copy-Item $outExe $appRoot -Force
Copy-Item (Join-Path $scriptDir "Microsoft.Web.WebView2.Core.dll") $appRoot -Force
Copy-Item (Join-Path $scriptDir "Microsoft.Web.WebView2.WinForms.dll") $appRoot -Force
Copy-Item (Join-Path $scriptDir "WebView2Loader.dll") $appRoot -Force
Copy-Item (Join-Path $scriptDir "README.md") $appRoot -Force
Copy-Item (Join-Path $scriptDir "extensions") $appRoot -Recurse -Force

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $appRoot,
    $zipOut,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true
)

Remove-Item $stageDir -Recurse -Force

$zipSizeMB = [Math]::Round((Get-Item $zipOut).Length / 1MB, 2)
Write-Host "Build complete: $outExe"
Write-Host "Package ready: $zipOut ($zipSizeMB MB)"
