# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Please **do not** open a public issue. Use GitHub's *Report a vulnerability* (private security advisory) on this repository. You should receive a response within 7 days.

## Design notes

* reController runs as a normal user (`asInvoker`). It never requires administrator rights for remapping.
* Changing HidHide settings requires elevation. The app re-launches itself with `--elevated-batch <request> <response>` through UAC. The elevated process:
  * accepts only an allow-list of HidHide CLI verbs (`--cloak-*`, `--dev-*`, `--app-*`), rejects quotes / NUL characters and caps the batch size;
  * ignores any executable path from the request and locates `HidHideCLI.exe` itself (registry / Program Files);
  * exchanges data through a freshly created per-user temp directory that is deleted afterwards.
* Injected keyboard / mouse events are tagged with a fixed `dwExtraInfo` signature (`0x52434E54`) so other software can recognise them.
* No network access is made by the application. There is no telemetry.
