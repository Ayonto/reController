# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-11-18

### Added
- 1000 Hz remapping engine with per-controller sessions, MMCSS scheduling and a high-resolution timer.
- Input: XInput (incl. Guide button) and SDL3 (DualShock 4, DualSense, Switch Pro, paddles, generic pads).
- Output: virtual Xbox 360 controller via ViGEmBus with rumble forwarding; keyboard (scan codes) and mouse (buttons, wheel, stick-to-mouse) via SendInput.
- Mappings to gamepad buttons / stick directions / triggers, key combos, mouse buttons and wheel; disabling controls.
- Activation modes: hold, toggle, turbo, auto-fire. Three shift layers with hold or toggle.
- Stick shaping (deadzone, saturation, anti-deadzone, curve, inversion, swap) and trigger shaping (deadzone, max, threshold, hair trigger).
- Games with multiple configs, 4 slots per controller with on-controller slot switching, automatic switching by executable.
- Live input tester, controller diagram with live highlighting, profile import/export.
- HidHide integration through an allow-listed elevated helper.
- Tray icon, start with Windows, single instance, accent colours, portable mode, crash-safe storage, rolling logs, `--selftest` diagnostics.
