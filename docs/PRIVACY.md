# Keeping the server private

This repository is meant to be shared. It must never contain the address of our server, a login, or the signing key.

What is kept out of git (see `.gitignore`):

| File | Holds | In the repo |
| --- | --- | --- |
| `OmegaLauncher.LaunchConfig.json` | auto-login email and password, launch options | `templates/OmegaLauncher.LaunchConfig.template.json` (empty login) |
| `OmegaLauncher.ServerList.json` | server names and SiteConfig addresses | `templates/OmegaLauncher.ServerList.template.json` (`[]`) |
| `Mho.Deployment.json` | update host address, update public key, default server entries | `templates/Mho.Deployment.template.json` (empty) |
| `mho-private.pem` | the key that signs updates | never |
| `baseline.json`, `hashes.json`, `host/` | publisher working files | never |

How a release gets our server in it: the release build copies the real `Mho.Deployment.json` (and a filled-in server list) into the release zip from a
folder outside this repository. A launcher built from source without those files works like a plain launcher: no server, no updater.

## Local guard

`tools/install-hooks.sh` installs a `pre-commit` hook that refuses a commit when a staged line contains a saved login, a private key, an IPv4 address that
is not private or loopback, or any text listed in `.git/info/mho-private-patterns` (one regular expression per line, kept only on your machine; put the server
address and the account name there).

If something sensitive ever does get committed, do not just delete it in a later commit: rewrite the history before pushing, and treat the secret as burned
(change the password, rotate the signing key).
