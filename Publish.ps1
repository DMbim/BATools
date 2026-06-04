# Publish.ps1 — BATools release automation
# Run from solution root after building Release R26 in VS

param(
    [string]$GitHubToken = $env:BA_GITHUB_TOKEN
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Config ────────────────────────────────────────────────────────────────────
$RepoOwner      = "DMbim"
$RepoName       = "BATools"
$BuildOutput    = "BA\bin\x64\Release R26"
$PropsFile      = "Directory.Build.props"
$AssetName      = "BA_R26.zip"

# ── Resolve paths ─────────────────────────────────────────────────────────────
$ScriptRoot     = $PSScriptRoot
$PropsPath      = Join-Path $ScriptRoot $PropsFile
$BuildPath      = Join-Path $ScriptRoot $BuildOutput
$ZipPath        = Join-Path $ScriptRoot $AssetName

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
    "1" { $patch++; }
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
Write-Host "Release version: $newVersion" -ForegroundColor Green
Write-Host "Build output:    $BuildPath"
Write-Host "Asset:           $ZipPath"
Write-Host "Repo:            $RepoOwner/$RepoName"
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
    Authorization  = "Bearer $GitHubToken"
    Accept         = "application/vnd.github+json"
    "User-Agent"   = "BATools-Publisher"
    "X-GitHub-Api-Version" = "2022-11-28"
}



# ── Verify build output exists ────────────────────────────────────────────────
if ($choice -ne "4") {
    Write-Host "Bumping version to $newVersion in $PropsFile..." -ForegroundColor Cyan
    $content = Get-Content $PropsPath -Raw
    $content = $content -replace "<VersionPrefix>$currentVersion</VersionPrefix>", "<VersionPrefix>$newVersion</VersionPrefix>"
    Set-Content $PropsPath $content -NoNewline
    Write-Host "Done." -ForegroundColor Green
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Yellow
    Write-Host " Version bumped to $newVersion in Directory.Build.props" -ForegroundColor Yellow
    Write-Host " You must now rebuild the solution in Visual Studio:" -ForegroundColor Yellow
    Write-Host "   Build -> Rebuild Solution (Release R26 / Any CPU)" -ForegroundColor Yellow
    Write-Host " Then come back here and press ENTER to continue." -ForegroundColor Yellow
    Write-Host "============================================================" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Press ENTER when rebuild is complete"
}

# ── Verify build output exists ────────────────────────────────────────────────
if (-not (Test-Path $BuildPath)) {
    Write-Error "Build output not found at: $BuildPath`nBuild the solution in VS first (Release R26)."
    exit 1
}

$dllPath = Join-Path $BuildPath "BA.dll"
if (-not (Test-Path $dllPath)) {
    Write-Error "BA.dll not found in build output. Build may have failed."
    exit 1
}

# ── Update BATools.version file ───────────────────────────────────────────────
$versionFilePath = Join-Path $BuildPath "BATools.version"
Set-Content $versionFilePath $newVersion -NoNewline
Write-Host "BATools.version updated to $newVersion" -ForegroundColor Cyan

# ── Zip build output ──────────────────────────────────────────────────────────
Write-Host "Zipping build output..." -ForegroundColor Cyan

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path "$BuildPath\*" -DestinationPath $ZipPath -CompressionLevel Optimal
Write-Host "Zip created: $ZipPath" -ForegroundColor Green

# ── Create GitHub release ─────────────────────────────────────────────────────
Write-Host "Creating GitHub release v$newVersion..." -ForegroundColor Cyan

$releaseBody = @{
    tag_name         = "v$newVersion"
    target_commitish = "master"
    name             = "v$newVersion"
    body             = "BATools v$newVersion"
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

# ── Upload asset ──────────────────────────────────────────────────────────────
Write-Host "Uploading $AssetName..." -ForegroundColor Cyan

$uploadUrl = $release.upload_url -replace "\{.*\}", ""
$uploadUrl = "$uploadUrl`?name=$AssetName"

$assetBytes = [System.IO.File]::ReadAllBytes($ZipPath)

try {
    $uploadHeaders = $headers.Clone()
    $uploadHeaders["Content-Type"] = "application/octet-stream"

    $uploaded = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $uploadHeaders `
        -Body $assetBytes
    Write-Host "Asset uploaded: $($uploaded.browser_download_url)" -ForegroundColor Green
}
catch {
    Write-Error "Failed to upload asset: $_"
    Write-Host "Release was created but asset upload failed. Delete the release on GitHub and try again." -ForegroundColor Yellow
    exit 1
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
Write-Host "URL: $($release.html_url)" -ForegroundColor Cyan
Write-Host ""