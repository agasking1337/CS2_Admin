# CS2_ADMIN

CS2 admin management plugin source repository.

Current plugin version: `1.0.4`

## Repository Contents

- Full source code (`src/`)
- Localization files (`resources/translations/`)
- Build project (`CS2_Admin.csproj`)
- Config templates (`config.json`, `commands.json`, `permissions.json`, `maps.json`)
- Guides (`GUIDE_EN.md`, `GUIDE_TR.md`)

## Command Model

- Server console commands use `sw_` prefix (`sw_ban`, `sw_addadmin`, ...).
- In-game commands use `!` prefix (`!ban`, `!addadmin`, ...).

## Config Versioning

All main config files include `Version: 1`:

- `config.json`
- `commands.json`
- `permissions.json`
- `maps.json`

If version is missing, mismatched, or file is corrupted, plugin deletes and regenerates that config file automatically at load.

## Ban Behavior

- `sw_ban` / `!ban` -> SteamID ban only.
- `sw_ipban` / `!ipban` -> IP ban only.
- `sw_addban` / `!addban` -> offline SteamID ban only.
- `sw_unban` / `!unban` -> removes related active bans for the given SteamID/IP target.

## Notable Recent Fixes

- Admin tag refresh reliability improved (join + deferred refresh + periodic refresh).
- Gravity command apply path hardened for runtime differences.
- Gag behavior updated: commands allowed, normal chat visible to admins only.

## Documentation

Detailed command lists, permissions, setup flow, and examples are in:

- `GUIDE_EN.md`
- `GUIDE_TR.md`
