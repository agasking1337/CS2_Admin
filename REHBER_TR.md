# CS2_ADMIN

## Ozellikler

- Admin management
- Admin toptime
- Report system
- Calladmin system
- Admin playtime system
- Auto tag system

## Console Commands

Use commands without `!` in server console.

- `admin addgroup <name> <flags> [immunity]`
- `admin editgroup <name> <flags> [immunity]`
- `admin removegroup <name>`
- `admin listgroups`
- `admin addadmin <steamid> <name> <#group or group1,group2> [immunity] [duration_days]`
- `admin editadmin <steamid> <name|groups|immunity|duration> <value>`
- `admin removeadmin <steamid>`
- `admin listadmins`
- `admin adminreload`
- `ban <target> <minutes|-1> [reason]`
- `ipban <target|ip> <minutes|-1> [reason]`
- `addban <steamid> <minutes|-1> [reason]`
- `unban <steamid|ip> [reason]`
- `warn <target> <minutes|-1> [reason]`
- `unwarn <target> [reason]`
- `mute <target> <minutes|-1> [reason]`
- `unmute <target> [reason]`
- `gag <target> <minutes|-1> [reason]`
- `ungag <target> [reason]`
- `silence <target> <minutes|-1> [reason]`
- `unsilence <target> [reason]`
- `kick <target> [reason]`
- `slap <target> [damage]`
- `slay <target>`
- `respawn <target>`
- `team <target> <t|ct|spec>`
- `noclip <target>`
- `goto <target>`
- `bring <target>`
- `freeze <target> [seconds]`
- `unfreeze <target>`
- `who <target>`
- `map <mapname>`
- `wsmap <workshop_id|name>`
- `rr [seconds]` / `restart [seconds]`
- `hson` / `hsoff`
- `bunnyon` / `bunnyoff`
- `respawnon` / `respawnoff`
- `rcon <command>`
- `cvar <cvar> [value]`
- `admintime`
- `admintimesend`

## Ingame Commands

- `!addgroup <name> <flags> [immunity]`
- `!editgroup <name> <flags> [immunity]`
- `!removegroup <name>`
- `!listgroups`
- `!addadmin <steamid> <name> <#group or group1,group2> [immunity] [duration_days]`
- `!editadmin <steamid> <name|groups|immunity|duration> <value>`
- `!removeadmin <steamid>`
- `!listadmins`
- `!adminreload`
- `!ban <target> <minutes|-1> [reason]`
- `!ipban <target|ip> <minutes|-1> [reason]`
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
- `!map <mapname>`
- `!wsmap <workshop_id|name>`
- `!rr [seconds]` / `!restart [seconds]`
- `!hson` / `!hsoff`
- `!bunnyon` / `!bunnyoff`
- `!respawnon` / `!respawnoff`
- `!rcon <command>`
- `!cvar <cvar> [value]`
- `!admintime`
- `!admintimesend`

## 4) Permissions

Default core permissions:

- `admin.root` -> full access.
- `admin.generic` -> general admin commands.
- `admin.ban` -> ban commands.
- `admin.ban.ip` -> ipban.
- `admin.unban` -> unban.
- `admin.kick` -> kick.
- `admin.mute` -> mute/gag/silence.
- `admin.cvar` -> cvar, noclip, and several server toggles.
- `admin.rcon` -> rcon.

## 5) What Each Permission Does

- `admin.root`:
- Admin/group management (`addadmin/removeadmin/addgroup/...`).
- `adminreload`.
- Root bypass (`admin.*`, `*`).

- `admin.generic`:
- Most moderation and communication commands.
- `warn`, `slap`, `slay`, `respawn`, `team`, `goto`, `bring`, `freeze`, `who`, `asay/say/psay/csay/hsay`, `admintime`.

- `admin.ban`:
- `ban`, `addban`, `lastban`.

- `admin.ban.ip`:
- `ipban`.

- `admin.unban`:
- `unban`.

- `admin.kick`:
- `kick`.

- `admin.mute`:
- `mute/unmute`, `gag/ungag`, `silence/unsilence`.

- `admin.cvar`:
- `noclip`, `hson/hsoff`, `bunnyon/bunnyoff`, `respawnon/respawnoff`, `cvar`.

- `admin.rcon`:
- `rcon`.

## 6) Recommended Setup Flow (Group First, Then Admin)

### 6.1 Create group

Create group first:

```text
admin addgroup Owner admin.root 100
```

Example moderator group:

```text
admin addgroup Moderator admin.generic,admin.kick,admin.mute 50
```

### 6.2 Add admin

After group creation, assign admin:

```text
admin addadmin 76561198255550637 SSonic @Owner
```

or multi-group:

```text
admin addadmin 7656119XXXXXXXXXX PlayerX Moderator,Helper 30 30
```

Notes:
- If two numbers are provided: `[immunity] [duration_days]`
- If one number is provided: `duration_days`

### 6.3 Verify

```text
admin listgroups
admin listadmins
admin adminreload
```

Online target player gets permissions and tag immediately.

Not: Mevcut tag davranisinda yeni admin eklenen oyuncuda tab tag'i bazen kesin gorunum icin
oyuncunun cikis-giris yapmasini gerektirebilir.

## 7) Target Formats

- Name: `PlayerName`
- `#userid`: `#12`
- SteamID64: `7656119...`
- SteamID64 with @: `@7656119...`
- Group targets (for supported commands): `@all`, `@t`, `@ct`, `@alive`, `@dead`

## 8) File Notes

- `config.json` -> general settings.
- `commands.json` -> editable aliases.
- `permissions.json` -> permission mapping.
- `resources/translations/tr.jsonc` and `en.jsonc` -> localization files.
