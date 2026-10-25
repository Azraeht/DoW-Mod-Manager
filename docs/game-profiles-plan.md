# Plan — two version streams, one code base: pluggable game profiles

## Version streams (facts as of 2026-10-25)

| Stream | What | Source | Base for the port? |
|---|---|---|---|
| **2.x** | DoW Mod Manager for the classic games (Original, Winter Assault, Dark Crusade, Soulstorm). Upstream `IgorTheLight/DoW-Mod-Manager` `master`, **v2.7.0.1** | MIT, `master` | **Yes**, this branch is rebased on it |
| **1.x** | "DoW DE Mod Manager" for Dawn of War: Definitive Edition, **v1.1.2.1** | MIT, **public**: same repo, branch `DE` (`upstream/DE`), authored by IgorTheLight | No (see the policy below) |

The two numbering schemes are independent: `1.1.2.1` (DE) is not older than `2.7.0.1`.
Upstream tags are also mislabeled. For example, tag `1.1.2.1`/`DE_1.1.2.1` points at master's
"Version 2.7.0.0" commit. Use branch heads, not tags, when tracking upstream.

**Policy (as requested): no code is taken from the 1.x DE manager.** DE support is built only from
publicly documented game behaviour: the Steam store/appmanifest, the game's own files, and Relic's
documented user-data layout. The 1.x source is MIT and in the same repo, so this is a choice, not a
legal requirement. It is listed as an open decision at the end.

## Where the code is today

DE support already exists, but it is spread over `GameVariant` switches:
`GameInstall.Is64Bit`, `.BaseGameModuleName`, `.SupportsNoFog`, `.IsOldGame`, `.ModsDir`,
`GameVariantDetector.{Detect, ExecutableFor, UserDataFor, IsDefinitiveEdition}`, the appid table in
`LinuxGameInstallLocator`, `ModlistService` (DE uses the Soulstorm list section) and the LAA guard.
Each new difference between the games means another `if (Variant == DefinitiveEdition)`.

## Target design: `IGameProfile`

One object per game *family* answers every "what does this game look like" question. Code that
handles mods, launching, LAA or DXVK asks the profile instead of switching on the variant.

```csharp
// Models/GameProfiles/IGameProfile.cs
public interface IGameProfile
{
    GameProfileId Id { get; }               // Classic, DefinitiveEdition
    string DisplayName { get; }

    // --- detection --------------------------------------------------------
    IReadOnlyList<int> SteamAppIds { get; } // Classic: 4570, 4580, 9450 ; DE: 3556750
    int DetectionPriority { get; }          // DE first: it also ships a W40k.exe
    GameVariant? Detect(string dir, int? steamAppId);   // null = not this family

    // --- layout -----------------------------------------------------------
    string ExecutableFor(GameVariant v);    // Soulstorm.exe / DarkCrusade.exe / W40kWA.exe / W40k.exe
    string BaseModuleName(GameVariant v);   // "W40k" | "DoWDE"
    IEnumerable<string> ModuleSearchDirs(GameInstall g);   // install dir (+ <UserData>/Mods for DE)
    string UserDataDir(string installDir, string? appDataDir); // Local.ini, warnings.log, Profiles/
    string ModlistSection(GameVariant v);   // section of "DoW Mod Manager Download Mods.list"

    // --- capabilities -----------------------------------------------------
    bool LaaApplies { get; }                // Classic: true (32-bit) ; DE: false (64-bit)
    bool GraphicsWrappersApply { get; }     // DXVK / dgVoodoo2 (Direct3D 9 games): both profiles
    bool NoFogApplies(GameVariant v);       // Soulstorm only ; DE: false until verified
    bool ModulesHavePlayableFlag(GameVariant v); // false for Original/WA
    IReadOnlyList<LaunchFlag> SupportedLaunchFlags { get; } // -dev, -nomovies, -forcehighpoly, -noprecachemodels
}
```

- `GameInstall` gets a `Profile` property. Its existing helpers (`Is64Bit`, `BaseGameModuleName`, …)
  become one-line delegations, so callers and the 237 tests keep compiling during the migration.
- `GameProfileRegistry` (DI singleton) lists the profiles in `DetectionPriority` order.
  `GameVariantDetector.Detect` becomes `registry.Detect(dir, appid)`.
- **Capabilities are data, not platform code.** `LaaService` asks `profile.LaaApplies` before the PE
  check (the PE 64-bit check stays as a safety net). The DXVK / dgVoodoo2 buttons ask
  `profile.GraphicsWrappersApply`. The launcher builds arguments from `SupportedLaunchFlags`.
- **Adding a game = adding one class.** For example, `DefinitiveEditionProfile.cs` + tests, without
  touching the launcher, locator or view models.

### Profile values

