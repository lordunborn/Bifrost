#!/bin/sh
# Checks the files tools/package-release.ps1 produced, before anything is published.
# Usage: sh tools/check-release-files.sh <release folder>
# Fails if: an archive is missing, an archive contains Mho.Deployment.json (server address + token secret), or the Linux program / start script lost its executable bit.
set -e

dir="$1"
[ -d "$dir" ] || { echo "usage: check-release-files.sh <release folder>"; exit 2; }
cd "$dir"

zip=$(ls OmegaLauncher-*-win-x64.zip)
tarball=$(ls OmegaLauncher-*-linux-x64.tar.gz)
ls programs/OmegaLauncher-win-x64.exe programs/OmegaLauncher-linux-x64 > /dev/null

unzip -l "$zip" > zip-listing.txt
tar -tzf "$tarball" > tar-listing.txt
tar -tvzf "$tarball" > tar-listing-long.txt

# grep reads each whole listing (no early exit), so nothing can slip past a closed pipe.
if grep -c "Mho.Deployment.json" zip-listing.txt tar-listing.txt | grep -v ":0$"; then
  echo "FAILED: an archive contains Mho.Deployment.json"
  exit 1
fi

executables=$(grep -E '^-rwxr-xr-x.*(OmegaLauncher/OmegaLauncher|OmegaLauncher/start-omega-launcher\.sh)$' tar-listing-long.txt | wc -l)
if [ "$executables" -ne 2 ]; then
  echo "FAILED: the Linux program and start script must both be executable (found $executables)"
  cat tar-listing-long.txt
  exit 1
fi

grep -q "OmegaLauncher.exe" zip-listing.txt || { echo "FAILED: the zip has no OmegaLauncher.exe"; exit 1; }

rm -f zip-listing.txt tar-listing.txt tar-listing-long.txt
echo "release files OK: both archives present, no deployment file inside, Linux program executable."
