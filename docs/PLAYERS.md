# Omega Launcher: player guide

Omega Launcher starts Marvel Heroes Omega, keeps your game files in step with the server, and works on Windows and on Linux / Steam Deck.

## What you need

- The stock Marvel Heroes client, version 1.53.0.203, installed from Steam. (The launcher checks the version and tells you if it is wrong.)
- About 4 GB of free disk space for the update and the backups of the original files it replaces.
- Windows 10 or 11, or Linux with Steam (Steam Deck included).

## Getting the launcher

Download it from the **Releases** page of this project on GitHub (the newest release; the **Windows** `.zip` or the **Linux / Steam Deck** `.tar.gz`).
You also need one small file from the server owner, **`Mho.Deployment.json`**. It holds the server address. Save it in the same folder as the launcher after you extract it.
(Without that file the launcher still starts, but it has no server to connect to and cannot update your game files.)

## Windows

1. Extract the zip anywhere (a folder on your desktop is fine). Do not put it inside the game folder. Put `Mho.Deployment.json` next to `OmegaLauncher.exe`.
2. Run `OmegaLauncher.exe`. Windows or your antivirus may warn about an unsigned program. That is expected for a small community program; the warning is about the file being unknown, not about anything it does.
3. The launcher looks for the game in your Steam libraries. If it cannot find it, it asks you to pick the game folder (the one that contains `UnrealEngine3` and `Data`).
4. If the status says **Update required**, press the green button (it says **Update**). The launcher shows how much it will download, makes a backup of every original file it replaces, and updates only what is different.
5. When the status says the client files are up to date, press **Play**.

The window also has **Updates** (check again, verify every file, restore the original files, change the game folder) and **Options** (resolution, auto-login, themes, news).

## Steam Deck

Do the setup once in Desktop Mode, then play from Gaming Mode.

1. Install the Marvel Heroes client from Steam (it is the Windows version; the Deck runs it through Proton). Start it once from Steam if you like, then close it.
2. Install a Proton build that works well with the game. The easiest way is **ProtonUp-Qt** (Discover store) and then install the newest **GE-Proton**. The launcher can also use Valve's Proton, umu-launcher or Wine, and shows which one it will use under **Runs with**.
3. Download the Linux archive, extract it to a permanent place (for example `~/Games/OmegaLauncher`), and run `start-omega-launcher.sh` once to check that the window opens. The window is the same as on Windows (1280 x 800).
4. Add it to Steam: in Steam, **Add a Game → Add a Non-Steam Game**, browse to `start-omega-launcher.sh` (set the file filter to all files). Open its **Properties** and put `--autoplay` in the launch options.
5. In Gaming Mode, start it from your library. With `--autoplay` it checks for updates, asks once before downloading, installs, and starts the game. D-pad moves between buttons and A presses.

On a Steam Deck the game starts at 1280 x 800 automatically. You can turn that off in **Options → Launcher**.

## If something goes wrong

| What you see | What to do |
| --- | --- |
| "The update server could not be reached" or "did not answer in time" | Check your internet connection. You can still press Play; the server decides whether your game is up to date enough to log in. |
| "Wrong game version" | The Steam install is not 1.53.0.203. Let Steam update or verify the game files, then press **Updates → Check for updates**. |
| "Some stock game files look wrong" | In Steam, right-click the game → Properties → Installed Files → **Verify integrity of game files**, then check again. |
| The game says it needs a patch when you log in | Your client files are older than the server needs. Press **Update** in the launcher, then Play from the launcher (starting the game any other way skips the check the server makes). |
| Not enough disk space | Free some space and try again. Nothing is left half-installed. |
| You want the original game back | **Updates → Restore original files** puts back every file the launcher replaced and removes the ones it added. |
| (Linux) The launcher closes at once and the message mentions `libICE`, `libSM` or another `.so` file | A desktop Linux or SteamOS already has these. On a minimal install add them, for example on Ubuntu or Debian: `sudo apt install libice6 libsm6 libx11-6 libxcursor1 libxrandr2 libxi6 libfontconfig1 libfreetype6 libxkbcommon-x11-0 libegl1`. |
| (Linux) "Nothing found" under Runs with | Install GE-Proton with ProtonUp-Qt, press **Refresh** in **Options → Launcher**, or use **Browse** to pick a `proton`, `umu-run` or `wine` file. |

## Asking for help

Run `OmegaLauncher --print-launch` (Linux: `./OmegaLauncher --print-launch`) in a terminal and send the output to the server owner. It shows what the launcher found and the exact command it would run, and it does not start the game or include any password.