| | `ClassicProfile` (2.x base) | `DefinitiveEditionProfile` |
|---|---|---|
| Variants | Original, WinterAssault, DarkCrusade, Soulstorm | DefinitiveEdition |
| Detect | exe priority Soulstorm > DC > WA > W40k (upstream `GetCurrentGameEXE`) | `W40k.exe` or `W40k_gog.exe`, **and** (`DoWDE.module` **or** appid 3556750 in manifest/`steam_appid.txt`) |
| Exe | per variant | `W40k.exe` (Steam), `W40k_gog.exe` (GOG), 64-bit |
| Base module | `W40k` | `DoWDE` |
| `.module` dirs | install dir | install dir + `<UserData>/Mods` (+ subfolders) + optional Soulstorm folder (`SoulstormDir`) |
| User data | install dir | `%APPDATA%\Relic Entertainment\Dawn of War` → on Linux `compatdata/3556750/pfx/drive_c/users/steamuser/AppData/Roaming/…` |
| LAA | yes (Steam vs GOG variant of the patch, as upstream) | no |
| DXVK / dgVoodoo2 | yes, Windows and Linux | yes, Windows and Linux (upstream's DE manager offers both) |
| No-fog | Soulstorm only, Windows only | no |
| Mod list section | per variant | Soulstorm section (current behaviour, to confirm) |

## Linux: launching and finding games (no registry)

Launching: already implemented. The order is Steam `-applaunch` (native/snap/Flatpak), then the
`steam://` URL, then direct Proton (`ProtonToolResolver`), then Wine with `WINEPREFIX`. The
launcher only needs the profile for the exe name and appid.

Discovery: `IGameInstallLocator` becomes a composite of **sources**. Each source yields
`(installDir, prefix, appid?, launcher)`, and the profile registry decides the variant:

1. **Steam** (exists): `libraryfolders.vdf` → `appmanifest_<id>.acf` → `steamapps/common/…`,
   prefix `compatdata/<appid>`. Native, Flatpak (`~/.var/app/com.valvesoftware.Steam`) and snap roots.
2. **Heroic** (new, GOG builds): `~/.config/heroic/gog_store/installed.json` (`install_path`) and
   `~/.config/heroic/GamesConfig/<appName>.json` (`winePrefix`, `wineVersion`). Flatpak variant under
   `~/.var/app/com.heroicgameslauncher.hgl/config/heroic`.
3. **Lutris** (new): game YAMLs (`~/.config/lutris/games/*.yml` on older versions,
   `~/.local/share/lutris/games/*.yml` on newer ones) give `game.exe` and `game.prefix`/`wine.prefix`.
   `pga.db` (SQLite) can be read as a fallback; avoid adding an SQLite dependency unless needed.
4. **Bottles** (optional): `~/.local/share/bottles/bottles/<name>/bottle.yml` + `drive_c` scan.
5. **Manual path** (exists): the user picks a folder, and the prefix is inferred by walking up to `drive_c`.

The paths above are from the launchers' documented or observed layouts and **must be checked against
current releases** before coding, with fixtures in tests (as for `SteamPaths`). The Wine/Heroic/Lutris
route should run the launcher's own Wine build. Plain `wine` from PATH is only the fallback.

## DXVK, dgVoodoo2, no-fog on Linux

- DXVK and dgVoodoo2: done on both OSes (`Services/GraphicsModService.cs`). On Linux, Wine DLL
  overrides come from the launcher. Remaining work is QA on a real Steam + Proton install.
- No-fog: write process memory from *inside* the prefix with a tiny helper `.exe` run through
  `RunInGamePrefixAsync`. Low priority.

## Milestones

1. **P1 – refactor, no behaviour change**: introduce `IGameProfile`, `ClassicProfile`,
   `DefinitiveEditionProfile` and the registry. Delegate the `GameInstall` helpers and port the existing tests.
2. **P2 – capabilities through profiles**: LAA, DXVK, no-fog and launch flags read from the profile.
   UI hides inapplicable toggles from the profile.
3. **P3 – discovery sources**: split the Steam locator into a source. Add Heroic and Lutris with fixture tests.
4. **P4 – DE polish**: verify the DE mod list section, DE launch flags (`-fullscreen` from the DE
   manager's Experimental section) and `Local.ini` keys on a real install. Done already: GOG exe,
   Soulstorm folder, `DXP2` as a base module.
5. **Ongoing – upstream sync**: rebase on `upstream/master` per upstream release, bump `VersionPrefix`,
   reset the suffix to `linux.1`, and port new upstream behaviour (diff `DoW Mod Manager/` between the old and new base).

## Open decisions

1. **The 1.x DE manager source** (`upstream/DE`, MIT): used as a behavioural reference only (GOG exe
   name, Soulstorm folder, base modules), reimplemented in the port, no code copied.
2. **Heroic/Lutris/Bottles**: which launchers are in scope for P3?

Settled: DXVK and dgVoodoo2 are ported on both OSes for every profile. The manager stays one
cross-platform application, with per-OS features listed in `docs/ARCHITECTURE.md`.
