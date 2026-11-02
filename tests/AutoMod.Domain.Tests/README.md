# AutoMod.Domain.Tests

A console runner: `dotnet run --project tests/AutoMod.Domain.Tests` prints every check and ends with `N 通过，M 失败`
(N passed, M failed; the exit code is non-zero on any failure). `dotnet run ... -- balance` runs the proficiency balance simulation instead.

- **Architecture tests** scan `src/` for the dependency direction between layers (the domain does not reference the game or the plugin;
  released code applies no patches and uses no files, network, processes or reflection).
- **Localization tests** check the eight `ModFiles/Localization/*.csv` tables: keys, placeholders, widths of UI strings, no Chinese
  characters outside `zh`. One check also walks the game's own item tables; it needs an export of them and is skipped unless the
  environment variable `AUTOMOD_GAMETABLES` points at it (a directory with `<language>/Item.tsv` and friends).
- **Chinese snapshot**: `snapshot_zh.json` is the stored output of every Chinese UI string, compared word for word, so the multilingual
  rework could not change the Chinese text. If you change a Chinese string on purpose, delete the file and run the tests once to
  regenerate it. `zh_ui_08.txt` and `zh_ui_08_map.tsv` are the original 0.8 Chinese strings and their key map, used by the localization tests.
