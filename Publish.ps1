# Publish.ps1 — BATools release automation (addin + installer, fully automated)
#
# Run from the solution root. Builds BA.dll for every ENABLED Revit-year
# target below, publishes BATools-Installer.exe as a self-contained
# single-file exe, verifies the installer actually is self-contained (guards
# against the framework-dependent regression that shipped in the past), then
# uploads everything to the same GitHub release and copies the installer to
# the S: share.
#
# Multi-year support: each entry in $RevitTargets is a Revit version this
# script can build BA.dll against. A build configuration existing and
# compiling is NOT the same as that build having been run inside a real
# install of that Revit version and confirmed working. Only flip a target's
# Enabled flag to $true after you've actually done that.

param(
    [string]$GitHubToken = $env:BA_GITHUB_TOKEN
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Config ────────────────────────────────────────────────────────────────────
$RepoOwner = "DMbim"
$RepoName  = "BATools"
$PropsFile = "Directory.Build.props"

# One entry per Revit year BA Tools can target. BuildConfig must match a real
# configuration in BA.csproj's <Configurations> list. RelativeOutput is where
# that configuration's build output lands, relative to this script.
$RevitTargets = @(
    [PSCustomObject]@{
        Year           = 2026
        BuildConfig    = "Release R26"
        RelativeOutput = "BA\bin\x64\Release R26"
        AssetName      = "BA_R26.zip"
        Enabled        = $true
    },
    [PSCustomObject]@{
        Year           = 2025
        BuildConfig    = "Release R25"
        RelativeOutput = "BA\bin\x64\Release R25"
        AssetName      = "BA_R25.zip"
        # Confirmed: built clean and exercised in a real Revit 2025 session.
        Enabled        = $true
    }
)

# ASSUMPTION — please confirm. Inferred from the RelativeOutput paths above
# (they only make sense if the project itself lives at BA\BA.csproj under
# this script's folder). Fix this line if the actual project filename differs.
$AddinProjectRelPath = "BA\BA.csproj"

# Confirmed this session: BATools-Installer.csproj is a sibling of BA\ at the
# solution root, not nested inside it. Version-agnostic, built once regardless
# of how many Revit-year targets are enabled above.
$InstallerProjectRelPath = "BATools-Installer\BATools-Installer.csproj"
$InstallerExeName        = "BATools-Installer.exe"

# Framework-dependent output is ~150KB. A genuine self-contained single-file
# WPF exe is 60-150MB+. 30MB gives enormous margin on both sides.
$InstallerMinSizeBytes = 30MB

# ASSUMPTION — please confirm/adjust to wherever you actually want this.
$InstallerShareDir = "S:\CAD\Autodesk Revit\_admin\BA_tools\Installer"

# ── Resolve paths ─────────────────────────────────────────────────────────────
$ScriptRoot           = $PSScriptRoot
$PropsPath            = Join-Path $ScriptRoot $PropsFile
$AddinProjectPath     = Join-Path $ScriptRoot $AddinProjectRelPath
$InstallerProjectPath = Join-Path $ScriptRoot $InstallerProjectRelPath
$InstallerProjectDir  = Split-Path $InstallerProjectPath -Parent

$EnabledTargets = @($RevitTargets | Where-Object { $_.Enabled })

# ── Functions ─────────────────────────────────────────────────────────────────

function Assert-SelfContainedPublish {
    param(
        [Parameter(Mandatory)] [string]$PublishDir,
        [Parameter(Mandatory)] [string]$ExeName,
        [long]$MinSizeBytes = 30MB
    )

    $exePath = Join-Path $PublishDir $ExeName
    if (-not (Test-Path $exePath)) {
        throw "GUARD FAILED: expected exe not found at $exePath."
    }

    $exeCount = @(Get-ChildItem -Path $PublishDir -Filter "*.exe" -File).Count
    if ($exeCount -ne 1) {
        throw "GUARD FAILED: expected exactly 1 .exe in $PublishDir, found $exeCount."
    }

    $size = (Get-Item $exePath).Length
    if ($size -lt $MinSizeBytes) {
        $sizeMb = [math]::Round($size / 1MB, 1)
        $minMb  = [math]::Round($MinSizeBytes / 1MB, 1)
        throw "GUARD FAILED: $ExeName is only $sizeMb MB (expected at least $minMb MB). This looks framework-dependent, not self-contained. Aborting before any deployment."
    }

    Write-Host "Guard passed: $ExeName is $([math]::Round($size / 1MB, 1)) MB." -ForegroundColor Green
    return $exePath
}

function Publish-GitHubReleaseAsset {
    param(
        [Parameter(Mandatory)] [string]$UploadUrlTemplate,
        [Parameter(Mandatory)] [hashtable]$Headers,
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string]$AssetName
    )

    $uploadUrl = ($UploadUrlTemplate -replace "\{.*\}", "") + "?name=$AssetName"
    $bytes = [System.IO.File]::ReadAllBytes($FilePath)

    $uploadHeaders = $Headers.Clone()
    $uploadHeaders["Content-Type"] = "application/octet-stream"

    Write-Host "Uploading $AssetName ($([math]::Round($bytes.Length / 1MB, 1)) MB)..." -ForegroundColor Cyan
    $uploaded = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $uploadHeaders -Body $bytes
    Write-Host "Uploaded: $($uploaded.browser_download_url)" -ForegroundColor Green
}

