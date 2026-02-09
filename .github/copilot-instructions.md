# Copilot Instructions

## General Guidelines
- Prefer PowerShell Core (pwsh) with args `-NoProfile -ExecutionPolicy Bypass -File`.
- Do not change the process WorkingDirectory; use whatever the terminal/host is set to.
- Ensure consistent behavior across solution explorer, editor, and ribbon.
- Support single-file execution only.
- Use the Output Window pane for output.
- Provide an Options page for mappings.
- User will need help building/testing the VSIX.