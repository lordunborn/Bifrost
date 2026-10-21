#!/bin/sh
# Scans every file in the repository for what must never be public: a private key, a saved launcher login, a public IPv4 address (a server address).
# Same rules as the pre-commit guard (tools/pre-commit), but over the whole tree. The release workflow runs it before anything is published.
# Exit code 0 = clean, 1 = something found (listed).

cd "$(git rev-parse --show-toplevel)" || exit 2
status=0

report() {
  echo "privacy check FAILED ($1):"
  printf '%s\n' "$2" | sed 's/^/    /'
  status=1
}

files=$(git ls-files | grep -v -E '\.(png|ico|zip|ttf|dll|exe|bin)$')

hits=$(printf '%s\n' "$files" | xargs grep -I -n -E 'BEGIN [A-Z ]*PRIVATE KEY' 2>/dev/null | head -5)
[ -n "$hits" ] && report "private key" "$hits"

hits=$(printf '%s\n' "$files" | xargs grep -I -n -E '"AutoLogin(Email|Password)[A-Za-z]*"[[:space:]]*:[[:space:]]*"[^"]+"' 2>/dev/null | head -5)
[ -n "$hits" ] && report "saved login in a launch config" "$hits"

ipv4='(^|[^0-9.])((25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])\.){3}(25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])([^0-9.]|$)'
allowed='(^|[^0-9.])(127\.0\.0\.1|0\.[0-9]+\.[0-9]+\.[0-9]+|1\.(4[0-9]|5[0-9])\.[0-9]+\.[0-9]+|1\.23\.0\.23|255\.255\.255\.[0-9]+|10\.[0-9]+\.[0-9]+\.[0-9]+|192\.168\.[0-9]+\.[0-9]+|172\.(1[6-9]|2[0-9]|3[01])\.[0-9]+\.[0-9]+|192\.0\.2\.[0-9]+|198\.51\.100\.[0-9]+|203\.0\.113\.[0-9]+)([^0-9.]|$)'
hits=$(printf '%s\n' "$files" | xargs grep -I -n -E "$ipv4" 2>/dev/null | grep -v -i 'version' | grep -v -E "$allowed" | head -5)
[ -n "$hits" ] && report "public IPv4 address" "$hits"

# The deployment file holds the server address and the token secret: it must not be tracked at all.
tracked=$(git ls-files | grep -E '(^|/)Mho\.Deployment\.json$')
[ -n "$tracked" ] && report "Mho.Deployment.json is tracked" "$tracked"

[ $status -eq 0 ] && echo "privacy check passed: no private key, saved login, public IP address or deployment file in the repository."
exit $status
