# Probably Automated

A mod for [Probably Stolen](https://store.steampowered.com/app/4348910/) that automates the shop's chores with hired apprentices.
*Artificial intelligence? No - Apprentice Intelligence.* 能工智人自动化，交给牛马的工作可能是自动化的。

Workshop page: <https://steamcommunity.com/sharedfiles/filedetails/?id=3831595688>

You buy "Apprentice Dispatch" in Wild's network, hire apprentices through the intercom and write routines for them
(filters, run conditions, move / swap / use a tool on an item / activate / toggle / press a button). Apprentices work the morning
or the night shift and a daily report tells you what they did. A strategy simulator runs a routine against a copy of your shop first.
Eight languages, follows the game's language.

- Uses the game's own mod system (`IMod`, Unity Mono). No BepInEx.
- The released mod declares no capabilities (`<Capabilities />`) and applies no Harmony patches.
- Everything is stored in the save file. Disabling the mod leaves your items untouched.
- It relies on public classes and fields of the game, which the game's authors do not promise to keep stable;
  a game update can break it.

## Build

Requirements: the .NET 8 SDK and a Steam install of Probably Stolen (the mod compiles against the game's own assemblies, which are not part of this repository).

```bash
# the game must be closed; installs into <game>/Mods/ProbablyAutomated
dotnet build src/AutoMod -c Release -p:GameDir="/path/to/Probably Stolen"
```

`GameDir` defaults to the usual Steam location on Windows (`C:/Program Files (x86)/Steam/steamapps/common/Probably Stolen`);
it can also be set as an environment variable. To publish, copy the content of `<game>/Mods/ProbablyAutomated` as it is.
Add `-p:InstallMod=false` to compile without copying anything into the game folder.
`src/AutoMod` needs the game's assemblies, so a bare `dotnet build` of the solution fails without a game install; the domain layer and its tests build on their own.

## Test

The domain layer (routine engine, apprentices, reports, simulation, localization core) does not reference the game and has its own tests:

```bash
dotnet run --project tests/AutoMod.Domain.Tests
```

It is a plain console runner (not xUnit/NUnit), so use `dotnet run`, not `dotnet test`. See [tests/AutoMod.Domain.Tests/README.md](tests/AutoMod.Domain.Tests/README.md).

## Layout

Naming: the mod and its Workshop item are called *Probably Automated* (assembly `ProbablyAutomated`, id `probably_automated`);
the projects and C# namespaces still say `AutoMod`, the mod's former name.

| Path | Content |
|---|---|
| `src/AutoMod.Domain/` | Game-independent domain: routine engine, apprentices, wages, reports, simulation, localization core, save data (netstandard2.1) |
| `src/AutoMod/` | The mod itself: game adapter, shift scheduling, localization bridge, UI. `ModFiles/` holds the manifest, textures and the eight translations |
| `tests/AutoMod.Domain.Tests/` | Unit tests of the domain layer, plus architecture tests for the dependency direction between layers |
| `art/` | Generator of the hand-drawn intercom icon |

## Reporting problems

Please include the version of the mod and of the game, the list of other enabled mods, a description of what you did,
and `Player.log` from `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\`
(right after the problem, before restarting the game; the previous run is kept as `Player-prev.log`).

## License

[MIT](LICENSE)

---

## 简介

Probably Stolen 的 mod：雇学徒、写流程，让他们替你上早班、晚班，次日看日报。
纯领域部分（流程引擎、学徒、报告、模拟、多语言核心）不依赖游戏，单独有单元测试；mod 本体用游戏自带的 mod 系统加载，发布版不需要任何能力、不打 Harmony 补丁。

编译：装好 .NET 8 SDK 和 Steam 版游戏，关闭游戏后运行 `dotnet build src/AutoMod -c Release -p:GameDir="游戏目录"`，产物会装进 `<游戏>/Mods/ProbablyAutomated`。
反馈问题时请附上 mod 和游戏的版本、启用的其它 mod、操作过程和 `Player.log`（位置见上）。
