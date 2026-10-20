# Builds the Android APK without opening the Unity editor, and optionally
# installs it on a phone plugged in over USB (USB debugging on, unlocked).
#
#   .\build-android.ps1 -Version 0.1 -Code 1
#   .\build-android.ps1 -Version 0.1 -Code 2 -Install
#   .\build-android.ps1 -Version 0.1 -Code 3 -Install -Launch
#
# Needs Unity 6000.5.8f1 with Android Build Support (and its OpenJDK and
# Android SDK & NDK). Close the project in the editor first: Unity can't
# open it twice. Signing: see Assets/Graveway/Editor/BuildScript.cs.

param(
    [string]$Version = "0.1",
    [int]$Code = 1,
    [switch]$Install,
    [switch]$Launch,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$project = $PSScriptRoot
$apk = Join-Path $project "Builds\graveway-$Version.apk"
$log = Join-Path $project "Logs\build-android.log"
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null

if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity. Pass -Unity <path to Unity.exe>." }
$androidPlayer = Join-Path (Split-Path $Unity) "Data\PlaybackEngines\AndroidPlayer"
if (-not (Test-Path $androidPlayer)) { throw "This Unity has no Android Build Support. Add it in Unity Hub: Installs > (gear) > Add modules." }

Write-Host "Building Graveway $Version (code $Code)..."
& $Unity -batchmode -quit -projectPath $project -buildTarget Android `
    -executeMethod Graveway.EditorTools.BuildScript.Android `
    -buildVersion $Version -buildCode $Code -out $apk -logFile $log | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $apk)) {
    Select-String -Path $log -Pattern "error|Exception|Build (Failed|Succeeded)" | Select-Object -Last 25 | ForEach-Object { $_.Line }
    throw "Build failed (exit $LASTEXITCODE). Full log: $log"
}
Select-String -Path $log -Pattern "Graveway: build" | Select-Object -Last 1 | ForEach-Object { $_.Line }

if ($Install -or $Launch) {
    $adb = Join-Path $androidPlayer "SDK\platform-tools\adb.exe"
    if (-not (Test-Path $adb)) { $adb = "adb" }
    & $adb install -r $apk
    if ($LASTEXITCODE -ne 0) { throw "adb install failed. Is the phone plugged in, unlocked, with USB debugging allowed? Try: & '$adb' devices" }
    if ($Launch) { & $adb shell monkey -p com.thejumpvault.gravewayunity -c android.intent.category.LAUNCHER 1 | Out-Null }
}
Write-Host "APK: $apk"
