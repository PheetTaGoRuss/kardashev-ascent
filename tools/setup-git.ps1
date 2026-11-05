# One-time Git setup for this Unity project (Windows PowerShell).
# Sets up Git LFS and Unity Smart Merge (UnityYAMLMerge) for this clone only.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\setup-git.ps1 [-MergeTool "C:\path\to\UnityYAMLMerge.exe"]
param([string]$MergeTool = $env:UNITY_YAML_MERGE)
$ErrorActionPreference = 'Stop'

Set-Location (git rev-parse --show-toplevel)

$version = ((Get-Content ProjectSettings/ProjectVersion.txt | Select-String '^m_EditorVersion:').Line -replace '^m_EditorVersion:\s*', '').Trim()

if (-not $MergeTool) {
    $MergeTool = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Data\Tools\UnityYAMLMerge.exe"
}

if (-not (Test-Path $MergeTool)) {
    Write-Host "UnityYAMLMerge not found for Unity $version."
    Write-Host "Install Unity $version with Unity Hub, or pass the tool's path:"
    Write-Host '  powershell -ExecutionPolicy Bypass -File tools\setup-git.ps1 -MergeTool "C:\path\to\UnityYAMLMerge.exe"'
    exit 1
}

$tool = $MergeTool -replace '\\', '/'

git lfs install --local
git config --local merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config --local merge.unityyamlmerge.driver "'$tool' merge -h -p --force --fallback none %O %B %A %A"
git config --local merge.unityyamlmerge.recursive binary

Write-Host "Done. Smart Merge uses: $tool"
