# ReinforcementProject

Unreal Engine 5.8.3 reinforcement-learning project environment.

## Editor setup

- Python Editor Script Plugin and Editor Scripting Utilities are enabled for editor targets.
- Unreal MCP (`ModelContextProtocol`), Toolset Registry and All Toolsets are enabled.
- The MCP server starts with the editor at `http://127.0.0.1:8000/mcp`.
- `.codex/config.toml` configures Codex for this project. Open the project as a trusted
  Codex workspace after starting the editor. Reconnect the client after restarting UE.
- `.mcp.json` provides the same endpoint for compatible HTTP MCP clients.
- `Content/Python/init_unreal.py` runs on editor startup and verifies the Unreal Python API.
- Python remote execution is enabled on loopback with discovery at `239.0.0.1:6766`.
  External Python scripts can use the engine's `PythonScriptPlugin/Content/Python/remote_execution.py`.

The Unreal Python API is an editor automation API. Runtime reinforcement-learning
communication and training dependencies will be configured separately.

With the editor running, verify both APIs using `python Tools/check_editor.py`.
Use `--engine-dir` if the engine is installed elsewhere. The checker uses only
Python's standard library and the engine-provided remote execution module.

## Build and run

On the initial development machine, use `BuildEditor.cmd` with the editor closed,
then `OpenEditor.cmd`. The scripts use the ASCII directory junction
`C:\UnrealProjects\Rein_Project` to avoid compiler problems with Korean paths.
See `BUILD-NOTES.md` for details. On another machine, clone into an ASCII-only path
and build/open `Rein_Project.uproject` with UE 5.8.3; adapt the launcher paths if needed.

Source, configuration and project assets are tracked. Generated binaries, build caches,
logs, intermediate backups, IDE settings and Python environments are excluded.

Official references:

- [Epic Unreal MCP setup](https://dev.epicgames.com/documentation/en-us/unreal-engine/unreal-mcp-in-unreal-editor)
- [Codex MCP configuration](https://developers.openai.com/codex/mcp)
