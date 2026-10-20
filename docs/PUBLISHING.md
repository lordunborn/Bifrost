# Publishing client updates

Who this is for: whoever edits the Marvel Heroes client (sips, icons, costume files) and has to get those edits to players.

## One-time setup

1. **Signing key.** `Bifrost.Publisher keygen <folder outside the repo>` writes `mho-private.pem` and `mho-public.key`.
   Keep the private key safe and backed up (losing it means every launcher has to be re-released with a new public key; leaking it lets anyone push files to your players).
   The public key goes into `Mho.Deployment.json` (see `templates/`), which ships with releases but is never committed.
2. **Baseline.** Have a pristine copy of the stock client (Steam: the game folder after "Verify integrity of game files"; it must be 1.53.0.203, the FileVersion of
   `UnrealEngine3\Binaries\Win64\MarvelHeroesOmega.exe`). Then
   `Bifrost.Publisher baseline "<pristine client folder>" --version 1.53.0.203 --out baseline.json` (about a minute).
3. **Host folder.** On the web server that already serves `SiteConfig.xml` create a `client` folder under the web root (Apache `htdocs\client`). Nothing else is needed:
   the files are plain static files (no extension on the chunk files is fine). First upload is about 2.2 GB.

## Every time the client is edited

1. Close the game, finish the edits in your working client.
2. `Bifrost.Publisher publish "<working client>" --baseline baseline.json --key mho-private.pem --out host --cache hashes.json --notes "what changed"`
   The managed set is automatic: every file that differs from the baseline, or is not in it, except backups and logs (`*.bak*`, `*.premerge`, `Logs/**`, ...).
   Use `--exclude "pattern"` for anything else that must not be shipped. Nothing changed means nothing is published and the version number stays.
3. `Bifrost.Publisher verify host --pubkey mho-public.key --deep` checks the folder exactly like a launcher would.
4. Upload `host` to the server's `client` folder: chunks first, `manifest.json` / `manifest.sig` / `baseline-sizes.json` last, so a player never sees a manifest whose
   chunks are not there yet. On Windows: `robocopy host\chunks <web root>\client\chunks /E /XO` and then copy the three files.
5. `Bifrost.Publisher verify http://<server>/client --pubkey mho-public.key` from your own PC to confirm the host serves it correctly.

Players are forced to update by default. `--optional` publishes an update the launcher only recommends.

## Testing without a graphics card on the server

The server machine never runs the game; it only stores and serves files, so nothing graphical is needed there. All testing of the client happens on your PC:

1. **Host dry run.** `Bifrost.Publisher serve host --port 8099` serves the folder at `http://localhost:8099/`; `verify http://localhost:8099/ ...` proves the HTTP path works.
2. **Fresh player.** Copy the pristine Steam client to a scratch folder (for example `D:\MHO-PlayerTest\Marvel Heroes`). That is a player who has just installed the game. Run the launcher
   from that folder against the local or real host, let it update, then start the game against the test server. Delete the folder and repeat for each release.
3. **Existing player.** Run the launcher in a copy of your working client: it must report "up to date" without downloading anything.
4. **Steam Deck.** Needs a real Deck (or a Linux machine with Steam and Proton). Until then the Linux build can be tested up to the update step in a Linux VM.
