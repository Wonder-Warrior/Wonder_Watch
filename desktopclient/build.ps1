$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$srcFile = Join-Path $scriptDir "src\Program.cs"
$installerSrcFile = Join-Path $scriptDir "src\Installer.cs"
$versionSrcFile = Join-Path $scriptDir "src\VersionInfo.cs"
$versionJsonPath = Join-Path $repoRoot "version.json"
$appVersion = "2.4.1"
if (Test-Path $versionJsonPath) {
    $verData = Get-Content $versionJsonPath -Raw | ConvertFrom-Json
    if ($verData.desktop -and $verData.desktop.version) {
        $parts = $verData.desktop.version.Split('.')
        if ($parts.Length -eq 3) {
            $appVersion = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
            $verData.desktop.version = $appVersion
            $verData | ConvertTo-Json -Depth 4 | Set-Content -Path $versionJsonPath -Encoding UTF8
        } else {
            $appVersion = $verData.desktop.version
        }
    }
}
Set-Content -Path $versionSrcFile -Value @"
using System.Reflection;
[assembly: AssemblyVersion("$appVersion.0")]
[assembly: AssemblyFileVersion("$appVersion.0")]
"@ -Encoding UTF8
$manifestFile = Join-Path $scriptDir "src\App.manifest"
$iconFile = Join-Path $repoRoot "favicon.ico"
$libDir = Join-Path $scriptDir "lib"
$extDir = Join-Path $scriptDir "extensions\ublock-origin"
$outExe = Join-Path $scriptDir "WonderWatch.exe"
$setupOut = Join-Path $scriptDir "WonderWatch-Setup-win-x64.exe"

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

Write-Host "Compiling WonderWatch.exe (v$appVersion)..."
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
$cscArgs += $versionSrcFile

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Packaging WonderWatch-Setup-win-x64.exe installer..."
if (Test-Path $setupOut) {
    Remove-Item $setupOut -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$stageDir = Join-Path $env:TEMP ("ww_stage_" + [Guid]::NewGuid().ToString("N"))
$appRoot = Join-Path $stageDir "WonderWatch-Desktop"
$payloadZip = Join-Path $stageDir "WonderWatchPayload.zip"
New-Item -ItemType Directory -Force -Path $appRoot | Out-Null

Copy-Item $outExe $appRoot -Force
Copy-Item (Join-Path $scriptDir "Microsoft.Web.WebView2.Core.dll") $appRoot -Force
Copy-Item (Join-Path $scriptDir "Microsoft.Web.WebView2.WinForms.dll") $appRoot -Force
Copy-Item (Join-Path $scriptDir "WebView2Loader.dll") $appRoot -Force
Copy-Item (Join-Path $scriptDir "README.md") $appRoot -Force
Copy-Item (Join-Path $scriptDir "extensions") $appRoot -Recurse -Force

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $appRoot,
    $payloadZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

$setupArgs = @(
    "/nologo",
    "/optimize+",
    "/platform:x64",
    "/target:winexe",
    "/out:$setupOut",
    "/win32manifest:$manifestFile",
    "/resource:$payloadZip,WonderWatchPayload.zip",
    "/r:System.Windows.Forms.dll",
    "/r:System.Drawing.dll",
    "/r:System.IO.Compression.dll",
    "/r:System.IO.Compression.FileSystem.dll"
)
if (Test-Path $iconFile) {
    $setupArgs += "/win32icon:$iconFile"
}
$setupArgs += $installerSrcFile
$setupArgs += $versionSrcFile

& $csc @setupArgs
if ($LASTEXITCODE -ne 0) {
    Remove-Item $stageDir -Recurse -Force
    throw "Installer compilation failed with exit code $LASTEXITCODE"
}

Remove-Item $stageDir -Recurse -Force

$setupSizeMB = [Math]::Round((Get-Item $setupOut).Length / 1MB, 2)
Write-Host "Build complete: $outExe"
Write-Host "Installer ready: $setupOut ($setupSizeMB MB)"
