# PiSwitch

PiSwitch is a macOS radial app switcher with per-instance menus (for example: `default`, `messaging`, `finder-groups`).

## Build

```bash
cd /path/to/piswitch
./scripts/build.sh
```

## Setup

Initialize local instance configs from public examples:

```bash
./scripts/init-config.sh
```

Then edit:

- `config/instances/default.json`
- `config/instances/messaging.json`
- `config/instances/finder-groups.json`

## Config Format

Minimum:

```json
{
  "apps": ["Safari", "Visual Studio Code", "Terminal", "Messages", "Mail"]
}
```

Optional per-instance overrides:

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

Notes:

- `colors` keys match app names (or finder-group names) in `apps`
- color values support hex (`#RGB`, `#RRGGBB`, `#RRGGBBAA`) and named system colors (`systemBlue`, `systemGreen`, etc.)
- `labels` overrides the text shown in each slice

## Run

Default:

```bash
./scripts/piswitch-launcher.sh
```

Messaging:

```bash
./scripts/piswitch-launcher.sh messaging
```

Finder groups:

```bash
./scripts/piswitch-launcher.sh finder-groups
```

## Karabiner Example

Example rules are in:

- `examples/karabiner/hyper-piswitch-rule.json`

They include:

- Caps Lock remapped to Hyper (`left_command + left_control + left_option + left_shift`)
- `Hyper+R` -> PiSwitch default
- `Hyper+H` -> PiSwitch messaging
- `Hyper+G` -> PiSwitch finder-groups

Replace `REPLACE_WITH_ABSOLUTE_PATH` in that file with your local project path.

## Finder Groups

For `finder-groups`, app names like `home`, `work`, `projects`, `archive` resolve to:

- `assets/finder-groups/<name>.app`
- fallback: `../bin/finder-groups/<name>.app`

## Repository Layout

- `Sources/PiSwitch/main.swift`: app logic
- `scripts/`: build, launcher, and setup scripts
- `config/examples/`: public example configs
- `config/instances/`: local instance configs
- `examples/karabiner/`: Karabiner snippets

## License

MIT. See `LICENSE`.
