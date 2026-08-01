#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ ! -f Config.Build.user.props ]]; then
  cp Config.Build.user.props.example Config.Build.user.props
  echo "Edit Config.Build.user.props so PeakGameRootDir points to your PEAK game folder." >&2
  exit 1
fi
dotnet restore DanceBox.sln
dotnet build DanceBox.sln -c Release --no-restore
printf 'Build complete. Find com.dline.dancebox.dll under artifacts/.\n'
