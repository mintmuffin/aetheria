@echo off
title Aetheria Roleplay Studio
echo Starting Aetheria Roleplay Studio...
python app\main.py
if errorlevel 1 (
    echo.
    echo Application exited with an error.
    pause
)
