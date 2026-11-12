# Game profile format

Each `profiles/<id>.json` describes one game. BoostRoot loads all of them at start; an invalid profile blocks loading (tests catch it).

| field | meaning |
|---|---|
| `id`, `name` | `^[a-z0-9-]{2,40}$`, display name |
| `antiCheat` | none · vanguard · eac · battleye · vac · ricochet · javelin · other |
| `competitive` | true → competitive level never enables upscalers or Radeon Boost |
| `group` | **A** = edits the game's settings file · **B** = driver/Windows only + tips card |
| `detect` | launcher ids (`steam`: app id, `epic`: AppName, `riot`, `minecraft`, `roblox`, `name`) + `process` (comma list) |
| `configs` | `{id, format, path, pick}` — format: ini · keyvalue (+`separator`) · keyvalues (Valve) · json · xml. Path supports `%ENV%`, `*`, `{install}`, `{steam}`, `{gameDir}`, `{documents}` |
| `neverTouch` | extra protected keys `configId:glob` (resolution, keybinds, sensitivity, crosshair, language, volume are always protected) |
| `levels` | `quality`, `balanced`, `maxfps`, `competitive` → `set: {"configId:key": "value"}` + optional `gpu` prefs |
| `recommend` | `[{ifScoreBelow, level}, …, {level}]` by PC score |
| `notes` | per key: TR/EN text + `fpsImpact`/`visualImpact` (shown in "What will change?") |
| `tips` | TR/EN in-game advice |
| `stretchKeys` | keys the opt-in 4:3 stretched mode may write |

Key paths: ini `Section/Key`, keyvalues `block/sub/key`, xml `Root/Child@attr`, json `a/b/c`.
