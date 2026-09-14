@echo off
rem 双击这个文件 = 打开 Blender 并载入主角模型（含骨架与两个动作）
rem 说明见 docs\18-Blender零基础第一步.md
set BLENDER=C:\Users\Gdy\Blender\blender-4.5.13-windows-x64\blender.exe
start "" "%BLENDER%" --factory-startup --python "%~dp0open_model_in_blender.py"
