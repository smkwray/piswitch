# PiSwitch

PiSwitch is a macOS radial app switcher with per-instance menus (for example: default, messaging, finder-groups).

## Status

`pie-switcher` is now deprecated in favor of `piswitch`.

## Project Layout

- `Sources/PiSwitch/main.swift`: app + daemon + IPC logic
- `scripts/build.sh`: release build
- `scripts/piswitch-launcher.sh`: launcher used by Karabiner
- `scripts/init-config.sh`: copies sample configs into local instances
- `config/examples/*.json`: public-safe example configs
- `config/instances/*.json`: your private local configs
- `examples/karabiner/hyper-piswitch-rule.json`: Karabiner example rules
- `run/`: runtime state

## Build

```bash
cd /path/to/piswitch
./scripts/build.sh
```

## First-Time Setup

1. Initialize local config files from examples:

```bash
./scripts/init-config.sh
```

2. Edit your private configs in:

- `config/instances/default.json`
- `config/instances/messaging.json`
- `config/instances/finder-groups.json`

## Config Format

Minimum format:

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

## Karabiner Example (Caps -> Hyper + PiSwitch)

An example is included at:

- `examples/karabiner/hyper-piswitch-rule.json`

What it contains:

- Caps Lock remapped to Hyper (`left_command + left_control + left_option + left_shift`)
- `Hyper+R` -> PiSwitch default
- `Hyper+H` -> PiSwitch messaging
- `Hyper+G` -> PiSwitch finder-groups

Important:

- Replace `REPLACE_WITH_ABSOLUTE_PATH` in the example with your real path.
- Back up `~/.config/karabiner/karabiner.json` before editing.

## Finder Groups

For finder-groups instance values like `home`, `work`, `projects`, `archive`, PiSwitch resolves app bundles in:

- `assets/finder-groups/<name>.app`
- fallback: `../bin/finder-groups/<name>.app`

## Public GitHub Safety

This repo is set up so personal files do not get committed:

- `config/instances/*.json` is gitignored
- `assets/finder-groups/` is gitignored (keep your local group apps private)
- `run/`, `.build/`, `dist/` are gitignored

Use only these as public examples:

- `config/examples/*.json`
- `examples/karabiner/hyper-piswitch-rule.json`

If you ever accidentally stage private files, unstage them with:

```bash
git rm --cached -r assets/finder-groups config/instances
```

## First GitHub Publish (CLI)

1. Create a new empty GitHub repo in browser (no README/license generated there).
2. Run locally:

```bash
cd /path/to/piswitch
git init
git add .
git commit -m "Initial PiSwitch release"
git branch -M main
git remote add origin https://github.com/<your-user>/<repo>.git
git push -u origin main
```

3. Add a license (recommended: MIT) and push that commit.

## Recommended Before Public Release

1. Add `LICENSE`.
2. Add screenshots or short GIF in README.
3. Test on a clean macOS user profile.
4. Remove or sanitize any absolute paths in examples.
5. Tag a release (`v0.1.0`) after first stable publish.

## License

MIT. See `LICENSE`.
