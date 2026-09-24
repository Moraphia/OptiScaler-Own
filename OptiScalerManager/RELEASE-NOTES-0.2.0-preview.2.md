# OptiScaler Manager 0.2.0-preview.2

- Expert settings now use the branch's embedded `OptiScaler.ini` documentation: short descriptions in the table and full source comments in a detail panel.
- Unknown keys remain editable and are labeled as undocumented instead of receiving guessed explanations.
- `dotnet publish` now always writes manager output to `OptiScalerManager/publish/` by default.
- Core test suite: 15 passing tests at packaging time.

Includes all changes from `0.2.0-preview.1`. The manager-only ZIP does not contain game installation binaries or third-party runtimes; supply a separate validated `Package/` folder when installing to a game.
