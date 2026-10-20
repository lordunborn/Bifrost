#!/bin/sh
# Installs the repository's git hooks (run once after cloning).
set -e
root="$(git rev-parse --show-toplevel)"
cp "$root/tools/pre-commit" "$(git rev-parse --git-dir)/hooks/pre-commit"
chmod +x "$(git rev-parse --git-dir)/hooks/pre-commit"
mkdir -p "$(git rev-parse --git-dir)/info"
echo "Installed pre-commit hook."
echo "Put your private patterns (server address, account names), one regular expression per line, in: $(git rev-parse --git-dir)/info/mho-private-patterns"
