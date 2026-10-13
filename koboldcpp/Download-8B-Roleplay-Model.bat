@echo off
title Download 8B Roleplay Model (4.7 GB Q4_K_M for RX 580 8GB VRAM)
cd /d "%~dp0"
if not exist "models" mkdir "models"

echo ============================================================================
echo  Downloading Llama-3.1-8B-Instruct-Q4_K_M.gguf (~4.7 GB via Cloudflare/Google CDN)
echo  Fits 100%% in AMD Radeon RX 580 8GB VRAM with 4096 Context Window!
echo ============================================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$m = Invoke-RestMethod -Uri 'https://registry.ollama.ai/v2/library/llama3.1/manifests/8b' -Headers @{'Accept'='application/vnd.docker.distribution.manifest.v2+json'}; $layer = $m.layers | Where-Object { $_.mediaType -eq 'application/vnd.ollama.image.model' } | Select-Object -First 1; Write-Host ('Downloading digest ' + $layer.digest + '...'); curl.exe -L ('https://registry.ollama.ai/v2/library/llama3.1/blobs/' + $layer.digest) -o 'models\Llama-3.1-8B-Instruct-Q4_K_M.gguf'"

echo.
echo Download complete! You can now run Start-KoboldCpp-RX580.bat
pause
