$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
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
