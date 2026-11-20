# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-11-20

### Added
- Drivers are bundled: the official signed ViGEmBus 1.22.0 and HidHide 1.5.230 installers are embedded in `reController.exe` and installed silently from a **One-time setup** screen (single UAC prompt). Broken/outdated ViGEmBus installs are detected and repaired. `--install-drivers` for unattended setup.
- Portable release zip (`portable.txt` keeps all data next to the exe) and reproducible packaging script `build/package.ps1`.
- `THIRD-PARTY-NOTICES.md`.
- 1000 Hz remapping engine with per-controller sessions, MMCSS scheduling and a high-resolution timer.
- Input: XInput (incl. Guide button) and SDL3 (DualShock 4, DualSense, Switch Pro, paddles, generic pads).
- Output: virtual Xbox 360 controller via ViGEmBus with rumble forwarding; keyboard (scan codes) and mouse (buttons, wheel, stick-to-mouse) via SendInput.
- Mappings to gamepad buttons / stick directions / triggers, key combos, mouse buttons and wheel; disabling controls.
- Activation modes: hold, toggle, turbo, auto-fire. Three shift layers with hold or toggle.
- Stick shaping (deadzone, saturation, anti-deadzone, curve, inversion, swap) and trigger shaping (deadzone, max, threshold, hair trigger).
- Games with multiple configs, 4 slots per controller with on-controller slot switching, automatic switching by executable.
- Live input tester, controller diagram with live highlighting, profile import/export.
- HidHide integration through an allow-listed elevated helper.
- Example games are seeded only once per data folder (prevents duplicate "General"/"Desktop" entries).
- Tray icon, start with Windows, single instance, accent colours, portable mode, crash-safe storage, rolling logs, `--selftest` diagnostics.
