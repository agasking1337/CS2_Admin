# CS2_ADMIN

Eklenti Surumu: `1.0.4`

## Ozellikler

- Admin management
- Admin toptime
- Report system
- Calladmin system
- Admin playtime system
- Auto tag system

## Config Versionlama

Ana config dosyalari `Version: 1` ile surumlenir:

- `config.json`
- `commands.json`
- `permissions.json`
- `maps.json`

Bir dosyada surum eksik/yanlis ise (veya JSON bozuksa), eklenti dosyayi silip bir sonraki yuklemede yeniden olusturur.

## Console Commands

Use `sw_` commands in server console (without `!`).

- `sw_addgroup <name> <flags> [immunity]`
- `sw_editgroup <name> <flags> [immunity]`
- `sw_removegroup <name>`
- `sw_listgroups`
- `sw_addadmin <steamid> <name> <#group or group1,group2> [duration_days]`
- `sw_editadmin <steamid> <name|groups|immunity|duration> <value>`
- `sw_removeadmin <steamid>`
- `sw_listadmins`
- `sw_adminreload`
- `sw_ban <target> <minutes|-1> [reason]`
- `sw_ipban <target|ip> <minutes|-1> [reason]`
- `sw_addban <steamid> <minutes|-1> [reason]`
- `sw_unban <steamid|ip> [reason]`
- `sw_warn <target> <reason>`
- `sw_unwarn <target> [reason]`
- `sw_mute <target> <minutes|-1> [reason]`
- `sw_unmute <target> [reason]`
- `sw_gag <target> <minutes|-1> [reason]`
- `sw_ungag <target> [reason]`
- `sw_silence <target> <minutes|-1> [reason]`
- `sw_unsilence <target> [reason]`
- `sw_kick <target> [reason]`
- `sw_slap <target> [damage]`
- `sw_slay <target>`
- `sw_respawn <target>`
- `sw_team <target> <t|ct|spec>`
- `sw_noclip <target>`
- `sw_goto <target>`
- `sw_bring <target>`
- `sw_freeze <target> [seconds]`
- `sw_unfreeze <target>`
- `sw_resize <target> <scale>`
- `sw_drug <target> [seconds]`
- `sw_burn <target> [seconds] [damage_per_tick]`
- `sw_disarm <target>`
- `sw_speed <target> <multiplier>` / `sw_setspeed <target> <multiplier>`
- `sw_gravity <target> <multiplier>` / `sw_setgravity <target> <multiplier>`
- `sw_rename <target> <new_name>`
- `sw_hp <target> <health>`
- `sw_money <target> <amount>` / `sw_setmoney <target> <amount>` / `sw_givemoney <target> <amount>`
- `sw_give <target> <item>` / `sw_giveitem <target> <item>`
- `sw_who <target>`
- `sw_vote "<soru>" "<cevap1>" "<cevap2>" ...`
- `sw_map <mapname>`
- `sw_wsmap <workshop_id|name>`
- `sw_rr [seconds]` / `sw_restart [seconds]`
- `sw_hson` / `sw_hsoff`
- `sw_bhopon` / `sw_bhopoff`
- `sw_respawnon` / `sw_respawnoff`
- `sw_rcon <command>`
- `sw_cvar <cvar> [value]`
- `sw_admintime`
- `sw_admintimesend`

## Ingame Commands

- `!addgroup <name> <flags> [immunity]`
- `!editgroup <name> <flags> [immunity]`
- `!removegroup <name>`
- `!listgroups`
- `!addadmin <steamid> <name> <#group or group1,group2> [duration_days]`
- `!editadmin <steamid> <name|groups|immunity|duration> <value>`
- `!removeadmin <steamid>`
- `!listadmins`
- `!adminreload`
- `!ban <target> <minutes|-1> [reason]`
- `!ipban <target|ip> <minutes|-1> [reason]`
- `!addban <steamid> <minutes|-1> [reason]`
- `!unban <steamid|ip> [reason]`
- `!warn <target> <reason>`
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
- `!resize <target> <scale>`
- `!drug <target> [seconds]`
- `!burn <target> [seconds] [damage_per_tick]`
- `!disarm <target>`
- `!speed <target> <multiplier>` / `!setspeed <target> <multiplier>`
- `!gravity <target> <multiplier>` / `!setgravity <target> <multiplier>`
- `!rename <target> <new_name>`
- `!hp <target> <health>`
- `!money <target> <amount>` / `!setmoney <target> <amount>` / `!givemoney <target> <amount>`
- `!give <target> <item>` / `!giveitem <target> <item>`
- `!who <target>`
- `!vote "<soru>" "<cevap1>" "<cevap2>" ...`
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

