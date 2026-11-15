# Compound Translations

A [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin for [Message From Aliens](https://store.steampowered.com/app/1858720/Message_From_Aliens/) that labels untranslated compound words with the translations of their parts, and lets you translate runs of glyphs as a unit.

## Installing

Install BepInEx first, if you haven't already, as described under [Requirements](#requirements). Then unzip the release zip into the game folder, the one that holds `MessageFromAliens.exe`. In Steam, right-click the game and choose Manage, then Browse local files, to open it. The zip holds `BepInEx/plugins/CompoundTranslations/`, so the mod lands in BepInEx's plugins folder. To update, unzip a newer release over it. To uninstall, delete `BepInEx/plugins/CompoundTranslations/`.

## Compound words

A word is a compound when its two components are both other words, or when it is one other word repeated two, three, or four times. When a compound has no translation, its English label changes from `?` to `<part 1> - <part 2>`, or `<n>x <part>` for a repeat (such as `3x small`). A word whose two components are the same word side by side or stacked also counts as a repeat. Each part is:

- the part's translation, if it has one;
- otherwise, if the part is itself a compound, its own hint in brackets, e.g. `(<part 1a> - <part 1b>) - <part 2>`;
- otherwise `?+` if the part has translated parts that are not shown, such as parts beyond the `Depth` limit;
- otherwise `?`.

A hint is shown only if at least one translated word appears in it. Otherwise the label stays `?`, or becomes `?+` if the word has translated parts at any depth. The label is drawn in the dimmed color the game uses for finished chapters (`Draw.DefaultColor(3)`).

The hint is display-only. Clicking the label to edit it starts from an empty field as usual, and the hint is never saved as a translation. Translating a word, or changing a setting at runtime, refreshes the visible hints.

## Sequence translations

While editing a glyph's label, shift+click the label of another glyph in the same sentence. Both glyphs and every glyph between them are selected: their own labels are hidden, and one label centered below the run opens for editing. While it is being edited, a bracket in the label color marks the run: a line through the label on each side of the text, spanning the run, with ticks up toward the glyphs at its ends. Shift+click again to extend the selection. Shift+clicking a run's translation adds the whole run to the selection.

Every occurrence of the same glyphs in the same order, anywhere in the game, shows that translation instead of the individual labels. Empty cells between glyphs are part of the run, so a run only matches where the spacing is the same. Clicking any glyph label inside a run edits the run. Clearing a run's translation removes it. Where runs overlap, the leftmost wins, then the longest. A saved run is not shown right after a glyph you translated as `and` or `or` when the glyph before that has a word type in common with the run's first glyph, because the run's last word may belong to both: in "up and down face", a run for "down face" is not used.

A sentence is one line of the game's text, which may wrap onto several rows. A run that wraps has its bracket line on each row, with ticks only at its start and end, and its label goes under the widest row.

Run translations are saved in your save slot alongside the game's own translations, as entries whose names start with `compoundtranslations.sequence:`. The game ignores them. They stay in the save if the mod is removed.

## Example

![Screenshot as described](screenshot.png)

Here you can see that the `L`, `seesaw`, and `| |` glyphs have been "translated" by the player. The mod is assembling those into compound translations where possible.

## Configuration

`BepInEx/config/sparr.messagefromaliens.compoundtranslations.cfg`, created on first launch:

| Key | Default | Effect |
|---|---|---|
| `Display.Separator` | `" - "` | Text between the two parts. |
| `Display.Brackets` | `"()"` | Opening and closing brackets around each compound, as one string split in half, e.g. `"[]"` or `"<<>>"`. `""` for none. |
| `Display.OmitOuterBrackets` | `true` | Leave out the brackets around the whole label, keeping them around nested compounds. |
| `Display.Depth` | `-1` | Layers of compounds to expand. `-1` expands all layers, `0` disables hints, `1` shows only the translations of a word's direct parts. |
| `Display.MarkHidden` | `true` | Show `?+` instead of `?` when the word has translated parts that are not shown. |
| `Sequences.Enabled` | `true` | Show and allow sequence translations. |

Each setting is tagged with its name in glyphs, and the mod names itself in glyphs, so Mod Settings can show them in the game.

BepInEx trims whitespace from setting values, so `Separator` and `Brackets` are written in double quotes, which are removed when read. Quotes are optional when the value has no leading or trailing spaces.

## Requirements

- BepInEx 5.4.23.6 or later, `BepInEx_win_x64` build, unzipped into the game folder. Tested with Unity 6000.6.8f1.
- On Linux under Proton, set the Steam launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- .NET SDK 8 or later to build.

## Building

```sh
dotnet build -c Release
```

The build copies `CompoundTranslations.dll` to `<game>/BepInEx/plugins/CompoundTranslations/`. Disable the copy with `-p:DeployToGame=false`.

The build looks for the game in `<SteamLibraryDir>/steamapps/common/Message From Aliens`. `SteamLibraryDir` comes from the first of these that is set:

1. `-p:SteamLibraryDir=/path/to/SteamLibrary` on the command line.
2. A `SteamLibraryDir` environment variable.
3. `GamePaths.props`, a copy of `GamePaths.props.example`. It is ignored by git.
4. Steam's default install location: `~/.local/share/Steam` on Linux, `C:\Program Files (x86)\Steam` on Windows.

For a game folder outside a Steam library, set `GameDir` to the game folder the same ways.

## Patches

| Method | Patch |
|---|---|
| `AlienCharacter.UpdateEarthText` | Postfix: for a word with no translation (saved value blank or exactly `?`), replaces the label's leading `?` with the hint, keeping any unit or number suffix the game appends, and sets the label color. Otherwise restores the normal label color. Blanks the label of a glyph inside a run. |
| `AlienCharacter.StartEdit` | Prefix: on shift+click, or on a glyph inside a run, skips the game's edit and opens the run's label instead. Postfix: restores the normal label color while editing. |
| `AlienCharacter.EndEdit` | Prefix: skips the save for a glyph whose edit was replaced by a run's. Postfix: refreshes the labels of visible untranslated compounds. |
| `Draw.Write`, `Draw.ClearSelected` | Postfix: marks the glyph layout as changed, so runs are found again on the next frame. |

Each applied hint is logged at the `Debug` level.
