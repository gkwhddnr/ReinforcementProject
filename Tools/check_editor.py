import argparse
import importlib.util
import json
import sys
import time
import urllib.request
from pathlib import Path


def check_mcp(url):
    headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}

    def request(method, request_id=None, params=None):
        message = {"jsonrpc": "2.0", "method": method}
        if request_id is not None:
            message["id"] = request_id
        if params is not None:
            message["params"] = params
        payload = json.dumps(message).encode("utf-8")
        with urllib.request.urlopen(urllib.request.Request(url, payload, headers), timeout=20) as response:
            session_id = response.headers.get("Mcp-Session-Id")
            if session_id:
                headers["Mcp-Session-Id"] = session_id
            content = response.read()
        return json.loads(content) if content else None

    initialized = request("initialize", 1, {
        "protocolVersion": "2025-11-25",
        "capabilities": {},
        "clientInfo": {"name": "rein-project-check", "version": "1.0"},
    })
    if "error" in initialized:
        raise RuntimeError(initialized["error"])
    request("notifications/initialized")
    tools = request("tools/list", 2)
    if "error" in tools or not tools.get("result", {}).get("tools"):
        raise RuntimeError(f"MCP tools unavailable: {tools}")
    print("MCP tools:", ", ".join(tool["name"] for tool in tools["result"]["tools"]))


def check_python(engine_dir):
    module_path = engine_dir / "Engine/Plugins/Experimental/PythonScriptPlugin/Content/Python/remote_execution.py"
    spec = importlib.util.spec_from_file_location("unreal_remote_execution", module_path)
    remote_module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(remote_module)
    remote = remote_module.RemoteExecution()
    remote.start()
    try:
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            nodes = [node for node in remote.remote_nodes if node.get("project_name") == "Rein_Project"]
            if nodes:
                break
            time.sleep(0.2)
        if len(nodes) != 1:
            raise RuntimeError(f"Expected one Rein_Project Python endpoint: {remote.remote_nodes}")
        remote.open_command_connection(nodes[0]["node_id"])
        result = remote.run_command(
            "unreal.SystemLibrary.get_engine_version()",
            exec_mode=remote_module.MODE_EVAL_STATEMENT,
            raise_on_failure=True,
        )
        print("Unreal Python API:", json.dumps(result, ensure_ascii=False))
    finally:
        remote.stop()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Verify a running Rein_Project editor's MCP and Python APIs.")
    parser.add_argument("--url", default="http://127.0.0.1:8000/mcp")
    parser.add_argument("--engine-dir", type=Path, default=Path("C:/Program Files/Epic Games/UE_5.8"))
    arguments = parser.parse_args()
    sys.dont_write_bytecode = True
    check_mcp(arguments.url)
    check_python(arguments.engine_dir)