## Ban Davranisi

- `sw_ban` / `!ban` -> yalnizca SteamID ban atar.
- `sw_ipban` / `!ipban` -> yalnizca IP ban atar.
- `sw_addban` / `!addban` -> yalnizca cevrimdisi SteamID ban atar.
- `sw_unban` / `!unban` -> SteamID veya IP ile ban kaldirir.

## 4) Permissions

Default core permissions:

- `admin.root` -> full access.
- `admin.generic` -> general admin commands.
- `admin.ban` -> ban/ipban/addban/unban/lastban.
- `admin.kick` -> kick.
- `admin.mute` -> mute/gag/silence.
- `admin.cheats` -> slap/slay/respawn/team/noclip/goto/bring/freeze/unfreeze/resize/drug/burn/disarm/speed/gravity/rename/hp/money/give.
- `admin.rcon` -> rcon ve sunucu toggle komutlari (`hson/hsoff`, `bhopon/bhopoff`, `respawnon/respawnoff`).
- `admin.cvar` -> `cvar`.
- `Report` permission varsayilan olarak bos (`""`) oldugu icin `!report` herkese aciktir (degistirilmezse).

## 5) What Each Permission Does

- `admin.root`:
- Admin/group management (`addadmin/removeadmin/addgroup/...`).
- `adminreload`.
- Root bypass (`admin.*`, `*`).

- `admin.generic`:
- Most moderation and communication commands.
- `admin` menu erisimi.
- `warn`, `unwarn`, `who`, `asay/say/psay/csay/hsay`, `admintime`, `map`, `wsmap`, `rr/restart`, `calladmin`, `listplayers`, `vote`.

- `admin.ban`:
- `ban`, `ipban`, `addban`, `unban`, `lastban`.

- `admin.kick`:
- `kick`.

- `admin.mute`:
- `mute/unmute`, `gag/ungag`, `silence/unsilence`.

- `admin.cheats`:
- `slap`, `slay`, `respawn`, `team`, `noclip`, `goto`, `bring`, `freeze`, `unfreeze`, `resize`, `drug`, `burn`, `disarm`, `speed`, `gravity`, `rename`, `hp`, `money`, `give`.

- `admin.rcon`:
- `rcon`, `hson/hsoff`, `bhopon/bhopoff`, `respawnon/respawnoff`.

- `admin.cvar`:
- `cvar`.

## 6) Recommended Setup Flow (Group First, Then Admin)

### 6.1 Create group

Create group first:

```text
sw_addgroup Owner admin.root 100
```

Example moderator group:

```text
sw_addgroup Moderator admin.generic,admin.kick,admin.mute 50
```

### 6.2 Add admin

After group creation, assign admin:

```text
sw_addadmin 76561198255550637 SSonic @Owner
```

or multi-group:

```text
sw_addadmin 7656119XXXXXXXXXX PlayerX Moderator,Helper 30
```

Notes:
- Optional last value is `[duration_days]`.
- Example: `sw_addadmin 7656119... PlayerX Moderator 30`

### 6.3 Verify

```text
sw_listgroups
sw_listadmins
sw_adminreload
```

Online target player gets permissions and tag immediately.

Not: Tag yenileme join + gecikmeli yenileme + periyodik yenileme ile guclendirildi. Nadir tab-cache durumunda cikis-giris yine gerekebilir.

## 7) Target Formats

- Name: `PlayerName`
- `#userid`: `#12`
- SteamID64: `7656119...`
- SteamID64 with @: `@7656119...`
- Group targets (for supported commands): `@all`, `@t`, `@ct`, `@alive`, `@dead`

## 8) File Notes

- `config.json` -> general settings.
- `config.json` icinde `Version` ve dil ayarlari bulunur.
- `commands.json` -> editable aliases.
- `commands.json` icinde `Version` bulunur.
- `commands.json` icindeki `Ban` alias'i `sw_ban` (konsol) ve `!ban` (oyun ici) icin kullanilir, sadece SteamID ban atar.
- `commands.json` icindeki `IpBan` alias'i `sw_ipban` (konsol) ve `!ipban` (oyun ici) icin kullanilir, sadece IP ban atar.
- `permissions.json` -> permission mapping.
- `permissions.json` icinde `Version` bulunur.
- `maps.json` icinde `Version` ile normal/workshop map listeleri bulunur.
- `resources/translations/*.jsonc` -> dil dosyalari (`en`, `tr`, `de`, `fr`, `it`, `el`, `ru`, `bg`, `hu`).
