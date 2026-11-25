<#
.SYNOPSIS
    Interactive release helper for SV Time Tracker.
    Updates Sv.TimeTracker.csproj version (if changed), commits, tags v<Version>, and pushes to GitHub to trigger the release workflow.

.EXAMPLE
    .\release.ps1
    .\release.ps1 -Version 1.0.1
    .\release.ps1 -Version 1.0.1 -DryRun
#>
param(
    [string]$Version,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$csprojPath = Join-Path $repoRoot "Sv.TimeTracker\Sv.TimeTracker.csproj"
if (-not (Test-Path $csprojPath)) {
    throw "Could not find $csprojPath"
}

[xml]$csprojXml = Get-Content $csprojPath
$currentVersion = ($csprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $currentVersion) {
    $currentVersion = "1.0.0"
}

Write-Host ""
Write-Host "=== SV Time Tracker Release Helper ===" -ForegroundColor Cyan
Write-Host "Current version in .csproj: $currentVersion" -ForegroundColor Yellow

# Warn if working tree has uncommitted changes
$dirty = git status --porcelain
if ($dirty) {
    Write-Host ""
    Write-Host "Warning: You have uncommitted changes in the working tree:" -ForegroundColor Yellow
    $dirty | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkYellow }
    $proceedDirty = Read-Host "Continue anyway? (y/N)"
    if ($proceedDirty -notin @("y", "Y", "yes", "YES")) {
        Write-Host "Aborted." -ForegroundColor Red
        exit 1
    }
}

# Prompt for version if not supplied as parameter
if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host ""
    $inputVersion = Read-Host "Enter release version (e.g. 1.0.1) or press Enter to release $currentVersion"
    $Version = if ([string]::IsNullOrWhiteSpace($inputVersion)) { $currentVersion } else { $inputVersion.Trim() }
}

$Version = $Version.TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "Invalid version '$Version'. Please use MAJOR.MINOR.PATCH format (e.g. 1.0.1)." -ForegroundColor Red
    exit 1
}

$tagName = "v$Version"
$existingLocalTag = git tag -l $tagName
if ($existingLocalTag) {
    Write-Host "Tag '$tagName' already exists locally. Choose a higher version number." -ForegroundColor Red
    exit 1
}

$branch = (git rev-parse --abbrev-ref HEAD).Trim()

Write-Host ""
Write-Host "Release Plan:" -ForegroundColor Cyan
Write-Host "  Branch:  $branch"
Write-Host "  Version: $Version"
Write-Host "  Tag:     $tagName"
if ($Version -ne $currentVersion) {
    Write-Host "  Action:  Update .csproj -> Commit -> Push $branch -> Tag $tagName -> Push tag"
} else {
    Write-Host "  Action:  Push $branch -> Tag $tagName -> Push tag"
}

if ($DryRun) {
    Write-Host ""
    Write-Host "[DryRun] No files modified and nothing pushed." -ForegroundColor Green
    exit 0
}

Write-Host ""
$confirm = Read-Host "Proceed and publish $tagName to GitHub? (y/N)"
if ($confirm -notin @("y", "Y", "yes", "YES")) {
    Write-Host "Aborted." -ForegroundColor Yellow
    exit 0
}

if ($Version -ne $currentVersion) {
    $content = Get-Content $csprojPath -Raw
    $content = [regex]::Replace($content, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
    $content = [regex]::Replace($content, '<AssemblyVersion>[^<]*</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>")
    $content = [regex]::Replace($content, '<FileVersion>[^<]*</FileVersion>', "<FileVersion>$Version.0</FileVersion>")
    $content = [regex]::Replace($content, '<InformationalVersion>[^<]*</InformationalVersion>', "<InformationalVersion>$Version</InformationalVersion>")
    Set-Content -Path $csprojPath -Value $content -NoNewline

    git add $csprojPath
    git commit -m "Release $tagName"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

git push origin $branch
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

git tag -a $tagName -m "Release $tagName"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

git push origin $tagName
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Successfully pushed $tagName!" -ForegroundColor Green
Write-Host "Track build:  https://github.com/softwarevantage/Sv.TmeTracker/actions/workflows/release.yml" -ForegroundColor Cyan
Write-Host "Release page: https://github.com/softwarevantage/Sv.TmeTracker/releases/tag/$tagName" -ForegroundColor Cyan
