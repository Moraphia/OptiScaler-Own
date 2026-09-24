# OptiScaler Manager 0.2.0-preview.1

- Reworked the game library and made common/expert configuration directly accessible.
- Grouped expert INI settings by feature, added descriptions and editable preset dropdowns.
- Labeled frame generation presets as 2×–6× (INI values 1–5); an existing value 6 remains visible as an experimental 7× request.
- Added package preflight for `OptiScaler.ini`, configuration diff confirmation, automatic snapshots, and encoding/newline preservation.
- Added dependency-readiness information and an optional own-GitHub-Release update channel with SHA-256 verification.
- Core test suite: 14 passing tests at packaging time.

This ZIP contains the manager only. It does not include `OptiScaler.dll`, NVIDIA/AMD/Intel runtimes, or game files. For installation, provide a separately validated and licensed `Package/` folder beside the manager executable.
