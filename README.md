<div align="center">

# [SwiftlyS2] CS2_Admin

<br/>
</div>

## Overview

**CS2_Admin** is the ultimate, all-in-one administration plugin for Counter-Strike 2 servers running on SwiftlyS2. While standard admin plugins offer basic ban and mute functionality, **CS2_Admin** goes significantly further by integrating **Live Discord Server Statuses**, **Interactive Fun Commands**, **Comprehensive Reporting**, and a fully automated **GitHub Auto-Updater**.

This plugin provides a highly robust, database-driven foundation for managing admins, permissions, player sanctions, and server features. Designed to be feature-rich yet highly customizable, it completely overhauls the standard admin experience.

## Download Shortcuts
<ul>
  <li>
    <code>📦</code>
    <strong>&nbsp;Download Latest Plugin Version</strong> ⇢
    <a href="https://github.com/candaysa/CS2_Admin/releases/latest" target="_blank" rel="noopener noreferrer">Click Here</a>
  </li>
  <li>
    <code>⚙️</code>
    <strong>&nbsp;Download Latest SwiftlyS2 Version</strong> ⇢
    <a href="https://github.com/swiftly-solution/swiftlys2/releases/latest" target="_blank" rel="noopener noreferrer">Click Here</a>
  </li>
</ul>

## Table of Contents

