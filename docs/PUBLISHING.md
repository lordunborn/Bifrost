# Publishing client updates

Who this is for: whoever edits the Marvel Heroes client (sips, icons, costume files) and has to get those edits to players.

## One-time setup

1. **Signing key.** `OmegaLauncher.Publisher keygen <folder outside the repo>` writes `mho-private.pem` and `mho-public.key`.
   Keep the private key safe and backed up (losing it means every launcher has to be re-released with a new public key; leaking it lets anyone push files to your players).
   The public key goes into `Mho.Deployment.json` (see `templates/`), which ships with releases but is never committed.
2. **Baseline.** Have a pristine copy of the stock client (Steam: the game folder after "Verify integrity of game files"; it must be 1.53.0.203, the FileVersion of
   `UnrealEngine3\Binaries\Win64\MarvelHeroesOmega.exe`). Then
   `OmegaLauncher.Publisher baseline "<pristine client folder>" --version 1.53.0.203 --out baseline.json` (about a minute).
3. **Host folder.** On the web server that already serves `SiteConfig.xml` create a `client` folder under the web root (Apache `htdocs\client`). Nothing else is needed:
   the files are plain static files (no extension on the chunk files is fine). First upload is about 2.2 GB.

## Every time the client is edited

1. Close the game, finish the edits in your working client.
2. `OmegaLauncher.Publisher publish "<working client>" --baseline baseline.json --key mho-private.pem --out host --cache hashes.json --notes "what changed"`
   The managed set is automatic: every file that differs from the baseline, or is not in it, except backups and logs (`*.bak*`, `*.premerge`, `Logs/**`, ...).
   Use `--exclude "pattern"` for anything else that must not be shipped. Nothing changed means nothing is published and the version number stays.
3. `OmegaLauncher.Publisher verify host --pubkey mho-public.key --deep` checks the folder exactly like a launcher would.
4. Upload `host` to the server's `client` folder: chunks first, `manifest.json` / `manifest.sig` / `baseline-sizes.json` last, so a player never sees a manifest whose
   chunks are not there yet. On Windows: `robocopy host\chunks <web root>\client\chunks /E /XO` and then copy the three files.
5. `OmegaLauncher.Publisher verify http://<server>/client --pubkey mho-public.key` from your own PC to confirm the host serves it correctly.

Players are forced to update by default. `--optional` publishes an update the launcher only recommends.

## Testing without a graphics card on the server

The server machine never runs the game; it only stores and serves files, so nothing graphical is needed there. All testing of the client happens on your PC:

1. **Host dry run.** `OmegaLauncher.Publisher serve host --port 8099` serves the folder at `http://localhost:8099/`; `verify http://localhost:8099/ ...` proves the HTTP path works.
2. **Fresh player.** Copy the pristine Steam client to a scratch folder (for example `D:\MHO-PlayerTest\Marvel Heroes`). That is a player who has just installed the game. Run the launcher
   from that folder against the local or real host, let it update, then start the game against the test server. Delete the folder and repeat for each release.
3. **Existing player.** Run the launcher in a copy of your working client: it must report "up to date" without downloading anything.
4. **Steam Deck.** Needs a real Deck (or a Linux machine with Steam and Proton). Until then the Linux build can be tested up to the update step in a Linux VM.

## Releasing a new launcher

There are two parts: the **public release on GitHub** (built automatically) and the **launcher update on your server** (so existing players are offered it).

### 1. The public GitHub release (automatic)

1. Make sure everything is committed and pushed to `master`.
2. Push a version tag. The version must be higher than the last release: `git tag v1.0.1` then `git push origin v1.0.1`.
3. The **Release** workflow (Actions tab) then runs the privacy check and the tests, builds Windows and Linux, and publishes a GitHub release with:
   `OmegaLauncher-1.0.1-win-x64.zip`, `OmegaLauncher-1.0.1-linux-x64.tar.gz`, the raw `programs` (for step 2 below) and `SHA256SUMS.txt`.
4. It stops without publishing if the repository holds a server address, saved login or key, or if an archive contains `Mho.Deployment.json`.
5. To try the build without publishing anything: Actions tab, **Release**, **Run workflow**, enter a version. The files stay as a downloadable workflow artifact for 14 days.

The public release holds the **plain launcher only**. `Mho.Deployment.json` (your server address, signing key and token secret) is never in it: players download the launcher from GitHub and get that one file from you.
To fix a bad release, delete the release and its tag on GitHub, fix the problem, and tag again with a new, higher version number.

### 2. The launcher update on your server (so existing players are offered it)

Players get launcher updates the same way they get game file updates: the launcher checks `client/launcher/latest.json` (signed with the same key) when it starts and offers the new version.
Download the `programs` files from the GitHub release (or build them yourself with the script below) and publish them with the commands in the next section.

### Building the files by hand (and player-ready archives with your deployment file inside)

1. Build the release files: `.\tools\package-release.ps1 -Version 1.0.1 -DeploymentFile <your filled Mho.Deployment.json> -Out <folder outside the repo>`.
   This produces `OmegaLauncher-1.0.1-win-x64.zip` and `OmegaLauncher-1.0.1-linux-x64.tar.gz` (what new players download, with your deployment file inside) and a `programs` folder (what existing launchers download).
2. Publish the programs for self-update, once per platform:
   `OmegaLauncher.Publisher publish-launcher <folder>\programs\OmegaLauncher-win-x64.exe --platform win-x64 --version 1.0.1 --key mho-private.pem --out host --notes "what is new"`
   and the same with `OmegaLauncher-linux-x64` and `--platform linux-x64`. Platforms of the same version go into one release.
3. Upload `host\launcher` to the server's `client\launcher` folder: the program files first, `latest.sig` and `latest.json` last.
4. Hand the two archives to new players. Existing players are offered the update the next time they open the launcher.

A launcher only updates to a version higher than its own, so every release needs a higher number than the last. The signature covers the file hashes, so a changed or substituted program is refused.
`Mho.Deployment.json` is never published (the publisher excludes it); it travels only inside the archives you hand out.

## Refusing out-of-date clients (the server gate)

Optional. With the gate on, the server refuses a login ("patch required") unless the client proves it has the launcher-managed files at or above a version you choose. Players who start the game any other way are refused too, so they are sent back to the launcher.

How it works: the launcher gives the game a small signed token (the installed content version, signed with a shared secret) through the launch option `-twofactorauthname=`. The game sends it to the server in the login request, which has no other use for that field. This is a guard against stale clients, not strong security: the secret ships with the launcher.

1. Make a secret and keep it out of chat and git. For example in PowerShell: `-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 40 | % {[char]$_})`.
2. Put it in the launcher's `Mho.Deployment.json` as `ContentTokenSecret` (the file you hand to players).
3. Put it in the server's `Config.ini` (or `ConfigOverride.ini`), section `[PlayerManager]`:

       RequiredContentVersion=1
       ContentTokenSecret=<the same secret>

   `RequiredContentVersion` is the content version players must have (the number the launcher shows as "version"). Raise it when an update must be mandatory. `0` turns the gate off (the default).
   If the secret is empty the server logs an error and does not enforce the gate, so a typo cannot lock everyone out.
4. Restart the server. In the log, a refused login looks like `IsClientContentOutOfDate(): <email> sent no valid client content token (required version 1, fields: ...)` or `has client content version N, required M`.

`AllowClientVersionMismatch` (the exe version check) is separate and keeps working as before.
