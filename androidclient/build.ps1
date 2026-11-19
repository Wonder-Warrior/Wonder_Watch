$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$versionJsonPath = Join-Path $repoRoot "version.json"
$buildGradlePath = Join-Path $scriptDir "app\build.gradle"

if (Test-Path $versionJsonPath) {
    $verData = Get-Content $versionJsonPath -Raw | ConvertFrom-Json
    if ($verData.android -and $verData.android.version) {
        $parts = $verData.android.version.Split('.')
        if ($parts.Length -eq 3) {
            $newVer = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
            $verData.android.version = $newVer
            $utf8NoBom = New-Object System.Text.UTF8Encoding $false
            [System.IO.File]::WriteAllText($versionJsonPath, ($verData | ConvertTo-Json -Depth 4), $utf8NoBom)
            $vCode = ([int]$parts[0] * 10000) + ([int]$parts[1] * 100) + ([int]$parts[2] + 1)
            if (Test-Path $buildGradlePath) {
                $gradleTxt = [System.IO.File]::ReadAllText($buildGradlePath).TrimStart([char]0xFEFF)
                $gradleTxt = $gradleTxt -replace 'versionCode\s+\d+', "versionCode $vCode"
                $gradleTxt = $gradleTxt -replace 'versionName\s+"[^"]+"', "versionName `"$newVer`""
                [System.IO.File]::WriteAllText($buildGradlePath, $gradleTxt, $utf8NoBom)
            }
            Write-Host "Android version auto-incremented to v$newVer (code $vCode)"
        }
    }
}

$javaHome = "C:\Program Files\Android\Android Studio\jbr"
$sdkDir = Join-Path $env:LOCALAPPDATA "Android\Sdk"

$gradleBat = Get-ChildItem "$env:USERPROFILE\.gradle\wrapper\dists\gradle-9.6.0-bin" -Recurse -Filter "gradle.bat" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
if (-not $gradleBat) {
    $gradleBat = Get-ChildItem "$env:USERPROFILE\.gradle\wrapper\dists" -Recurse -Filter "gradle.bat" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $gradleBat) {
    throw "Gradle binary not found in ~/.gradle/wrapper/dists"
}

$env:JAVA_HOME = $javaHome
$env:ANDROID_HOME = $sdkDir
$env:ANDROID_SDK_ROOT = $sdkDir

# Ensure local.properties matches this machine's Android SDK path
$escapedSdk = $sdkDir -replace '\\', '\\' -replace ':', '\:'
Set-Content -Path (Join-Path $scriptDir "local.properties") -Value "sdk.dir=$escapedSdk" -Encoding ASCII

Push-Location $scriptDir
try {
    & $gradleBat assembleRelease --no-daemon --offline
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Offline Gradle build failed, retrying with network resolution..."
        & $gradleBat assembleRelease --no-daemon
        if ($LASTEXITCODE -ne 0) {
            throw "Gradle build failed with exit code $LASTEXITCODE"
        }
    }
}
finally {
    Pop-Location
}

$apkSrc = Join-Path $scriptDir "app\build\outputs\apk\release\app-release.apk"
if (-not (Test-Path $apkSrc)) {
    throw "Built APK not found at $apkSrc"
}

$apkOut = Join-Path $scriptDir "WonderWatch-Android.apk"
Copy-Item $apkSrc $apkOut -Force

$sizeMB = [Math]::Round((Get-Item $apkOut).Length / 1MB, 2)
Write-Host "Build complete: $apkOut ($sizeMB MB)"