- [Features Overview](#features-overview)
- [Requirements](#requirements)
- [Installation](#installation)
- [Initial Setup](#initial-setup)
- [Configuration](#configuration)
- [Command Blocker](#command-blocker)
- [Commands & Permissions](#commands--permissions)

---

## Features Overview

Unlike basic admin plugins, CS2_Admin includes a massive suite of features out-of-the-box. Every single feature listed below is built-in and requires **zero** additional plugins.

### 🛡️ Advanced Permission & Immunity System
CS2_Admin uses Swiftly's native permission system (`admin.root`, `admin.ban`, `admin.cheats`, etc.) combined with a fully configurable immunity level system. You can create unlimited admin groups, each with their own permission set and immunity level. Higher immunity admins are protected from actions by lower immunity admins, ensuring your hierarchy is always respected.

### 🏷️ [Admin Tag Manager](#admin-tag-manager)
A complete chat customization system. Assign colored tags, colored names, and colored chat messages to each admin group individually. Supports 20+ built-in color codes including `[red]`, `[blue]`, `[gold]`, `[lime]`, `[purple]`, `[magenta]` and more. Tags are displayed both in the **chat** and on the **scoreboard**, so everyone on the server can instantly see who is an admin and what rank they hold.

### ⏱️ [AFK Manager Module](#afk-manager-module)
A fully automated AFK detection system that monitors player activity. When a player goes idle beyond the configured threshold, the system warns them with an audible alert before automatically moving them to spectator or kicking them, depending on server population. Admins can optionally be excluded from AFK checks.

### 📊 [Admin Toptime](#admin-toptime)
Track and monitor the playtime of every administrator on the server. The system automatically records minutes of active play time at configurable intervals. Admins can check their own playtime with `!admintime`, and root admins can broadcast a full leaderboard of the most active admins to the server or to a dedicated Discord channel.

### 🚨 [CallAdmin & Player Reports](#calladmin--player-reports)
Two separate but complementary systems for player-to-admin communication:
- **CallAdmin** (`!calladmin`): A general distress call that notifies admins via Discord when no admin is online.
- **Reports** (`!report <target>`): A targeted report system where a player selects another player and provides a reason. The report is sent as a rich Discord embed including the reporter's details, the accused player's SteamID, the server info, and interactive buttons for admins to mark as resolved.

### 💬 Rich Communication Commands
Full suite of admin-to-player messaging commands. Broadcast to all players with `!say`, send private messages with `!psay`, display prominent center-screen HTML messages with `!csay`, show HUD alerts with `!hsay`, or communicate exclusively with other online admins using `!asay`.

### ⚖️ Comprehensive Sanction Management
The most complete sanction system available for SwiftlyS2:
- **Ban**: By SteamID, by IP, or both simultaneously. Supports offline banning (`!addban`).
- **Mute / Gag / Silence**: Silence voice, text, or both. All with configurable durations.
- **Warn**: Issue formal warnings with reasons that are stored in the database. Accumulated warnings can be reviewed at any time.
- **Kick**: Instantly remove a player with an optional reason.
- **Predefined Reasons & Durations**: The in-game sanction menu presents a list of preconfigured reasons (e.g., "Hacking", "Insult players") and durations (5 min, 1 hour, permanent, etc.) so admins don't have to type anything manually.

### 🎮 30+ Fun & Utility Commands
Transform your server into a playground with a massive library of player manipulation commands. Freeze, burn, bury, unbury, blind, beacon, resize, glow, disarm, adjust gravity, change speed, set health, give money, give weapons, teleport (`!goto` / `!bring`), toggle noclip, toggle godmode, and much more. Each command supports targeting individual players, groups, or the entire server.

### 🔄 Discord Integration (Superior)
CS2_Admin's Discord integration is far beyond a simple webhook logger:
- **Admin Action Logs**: Every admin action (ban, kick, mute, etc.) is logged to a dedicated Discord channel with full details.
- **Chat Logs**: Optionally mirror all in-game chat to a Discord channel.
- **Connection Logs**: Track player connects and disconnects.
- **Live Server Status**: A self-updating embed message in your Discord that shows your server's current map, player count, IP address, online/offline status, and a quick-connect command — all updated every 30 seconds.
- **Leaderboard**: Automatically publishes a top-players leaderboard to Discord at a configurable interval.
- **Account Verification**: Players link their Steam account to their Discord user via `!verify` (in-game code) + a button/modal panel in your verify channel. Optionally grants a "Verified" role. Links are stored in the `admin_discord_links` table.
- **Command Blocker**: Optionally require the configured Discord verified role before players can use selected commands. Configure the command list in `commandblocker.json`; all other commands remain available.
- **Custom Banners**: Add your own server banner image to the status embed for a professional look.

### 🔁 Auto-Updater
When a new version is released on GitHub, the plugin automatically detects it on the next server start, downloads the update, replaces the old files, and reloads itself. Zero manual intervention required. You'll never run an outdated version again.

### 🌍 Multi-Language Support
All player-facing messages (chat notifications, center HTML alerts, menu labels) are fully translatable. The plugin ships with **Turkish (TR)**, **English (EN)**, and **Hungarian (HU)** translations out of the box. Adding a new language is as simple as creating a new `.jsonc` file in the `resources/language/` directory.

### 🗺️ Map Management
Change the current map from the in-game menu or via command with `!map`. Full support for **Workshop Maps** via `!wsmap`. The plugin reads from a configurable map list so you can curate which maps are available for selection.

### 🗳️ Voting System
Start server-wide votes with `!vote`. Admins can pose a question with multiple options, and all players can cast their vote through an interactive in-game menu.

### ⚙️ Server Mode Toggles
Quickly toggle server-wide gameplay modifications without touching config files:
- **Headshot Only** (`!hson` / `!hsoff`): Restrict kills to headshots only.
- **Bunny Hop** (`!bhopon` / `!bhopoff`): Enable or disable auto-bunnyhop for all players.
- **Auto Respawn** (`!respawnon` / `!respawnoff`): Players respawn automatically after death.

---

### Detailed Feature Spotlights

#### Admin Tag Manager
The tag system allows you to define a unique visual identity for each admin group. In your `tags.json`, you define per-group styles:
- **TagColor**: The color of the rank tag prefix (e.g., `[ADMIN]`, `[MOD]`).
- **NameColor**: The color of the admin's name in chat.
- **ChatColor**: The color of the admin's actual chat message text.

Example: A "SuperAdmin" group could have a `[gold]` tag, a `[red]` name, and `[white]` chat text, making their messages instantly recognizable. Regular players receive a configurable default tag (e.g., `PLAYER`). Tags are also displayed on the **in-game scoreboard** for extra visibility.

#### AFK Manager Module
The AFK Manager runs on a configurable timer (default: 30 seconds). It watches for player input (movement, mouse, shooting) and if no activity is detected, it issues a warning with a sound alert. If the player still doesn't respond, they are moved to spectator or kicked. The module can be configured to:
- Skip warmup rounds (so players aren't punished during warmup).
- Exempt admins from AFK checks entirely.
- Play a custom warning sound before taking action.

#### Admin Toptime
The playtime tracker records admin activity at configurable intervals (default: every 1 minute). Key features:
- `!admintime` — Check your own accumulated playtime.
- `!admintimesend` — Broadcast a top-admins leaderboard to the server and/or Discord.
- Configurable top limit (default: top 20 admins displayed).
- Optional auto-send on a specific day of the week (e.g., every Monday) with automatic reset after sending.

#### CallAdmin & Player Reports
- **CallAdmin**: Players type `!calladmin <reason>` to send a general help request. This posts a rich embed to your configured Discord channel with the player's name, SteamID, server info, and their message. Perfect for when no admin is online.
- **Report**: Players type `!report` to open a target selection menu, choose a player, and provide a reason. The report is sent to Discord with full details of both the reporter and the accused, including interactive **"Mark as Resolved"** and **"Player Punishments"** buttons for Discord-side moderation.

---

## Requirements

- **SwiftlyS2** - Core plugin framework
- **MySQL / MariaDB** - A database connection named `mysql_detailed` configured in your Swiftly `database.json`.

---

## Installation

1. Download the latest `CS2_Admin.zip` from the [Releases](https://github.com/candaysa/CS2_Admin/releases/latest) page.
2. Extract the contents directly into your server's `addons/swiftlys2/plugins/` directory.
3. Ensure your Swiftly `database.json` has a connection named `mysql_detailed`.
4. Restart the server or load the plugin via the Swiftly console command:
   ```bash
   swiftly load CS2_Admin
   ```

---

## Initial Setup

Once the plugin is installed and running, you need to set up your first Admin Group and assign yourself as an Admin via the server console.

**Step 1: Create an Admin Group**
Use the `sw_addgroup` command in your server console to create a master admin group with an immunity level (e.g., 100) and specific permissions (e.g., `admin.root` for full access).
```bash
sw_addgroup "RootAdmin" 100 "admin.root"
```

**Step 2: Add Yourself as an Admin**
Once the group is created, add yourself to this group using your SteamID64 and the `sw_addadmin` command. 
```bash
sw_addadmin 76561198XXXXXXX "YourName" "RootAdmin"
```
*Note: Replace `76561198XXXXXXX` with your actual SteamID64, and `"YourName"` with your admin nickname.*

Congratulations! You now have full access to the in-game admin menu (`!admin`) and all commands.

---

## Configuration

All configuration files are automatically generated in `addons/swiftlys2/configs/plugins/CS2_Admin/` upon first load. You can also view the full example configurations in the [example_configs](example_configs/) directory.

### `config.json`
Controls the core settings of the plugin, including language, prefix, ban mode, debug mode, and admin playtime tracking intervals.
[View Example config.json](example_configs/config.json)

### `commands.json`
Customize all command aliases. For example, change `!ban` to `!yasakla` or add additional aliases for any command.
[View Example commands.json](example_configs/commands.json)

### `permissions.json`
Defines which Swiftly permissions are mapped to each plugin feature and command.
[View Example permissions.json](example_configs/permissions.json)

### `discord.json`
Manages Discord bot token, channel IDs for logging, server status, reports, leaderboards, banner configuration, and account verification (`VerifyEnabled`, `VerifyChannelId`, `GuildId`, `VerifiedRoleId`).
[View Example discord.json](example_configs/discord.json)

### `commandblocker.json`
Enables or disables the Command Blocker and lists the commands that require the Discord verified role. The file is generated on first load with the feature disabled by default.
[View Example commandblocker.json](example_configs/commandblocker.json)

### `tags.json`
Configures the Admin Tag Manager: per-group chat colors, name colors, tag colors, scoreboard tags, and the default player tag.
[View Example tags.json](example_configs/tags.json)

### `maps.json`
Defines the available game maps and workshop maps that can be selected via `!map` and `!wsmap` commands.
[View Example maps.json](example_configs/maps.json)

---

## Command Blocker

Set `Enabled` to `true` in `commandblocker.json` and add the command names you want to restrict to `BlockedCommands`. For example:

```json
{
  "CS2AdminCommandBlocker": {
    "Version": 1,
    "Enabled": true,
    "BlockedCommands": ["shop", "ws", "skins"],
    "RoleCacheSeconds": 60
  }
}
```

Configure `VerifyEnabled`, `BotToken`, `GuildId`, and `VerifiedRoleId` in `discord.json` as well. A player can use a listed command only while their Steam account is linked to a Discord account with the configured verified role. Players without a link or role receive a translated chat message. The `!verify` and `!unverify` commands remain accessible so players can manage their link.

Only commands in `BlockedCommands` are affected. The module checks the Discord role asynchronously and caches successful results for `RoleCacheSeconds` (minimum 10, maximum 600 seconds) to reduce repeated requests. The first attempt while a role check is pending is blocked; the player can retry after it completes.

---

## Commands & Permissions

CS2_Admin natively ties into Swiftly's permission system. Below are all the commands included in the plugin.

### Core & Admin Management Commands
| Command | Default Permission | Description |
|---------|--------------------|-------------|
| `!admin` | `admin.generic` | Opens the main interactive admin menu |
| `!asay <message>` | `admin.generic` | Sends a message only to other admins |
| `!csay <message>` | `admin.generic` | Sends a centered message to all players |
| `!hsay <message>` | `admin.generic` | Sends a HUD message to all players |
| `!psay <target> <msg>` | `admin.generic` | Sends a private message to a specific player |
| `!say <message>` | `admin.generic` | Sends an admin chat message to all players |
| `!report [target]` | `@All` | Opens a player selection menu to report a player, or sends a direct report with a message |
| `!calladmin <reason>` | `@All` | Sends a help request message directly to the Discord CallAdmin channel |
| `!verify` | `@All` | Generates a code to link your Steam account to Discord via the verify panel |
| `!unverify` | `@All` | Removes your Steam ↔ Discord account link |
| `!afk` | `@All` | Marks yourself as AFK or checks AFK status |
| `!admintime` | `admin.generic` | Checks your own admin playtime |
| `!admintimesend` | `admin.root` | Broadcasts top admin playtimes |
| `!adminreload` | `admin.root` | Reloads CS2_Admin configurations |
| `!addadmin` | `admin.root` | Adds a new admin |
| `!editadmin` | `admin.root` | Edits an existing admin |
| `!removeadmin` | `admin.root` | Removes an admin |
| `!listadmins` | `admin.root` | Lists all admins |
| `!addgroup` | `admin.root` | Adds a new admin group |
| `!editgroup` | `admin.root` | Edits an existing admin group |
| `!removegroup` | `admin.root` | Removes an admin group |
| `!listgroups` | `admin.root` | Lists all admin groups |

### Sanction Commands
| Command | Default Permission | Description |
|---------|--------------------|-------------|
| `!ban <target> <time> [reason]` | `admin.ban` | Bans a player from the server |
| `!ipban <ip> <time> [reason]` | `admin.ban` | Bans an IP address |
| `!lastban <target>` | `admin.ban` | Opens a sanction menu for recently disconnected players (ban, mute, gag, warn) |
| `!addban <steamid> <time> [reason]` | `admin.ban` | Bans an offline player by SteamID64 |
| `!unban <steamid/ip>` | `admin.ban` | Unbans a player |
| `!warn [target] [reason]` | `admin.generic` | Issues a warning to a player (opens a target menu if no target is specified) |
| `!unwarn <target>` | `admin.generic` | Removes a warning from a player |
| `!listwarns <target>` | `admin.generic` | Lists a player's warnings |
| `!kick <target> [reason]` | `admin.kick` | Kicks a player from the server |
| `!mute <target> <time> [reason]` | `admin.mute` | Mutes a player's voice chat |
| `!unmute <target>` | `admin.mute` | Unmutes a player's voice chat |
| `!gag <target> <time> [reason]` | `admin.mute` | Mutes a player's text chat |
| `!ungag <target>` | `admin.mute` | Unmutes a player's text chat |
| `!silence <target> <time>` | `admin.mute` | Applies both Mute and Gag |
| `!unsilence <target>` | `admin.mute` | Removes both Mute and Gag |

### Fun & Utility Commands
| Command | Default Permission | Description |
|---------|--------------------|-------------|
| `!noclip <target>` | `admin.cheats` | Toggles noclip for a player |
| `!god <target>` | `admin.cheats` | Toggles godmode for a player |
| `!slap <target> [damage]` | `admin.cheats` | Slaps a player, dealing optional damage |
| `!slay <target>` | `admin.cheats` | Instantly kills a player |
| `!respawn <target>` | `admin.cheats` | Respawns a dead player |
| `!goto <target>` | `admin.cheats` | Teleports you to a player |
| `!bring <target>` | `admin.cheats` | Teleports a player to you |
| `!freeze <target> <time>` | `admin.cheats` | Freezes a player in place |
| `!unfreeze <target>` | `admin.cheats` | Unfreezes a player |
| `!resize <target> <scale>` | `admin.cheats` | Changes the physical size of a player |
| `!blind <target> <amount>` | `admin.cheats` | Blinds a player's screen |
| `!glow <target> <color\|r> <g> <b> [a]`| `admin.cheats` | Applies an outline glow to a player. Use a color name (red, green, blue, yellow, cyan, magenta, white, orange, purple, pink, lime, turquoise) or RGB values |
| `!rgb <target> [duration]` | `admin.cheats` | Applies a cycling rainbow glow (default 30s, max 300s). Use `off` to stop |
| `!beacon <target>` | `admin.cheats` | Places a pinging beacon ring around a player |
| `!bury <target>` | `admin.cheats` | Buries a player underground, trapping them |
| `!unbury <target>` | `admin.cheats` | Unburies a player, bringing them back up |
| `!burn <target> <time>` | `admin.cheats` | Sets a player on fire |
| `!disarm <target>` | `admin.cheats` | Strips all weapons from a player |
| `!speed <target> <multiplier>` | `admin.cheats` | Alters a player's movement speed |
| `!gravity <target> <multiplier>`| `admin.cheats` | Alters a player's gravity |
| `!hp <target> <amount>` | `admin.cheats` | Sets a player's health |
| `!money <target> <amount>` | `admin.cheats` | Sets a player's money |
| `!give <target> <item>` | `admin.cheats` | Gives a weapon/item to a player |
| `!rename <target> <new_name>` | `admin.cheats` | Forces a player's name to change |
| `!unrename <target>` | `admin.cheats` | Restores a player's original name |

### Player Management & Server Commands
| Command | Default Permission | Description |
|---------|--------------------|-------------|
| `!team <target> <t/ct/spec>` | `admin.cheats` | Forces a player into a specific team |
| `!mixteam` | `admin.cheats` | Shuffles all players evenly across teams |
| `!map <map_name>` | `admin.generic` | Changes the current map |
| `!wsmap <id>` | `admin.generic` | Changes the map to a Workshop map |
| `!rr` | `admin.generic` | Restarts the game/round |
| `!vote <question> [opt1]...` | `admin.generic` | Starts a server-wide vote |
| `!hson` / `!hsoff` | `admin.rcon` | Toggles headshot-only mode |
| `!bhopon` / `!bhopoff` | `admin.rcon` | Toggles auto-bunnyhop |
| `!respawnon` / `!respawnoff` | `admin.rcon` | Toggles auto-respawn |
| `!rcon <command>` | `admin.rcon` | Executes a server console command |
| `!cvar <cvar> [value]` | `admin.rcon` | Changes a server convar |
| `!players` | `@All` | Lists active players and their status |

---

## 🎯 Target Selection

When applying commands to players, **using the player's ID (`#id`) instead of their name** is highly recommended to prevent name confusion and accidental actions on the wrong player.

You can view player IDs by typing `!players` in chat or `status` in the server console. Each player's ID is also visible on the scoreboard tag (e.g., `#12 | ADMIN |`).

### Usage Example

If a player named **Ali** has ID **12**, instead of:
```
!kick Ali
```
Use:
```
!kick #12
```
This ensures you always target the correct player, even if multiple players have similar names.

### Single Target Formats
| Format | Example | Description |
|--------|---------|-------------|
| `#id` | `#12` | Targets a player by their unique server ID (recommended) |
| `name` | `Ali` | Targets a player by partial name match |
| `steamid` | `76561198XXXXXXX` | Targets a player by their SteamID64 |

### Mass Target Selectors
| Selector | Description |
|----------|-------------|
| `@all` | All players on the server |
| `@t` | All Terrorist players |
| `@ct` | All Counter-Terrorist players |
| `@alive` | All alive players |
| `@dead` | All dead players |
| `@humans` | All human (non-bot) players |
| `@bots` | All bot players |
| `@me` | Yourself (the command sender) |
