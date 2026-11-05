#!/usr/bin/env bash
# One-time Git setup for this Unity project (macOS, Linux, or Git Bash on Windows).
# Sets up Git LFS and Unity Smart Merge (UnityYAMLMerge) for this clone only.
#
# Usage:  bash tools/setup-git.sh [path/to/UnityYAMLMerge]
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

version=$(sed -n 's/^m_EditorVersion: *//p' ProjectSettings/ProjectVersion.txt | tr -d '\r')

merge_tool="${1:-${UNITY_YAML_MERGE:-}}"
if [ -z "$merge_tool" ]; then
  for candidate in \
    "/c/Program Files/Unity/Hub/Editor/$version/Editor/Data/Tools/UnityYAMLMerge.exe" \
    "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/Tools/UnityYAMLMerge" \
    "$HOME/Unity/Hub/Editor/$version/Editor/Data/Tools/UnityYAMLMerge"; do
    if [ -f "$candidate" ]; then merge_tool="$candidate"; break; fi
  done
fi

if [ -z "$merge_tool" ] || [ ! -f "$merge_tool" ]; then
  echo "UnityYAMLMerge not found for Unity $version."
  echo "Install Unity $version with Unity Hub, or pass the tool's path:"
  echo "  bash tools/setup-git.sh \"/path/to/UnityYAMLMerge\""
  exit 1
fi

# Git on Windows wants forward slashes (C:/...) rather than /c/...
if command -v cygpath >/dev/null 2>&1; then merge_tool=$(cygpath -m "$merge_tool"); fi

git lfs install --local
git config --local merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config --local merge.unityyamlmerge.driver "'$merge_tool' merge -h -p --force --fallback none %O %B %A %A"
git config --local merge.unityyamlmerge.recursive binary

echo "Done. Smart Merge uses: $merge_tool"
