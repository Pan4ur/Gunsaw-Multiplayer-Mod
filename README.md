# Gunsaw Multiplayer Mod

> **WIP, MVP, IN-DEV — not production-ready.**  This is an unofficial multiplayer mod for
> [Gunsaw](https://orsonik.itch.io/gunsaw-demo). It is actively being developed and
> can contain desyncs, crashes, incomplete mechanics, and compatibility issues

![icon](img/icon.png)

## Current development features

- Public lobby list, lobby creation, joining, and in-game chat
- Multiplayer settings for PvP, grabbing players, and respawning
- Replication of players, weapons, many world props, NPCs, and selected map hazards
- Host-to-client custom level transfer

## TODO

- [ ] Test
- [ ] Add more mp settings and features
- [ ] Fix tails
- [X] Far objects sync
- [ ] Switch to UDP or Steam lobbies(?)
- [ ] Fix compatibility with Crossover on apple silicon
- [ ] Make sure there are definitely no desyncs and crashes
- [ ] Refactor god classes
- [ ] Fix or disable the process of transferring into another body

## Installation

1. Download [Gunsaw](https://orsonik.itch.io/gunsaw-demo/purchase)
2. Extract the game to C:\Games\Gunsaw (or another folder)
3. Start the unmodified game once, then close it
4. Install [BepInEx](https://github.com/bepinex/bepinex/releases) into the game folder — the folder that contains the
   `Gunsaw.exe`
5. Download `GunsawMultiplayer.dll` from releases
6. Copy the `GunsawMultiplayer.dll` to ```<Gunsaw folder>\BepInEx\plugins\GunsawMultiplayer.dll```
7. Start Gunsaw, open the **Multiplayer** menu at bottom-left corner, and create or join a lobby
8. Smash your friends in every way possible

![guide](img/guide.png)

The mod currently uses a default lobby server. Idk how long it will stay online, but you can host your own lobby server by setting its IP address and following the instructions below

## Custom levels

1. Create and export a level in Gunsaw's level editor
2. Host a multiplayer lobby
3. In the multiplayer window, choose **Paste custom level**. The exported level code must be
   in the clipboard
4. Confirm that the status says the level is loaded, then choose **Start custom level**

## Crashes

You'll most likely encounter crashes. If you see this window, please copy the error message and open an issue describing what you were doing before the crash occurred

![crash](img/crash.png)

## Building the mod

You need the .NET SDK and a local Gunsaw installation whose required managed assemblies are
available in `GunsawMultiplayer/lib/`. For the standard local installation, the source DLLs are located in `Gunsaw\BepInEx\core\` and `Gunsaw\Gunsaw_Data\Managed\`. Copy `BepInEx.dll` and `0Harmony.dll` from the `BepInEx\core` directory, and `Assembly-CSharp.dll` together with the required `UnityEngine*.dll` files from the `Gunsaw_Data\Managed` directory into `GunsawMultiplayer\lib\`. These game DLLs are not included in the repository and must be obtained from your own Gunsaw installation.

```powershell
.\build-mod.ps1
```

## Running your own lobby server

The relay/lobby service lives in [LobbyServer](https://github.com/Pan4ur/Gunsaw-Lobby-Server). It provides the HTTP
lobby API and the WebSocket endpoint used by the mod. A public deployment needs HTTPS and
WSS, normally through the included Caddy or Nginx reverse-proxy examples

## Contributing

Pull requests are very welcome

## Credits

- [Orsoniks](https://github.com/Orsoniks) for **Gunsaw**
- [BepInEx team](https://github.com/BepInEx) for [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX)
- [OpenAI](https://github.com/OPENAI) for **GPT 5.6 Sol**

## Disclaimer

This is a community-made, unofficial modification. It is not affiliated with, endorsed by,
or supported by Orsoniks or the developers of Gunsaw. This repository does not claim ownership
of Gunsaw, its characters, assets, code, or any other original-game rights. You must obtain
Gunsaw from its official source before using this mod
