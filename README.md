# CS2_Admin (SwiftlyS2)

Current documentation for the latest command/config model used in this repository.

## Highlights

- Group based admin system (admins are assigned existing groups, no direct flag input on addadmin).
- Scoreboard tag system:
- Non-admin players use `Tags.PlayerTag` from config.
- Admin players use their primary group name as tag automatically.
- `!calladmin` and `!report` commands with separate Discord webhooks.
- Admin playtime integration:
- `!admintime` shows top list (menu for player, console list for server console).
- `!admintimesend` sends top list to Discord webhook.
- `!adminreload` reloads admin/group permissions and refreshes tags for online players.
- Language support from config:
- default is `en`, set `Language` to `tr` for Turkish.
- DB safety:
- if DB/schema is not ready, plugin logs warning and disables DB-backed features instead of spamming errors.

## Installation

1. Build the plugin.
2. Copy output to:

```text
.../game/csgo/addons/swiftlys2/plugins/CS2_Admin/
```

3. Keep `resources/translations` with the DLL.
4. Restart server.

## Config Files

The plugin now uses three JSON files:

- `config.json` -> section `CS2Admin`
- `commands.json` -> section `CS2AdminCommands`
- `permissions.json` -> section `CS2AdminPermissions`

### `config.json` important fields

```json
{
  "CS2Admin": {
    "Language": "en",
    "Discord": {
      "Webhook": "",
      "CallAdminWebhook": "",
      "ReportWebhook": "",
      "AdminTimeWebhook": ""
    },
    "Tags": {
      "Enabled": true,
      "PlayerTag": "PLAYER"
    }
  }
}
```

Notes:

- `AdminTag` is removed. Admin tag comes from group name.
- `Language` accepts `en` or `tr` (fallback is `en`).
- Permanent duration is `-1` in sanction durations.

### `commands.json` editable aliases

Only selected aliases are configurable from JSON (others are fixed defaults to avoid command conflicts):

- `CallAdmin`
- `Report`
- `AdminTime`
- `AdminTimeSend`
- `Ban`
- `IpBan`
- `LastBan`
- `Warn`
- `Unwarn`
- `AddBan`
- `Unban`
- `Kick`
- `Goto`
- `Bring`
- `ChangeMap`
- `ChangeWSMap`
- `Who`

### `permissions.json`

Permissions were moved out of `config.json` into `permissions.json`.
`RootBypassPermissions` is internal and not exposed in JSON.

## Command Model

### Root only admin management

Only `admin.root` can run these:

- `!addadmin <steamid> <name> <#group or group1,group2> [immunity] [duration_days]`
- `!editadmin <steamid> <name|groups|immunity|duration> <value>`
- `!removeadmin <steamid>`
- `!listadmins` (`!admins`)
- `!addgroup <group_name> <flags_csv> [immunity]`
- `!editgroup <group_name> <flags_csv> [immunity]`
- `!removegroup <group_name>`
- `!listgroups`
- `!adminreload`

Addadmin behavior:

- If only one numeric optional argument is provided, it is treated as `duration_days`.
- If two numeric optional arguments are provided, order is `[immunity] [duration_days]`.
- If immunity is omitted, max immunity from selected groups is used automatically.

### Player/admin communication

- `!asay <message>`
- `!say <message>`
- `!psay <target> <message>`
- `!csay <message>`
- `!hsay <message>`
- `!calladmin <message>`
- `!report <message>`

### Sanctions and player actions

- `!ban <target> <minutes|-1> [reason]`
- `!ipban <target|ip> <minutes|-1> [reason]`
- `!lastban`
- `!addban <steamid> <minutes|-1> [reason]`
- `!unban <steamid|ip> [reason]`
- `!warn <target> <minutes|-1> [reason]`
- `!unwarn <target> [reason]`
- `!mute <target> <minutes|-1> [reason]`
- `!unmute <target> [reason]`
- `!gag <target> <minutes|-1> [reason]`
- `!ungag <target> [reason]`
- `!silence <target> <minutes|-1> [reason]`
- `!unsilence <target> [reason]`
- `!kick <target> [reason]`
- `!slap <target> [damage]`
- `!slay <target>`
- `!respawn <target>`
- `!team <target> <t|ct|spec>`
- `!noclip <target>`
- `!goto <target>`
- `!bring <target>`
- `!freeze <target> [seconds]`
- `!unfreeze <target>`
- `!who <target>`

### Server commands

- `!map <mapname>`
- `!wsmap <workshop_id|name>`
- `!rr [seconds]` / `!restart [seconds]`
- `!hson` / `!hsoff`
- `!bunnyon` / `!bunnyoff`
- `!respawnon` / `!respawnoff`
- `!rcon <command>`
- `!cvar <cvar> [value]`

### Admin playtime

- `!admintime`
- `!admintimesend`

### Removed commands

- `!status` removed.
- `!players` removed.
- `!list` removed.

## Target Format

All major action commands support these target styles:

- Name or partial name:
- `!ban PlayerName 30 abuse`
- SteamID with `@` prefix:
- `!ban @7656119XXXXXXXXXX 30 abuse`
- Status/user id with `#` prefix:
- `!ban #12 30 abuse`

Same format applies to kick/slay/slap/freeze and similar target-based commands.

## Build

```bash
dotnet build -c Release
```
