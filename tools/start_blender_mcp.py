"""启动 Blender GUI，并把 blender-mcp 的 socket 服务开起来（docs/17 路线 B）。

    blender.exe --python tools/start_blender_mcp.py

为什么需要它（T48 踩过的两个坑）：
1. `--python-expr "bpy.ops.preferences.addon_enable(...)"` 一旦抛异常，
   **Blender 会直接退出**（不是打印一下就继续），窗口当场消失、MCP 连不上。
   所以这里把启用放进 try/except —— 失败也只是打印，不会把窗口带走。
2. addon 只要被启用，`register()` 就会自动 start server（端口 9876），
   不需要人去 N 面板点 "Start MCP Server"。

注意：**不要**配 `--factory-startup`——那会忽略用户配置，addon 得再启用一次。
启动后可用 `Get-NetTCPConnection -LocalPort 9876` 确认在监听。
"""

import traceback

import bpy

try:
    bpy.ops.preferences.addon_enable(module="blender_mcp")
    print("[MCP] addon enabled")
except Exception as exc:  # noqa: BLE001 - 就是为了不让它带走 Blender
    print("[MCP] addon enable FAILED:", exc)
    traceback.print_exc()

try:
    server = bpy.types.blendermcp_server
    print("[MCP] server running =", server.running)
except Exception as exc:  # noqa: BLE001
    print("[MCP] server state unknown:", exc)