# ── Sanity: at least one target enabled ────────────────────────────────────────
if ($EnabledTargets.Count -eq 0) {
    Write-Error "No Revit targets are Enabled in `$RevitTargets. Nothing to build."
    exit 1
}

# ── Read current version from Directory.Build.props ───────────────────────────
[xml]$props = Get-Content $PropsPath
$currentVersion = $props.Project.PropertyGroup.VersionPrefix
if ([string]::IsNullOrWhiteSpace($currentVersion)) {
    Write-Error "Could not read VersionPrefix from $PropsPath"
    exit 1
}

$parts = $currentVersion.Split('.')
$major = [int]$parts[0]
$minor = [int]$parts[1]
$patch = [int]$parts[2]

Write-Host ""
Write-Host "Current version: $currentVersion" -ForegroundColor Cyan
Write-Host ""
Write-Host "Select version bump:"
Write-Host "  [1] Patch  -> $major.$minor.$($patch + 1)"
Write-Host "  [2] Minor  -> $major.$($minor + 1).0"
Write-Host "  [3] Major  -> $($major + 1).0.0"
Write-Host "  [4] No bump (use current version)"
Write-Host ""

$choice = Read-Host "Enter choice (1/2/3/4)"

switch ($choice) {
    "1" { $patch++ }
    "2" { $minor++; $patch = 0 }
    "3" { $major++; $minor = 0; $patch = 0 }
    "4" { Write-Host "Using current version $currentVersion" -ForegroundColor Yellow }
    default {
        Write-Error "Invalid choice. Aborting."
        exit 1
    }
}

$newVersion = "$major.$minor.$patch"

# ── Confirm ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Release version:    $newVersion" -ForegroundColor Green
Write-Host "Revit targets to build and ship this run:" -ForegroundColor Green
foreach ($t in $EnabledTargets) {
    Write-Host "  - Revit $($t.Year): $($t.BuildConfig) -> $($t.AssetName)"
}
$skipped = @($RevitTargets | Where-Object { -not $_.Enabled })
if ($skipped.Count -gt 0) {
    Write-Host "Skipped (Enabled = `$false):" -ForegroundColor DarkYellow
    foreach ($t in $skipped) {
        Write-Host "  - Revit $($t.Year): $($t.BuildConfig)"
    }
}
Write-Host ""
Write-Host "Installer project:  $InstallerProjectPath"
Write-Host "Share destination:  $InstallerShareDir"
Write-Host "Repo:               $RepoOwner/$RepoName"
Write-Host ""

$confirm = Read-Host "Proceed? (y/n)"
if ($confirm -ne "y") {
    Write-Host "Aborted." -ForegroundColor Yellow
    exit 0
}

# ── GitHub token check ────────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
    Write-Host ""
    $GitHubToken = Read-Host "Enter GitHub personal access token (needs repo + write:packages scope)"
}
if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
    Write-Error "No GitHub token provided. Aborting."
    exit 1
}

$headers = @{
    Authorization          = "Bearer $GitHubToken"
    Accept                 = "application/vnd.github+json"
    "User-Agent"           = "BATools-Publisher"
    "X-GitHub-Api-Version" = "2022-11-28"
}

# ── Bump version in Directory.Build.props ──────────────────────────────────────
if ($choice -ne "4") {
    Write-Host "Bumping version to $newVersion in $PropsFile..." -ForegroundColor Cyan
    $content = Get-Content $PropsPath -Raw
    $content = $content -replace "<VersionPrefix>$currentVersion</VersionPrefix>", "<VersionPrefix>$newVersion</VersionPrefix>"
    Set-Content $PropsPath $content -NoNewline
    Write-Host "Done." -ForegroundColor Green
}

# ── Build the addin for every enabled Revit-year target ────────────────────────
if (-not (Test-Path $AddinProjectPath)) {
    Write-Error "Addin project not found at $AddinProjectPath. Fix `$AddinProjectRelPath at the top of this script."
    exit 1
}

$zipPaths = @{}

foreach ($target in $EnabledTargets) {
    Write-Host ""
    Write-Host "── Building addin for Revit $($target.Year) ($($target.BuildConfig)) ──" -ForegroundColor Cyan

    $buildPath = Join-Path $ScriptRoot $target.RelativeOutput

    dotnet clean $AddinProjectPath -c $target.BuildConfig -p:Platform=x64 | Out-Null
    dotnet build $AddinProjectPath -c $target.BuildConfig -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for Revit $($target.Year) (exit code $LASTEXITCODE). Aborting, nothing was deployed."
        exit 1
    }
    Write-Host "Revit $($target.Year) build succeeded." -ForegroundColor Green

    if (-not (Test-Path $buildPath)) {
        Write-Error "Build output not found at: $buildPath (build reported success but output is missing, path mismatch?)."
        exit 1
    }
    $dllPath = Join-Path $buildPath "BA.dll"
    if (-not (Test-Path $dllPath)) {
        Write-Error "BA.dll not found in build output for Revit $($target.Year)."
        exit 1
    }

    $versionFilePath = Join-Path $buildPath "BATools.version"
    Set-Content $versionFilePath $newVersion -NoNewline

    $zipPath = Join-Path $ScriptRoot $target.AssetName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$buildPath\*" -DestinationPath $zipPath -CompressionLevel Optimal
    $zipPaths[$target.AssetName] = $zipPath

    Write-Host "Zip created: $zipPath" -ForegroundColor Green
}

