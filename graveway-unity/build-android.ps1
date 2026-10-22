# Builds the Android APK without opening the Unity editor, and optionally
# installs it on a phone plugged in over USB (USB debugging on, unlocked), or
# publishes it as a GitHub Release that installed copies offer as an update.
#
#   .\build-android.ps1 -Version 0.1 -Code 1
#   .\build-android.ps1 -Version 0.1 -Code 2 -Install
#   .\build-android.ps1 -Version 0.1 -Code 3 -Install -Launch
#   .\build-android.ps1 -Version 0.2 -Code 2 -Release [-Notes "What changed"]
#
# -Release publishes a pre-release on My-Games tagged
# graveway-unity-v<Version>-<Code>, with the APK attached (Updates.cs looks for
# exactly that). It needs the GitHub CLI signed in, everything committed and
# pushed, and a Code higher than the last release's, or phones won't see it.
#
# Needs Unity 6000.5.8f1 with Android Build Support (and its OpenJDK and
# Android SDK & NDK). Close the project in the editor first: Unity can't
# open it twice. Signing: see Assets/Graveway/Editor/BuildScript.cs.

param(
    [string]$Version = "0.1",
    [int]$Code = 1,
    [switch]$Install,
    [switch]$Launch,
    [switch]$Release,
    [string]$Notes = "",
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$project = $PSScriptRoot
$repo = "enachealex/My-Games"
$apk = Join-Path $project "Builds\graveway-$Version.apk"
$log = Join-Path $project "Logs\build-android.log"
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null

if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity. Pass -Unity <path to Unity.exe>." }
$androidPlayer = Join-Path (Split-Path $Unity) "Data\PlaybackEngines\AndroidPlayer"
if (-not (Test-Path $androidPlayer)) { throw "This Unity has no Android Build Support. Add it in Unity Hub: Installs > (gear) > Add modules." }

# A release is built from a commit that's on GitHub, so its tag points at the
# code that's in it.
if ($Release) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "-Release needs the GitHub CLI (gh), signed in." }
    $dirty = git -C $project status --porcelain -- .
    if ($dirty) { throw "-Release needs everything committed first:`n$($dirty -join "`n")" }
    $commit = (git -C $project rev-parse HEAD).Trim()
    if (-not (git -C $project branch -r --contains $commit)) { throw "-Release needs commit $commit pushed to GitHub first." }
    $tag = "graveway-unity-v$Version-$Code"
    $codes = gh release list --repo $repo --limit 100 --json tagName --jq '.[].tagName' |
        ForEach-Object { if ($_ -match '^graveway-unity-v.+-(\d+)$') { [int]$Matches[1] } }
    $last = ($codes | Measure-Object -Maximum).Maximum
    if ($last -and $Code -le $last) { throw "Release code $Code isn't higher than the last one ($last): phones wouldn't offer it. Use -Code $($last + 1)." }
}

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

if ($Release) {
    $asset = Join-Path $project "Builds\graveway-unity-$Version.apk"
    Copy-Item $apk $asset -Force
    if (-not $Notes) { $Notes = "Graveway for Unity $Version (build $Code), built from $commit. Installs over the build before it; progress stays." }
    gh release create $tag $asset --repo $repo --target $commit --prerelease --title "Graveway for Unity $Version" --notes $Notes
    if ($LASTEXITCODE -ne 0) { throw "Couldn't publish the release (gh release create failed)." }
    Write-Host "Released: https://github.com/$repo/releases/tag/$tag"
}
Write-Host "APK: $apk"
