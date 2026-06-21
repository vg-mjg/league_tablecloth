# League Tablecloth

A Mahjong Soul (Unity client) mod that replaces the tablecloth with dynamically generated one composed from 4 triangles, one per seat, matched to players' nicknames.

It functions like [tablecloth-generator-web](https://github.com/anon-446/tablecloth-generator-web) and the [tablecloth switcher](https://files.catbox.moe/mzupbi.js) script combined, except it's built for the desktop client. Our version makes no web request and **expects all tablecloth triangles to exist locally**. There's no auto-update yet.

## Installation

We depend on [mjslib](https://github.com/vg-mjg/mjslib), so follow its installation instructions first.

Next, download `league_tablecloth.zip` [from releases](https://github.com/vg-mjg/league_tablecloth/releases) and unpack it into `<GameDir>/BepInEx/plugins`. Keep the directory so that you end up with:

```text
BepInEx/plugins/
  Mjslib.dll
  league_tablecloth/
    league_tablecloth.dll
    assets/
      players.json
      *.png
```

Run the game and open a log or spectate a match. Even with no nicknames matches, it should display the default green tablecloth. Check the logs at `<GameDir>/BepInEx/` if you encounter any issues.

## Customizing

The release zip should contain tablecloth triangles and player name mapping for the [current event].
If you are working on a tablecloth or simply want to change it, edit the `assets/players.json`.
It maps exact in-game nicknames to file names (don't trust whatever some retard put in the form as their nickname).
Modified PNG triangles should be loaded again without restarting the game. You just need to quit and re-enter the log. The JSON map is cached though.

## Build

Use the dotnet toolchain (>=net6.0) to build it.

```sh
dotnet build Plugin/Plugin.csproj -c Release -p:BepInExRoot=/path/to/BepInEx -p:MjslibDll=/path/to/Mjslib.dll
```

By default it will look for `Mjslib.dll` in the `<BepInExRoot>/plugins` directory, so if you followed the installation instructions, you only need to set `BepInExRoot`.

For a local `BepInExRoot` default, copy `Directory.Build.props.example` to `Directory.Build.props` and edit the `BepInExRoot` path inside.

To produce the release zip run: `scripts/dist.sh`. Any extra arguments are forwarded to `dotnet build`.
