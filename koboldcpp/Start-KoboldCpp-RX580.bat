@echo off
title Aetheria - KoboldCpp Vulkan Server (8B + 8192 Context Profile)
cd /d "%~dp0"

set "MODEL_FILE="
if exist "%~dp0models\Llama-3.1-8B-Instruct-Q4_K_M.gguf" (
    set "MODEL_FILE=%~dp0models\Llama-3.1-8B-Instruct-Q4_K_M.gguf"
) else (
    for %%F in ("models\*.gguf") do (
        set "MODEL_FILE=%%~fF"
    )
)

if "%MODEL_FILE%"=="" (
    echo [ERROR] No .gguf model found in "%~dp0models\".
    echo Place any .gguf model into "%~dp0models\" or run Download-8B-Roleplay-Model.bat first.
    pause
    exit /b 1
)

echo ============================================================================
echo  AETHERIA LOCAL AI SERVER - 8B + 8192 CONTEXT PROFILE:
echo    GPU : Vulkan Hardware Acceleration (33 GPU Layers + Q8 KV Cache)
echo    CPU : High-Performance AVX2 Threads (BLAS Batch 512)
echo    CTX : 8192 Tokens (Smart Context + Flash Attention + 8-bit KV Cache)
echo    API : http://127.0.0.1:5001/v1 (OpenAI-Compatible for Aetheria)
echo    MDL : %MODEL_FILE%
echo ============================================================================
echo.

"%~dp0koboldcpp.exe" --model "%MODEL_FILE%" --usevulkan --gpulayers 33 --threads 10 --blasthreads 10 --blasbatchsize 512 --contextsize 8192 --quantkv q8_0 --smartcontext --port 5001 --skiplauncher
