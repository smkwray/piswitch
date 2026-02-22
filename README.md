<p align="center">
  <img src="assets/logo/piswitch-logo.png" alt="PiSwitch logo" width="420">
</p>

# PiSwitch

A macOS radial app switcher with multiple instance menus (e.g. `default`, `messaging`, `finder-groups`). Trigger it with a hotkey, flick toward the app you want, release.

Pronounced `/pɪs.wɪtʃ/` — pie + switch.

<p align="center">
  <img src="assets/screenshots/piswitch-screenshot.png" alt="PiSwitch screenshot" width="280">
</p>

## Build

```bash
cd /path/to/piswitch
./scripts/build.sh
```

Requires Xcode command-line tools (`xcode-select --install`).

## Setup

Initialize local configs from the bundled examples:

```bash
./scripts/init-config.sh
```

Then customize the instance files:

- `config/instances/default.json` — your main app switcher
- `config/instances/messaging.json` — chat/communication apps
- `config/instances/finder-groups.json` — Finder group shortcuts

## Config format

Minimal config — just list the apps:

```json
{
  "apps": ["Safari", "Visual Studio Code", "Terminal", "Messages", "Mail"]
}
```

With optional overrides for colors and labels:

```json
{
  "apps": ["Safari", "Visual Studio Code", "Terminal", "Messages", "Mail"],
  "colors": {
    "Safari": "#2AA8FF",
    "Visual Studio Code": "systemBlue",
    "Messages": "#34C759"
  },
  "labels": {
    "Visual Studio Code": "VS Code",
    "Messages": "Msg"
  }
}
```

**Colors** accept hex (`#RGB`, `#RRGGBB`, `#RRGGBBAA`) or macOS system colors (`systemBlue`, `systemGreen`, etc.). Keys match app names in the `apps` list.

**Labels** override the text displayed in each pie slice.

Many popular apps (Safari, VS Code, iTerm, Claude, Slack, Discord, etc.) have built-in default colors and short labels, so you only need overrides for apps that aren't covered or when you want something custom.

## Usage

```bash
# Default instance
./scripts/piswitch-launcher.sh

# Named instance
./scripts/piswitch-launcher.sh messaging
./scripts/piswitch-launcher.sh finder-groups
```

## Hotkey setup with Karabiner-Elements

Example rules in `examples/karabiner/hyper-piswitch-rule.json`:

| Hotkey | Action |
|---|---|
| Caps Lock | Remapped to Hyper (`Cmd+Ctrl+Opt+Shift`) |
| Hyper + R | PiSwitch default |
| Hyper + H | PiSwitch messaging |
| Hyper + G | PiSwitch finder-groups |

To use: copy the rule into your Karabiner config and replace `REPLACE_WITH_ABSOLUTE_PATH` with your local project path.

## Finder groups

For the `finder-groups` instance, app names like `home`, `work`, `projects` resolve to stub `.app` bundles at:

1. `assets/finder-groups/<name>.app`
2. Fallback: `../bin/finder-groups/<name>.app`

These open specific Finder locations rather than real applications.

## Project layout

```
Sources/PiSwitch/main.swift    App logic (single-file Swift)
scripts/
  build.sh                     Build the .app bundle
  piswitch-launcher.sh         Launch with optional instance name
  init-config.sh               Copy example configs to instances/
  smoke-test.sh                Basic sanity check
config/
  examples/                    Public example configs
  instances/                   Local configs (gitignored)
examples/karabiner/            Karabiner-Elements snippets
assets/
  logo/                        Project logo
  screenshots/                 Screenshots
  finder-groups/               Stub .app bundles for Finder groups
```

## License

MIT. See `LICENSE`.