# ── Publish the installer (self-contained, single-file, built once) ────────────
Write-Host ""
Write-Host "Publishing installer..." -ForegroundColor Cyan
if (-not (Test-Path $InstallerProjectPath)) {
    Write-Error "Installer project not found at $InstallerProjectPath. Fix `$InstallerProjectRelPath at the top of this script."
    exit 1
}

dotnet clean $InstallerProjectPath -c Release | Out-Null
dotnet publish $InstallerProjectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "Installer publish failed (exit code $LASTEXITCODE). Aborting, nothing was deployed."
    exit 1
}

# Deterministic SDK-style publish path for this exact command shape, confirmed
# empirically against this project: <projectDir>\bin\Release\net8.0-windows\win-x64\publish\
$InstallerPublishDir = Join-Path $InstallerProjectDir "bin\Release\net8.0-windows\win-x64\publish"

# ── Guard: refuse to deploy anything that isn't really self-contained ──────────
$verifiedInstallerExe = Assert-SelfContainedPublish `
    -PublishDir $InstallerPublishDir `
    -ExeName $InstallerExeName `
    -MinSizeBytes $InstallerMinSizeBytes

# ── Create GitHub release ─────────────────────────────────────────────────────
Write-Host ""
Write-Host "Creating GitHub release v$newVersion..." -ForegroundColor Cyan

$releaseBody = @{
    tag_name         = "v$newVersion"
    target_commitish = "master"
    name             = "v$newVersion"
    body             = "BATools v$newVersion ($($EnabledTargets.Year -join ', '))"
    draft            = $false
    prerelease       = $false
} | ConvertTo-Json

$releaseUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases"

try {
    $release = Invoke-RestMethod -Uri $releaseUrl -Method Post -Headers $headers `
        -Body $releaseBody -ContentType "application/json"
    Write-Host "Release created: $($release.html_url)" -ForegroundColor Green
}
catch {
    Write-Error "Failed to create GitHub release: $_"
    exit 1
}

# ── Upload every addin zip plus the installer to the release ───────────────────
try {
    foreach ($target in $EnabledTargets) {
        Publish-GitHubReleaseAsset -UploadUrlTemplate $release.upload_url -Headers $headers `
            -FilePath $zipPaths[$target.AssetName] -AssetName $target.AssetName
    }

    Publish-GitHubReleaseAsset -UploadUrlTemplate $release.upload_url -Headers $headers `
        -FilePath $verifiedInstallerExe -AssetName $InstallerExeName
}
catch {
    Write-Error "Asset upload failed: $_"
    Write-Host "Release was created but an asset upload failed. Delete the release on GitHub and try again." -ForegroundColor Yellow
    exit 1
}

# ── Copy installer to the S: share ──────────────────────────────────────────────
Write-Host ""
Write-Host "Copying installer to share..." -ForegroundColor Cyan
try {
    if (-not (Test-Path $InstallerShareDir)) {
        New-Item -ItemType Directory -Path $InstallerShareDir -Force | Out-Null
    }
    $shareDest = Join-Path $InstallerShareDir $InstallerExeName
    Copy-Item -Path $verifiedInstallerExe -Destination $shareDest -Force
    Write-Host "Installer copied to: $shareDest" -ForegroundColor Green
}
catch {
    Write-Host "WARN: Failed to copy installer to share: $_" -ForegroundColor Yellow
    Write-Host "GitHub release still succeeded, copy it to the share manually: $verifiedInstallerExe" -ForegroundColor Yellow
}

# ── Commit version bump ───────────────────────────────────────────────────────
if ($choice -ne "4") {
    Write-Host "Committing version bump..." -ForegroundColor Cyan
    Push-Location $ScriptRoot
    try {
        git add $PropsFile
        git commit -m "chore: bump version to v$newVersion"
        git push origin master
        Write-Host "Version bump committed and pushed." -ForegroundColor Green
    }
    catch {
        Write-Host "WARN: Git commit failed: $_" -ForegroundColor Yellow
        Write-Host "Version was bumped locally but not committed. Commit manually." -ForegroundColor Yellow
    }
    finally {
        Pop-Location
    }
}

# ── Done ──────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Release v$newVersion published successfully." -ForegroundColor Green
Write-Host "Revit targets shipped: $($EnabledTargets.Year -join ', ')" -ForegroundColor Cyan
Write-Host "GitHub: $($release.html_url)" -ForegroundColor Cyan
Write-Host "Share:  $InstallerShareDir\$InstallerExeName" -ForegroundColor Cyan
Write-Host ""
