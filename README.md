# Aetheria — AI Roleplay Studio & Multi-User Co-Op Engine

[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%2F%2010%20%7C%20Android-blue.svg)](https://github.com/mintmuffin/aetheria)
[![Technology](https://img.shields.io/badge/.NET-8.0%20WinUI%203-purple.svg)](https://dotnet.microsoft.com/)
[![Local AI](https://img.shields.io/badge/Local%20AI-KoboldCpp%20%7C%20Ollama-green.svg)](https://github.com/LostRuins/koboldcpp)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

**Aetheria** is a high-performance, native roleplaying studio and collaborative co-op narrative engine built for Windows and mobile. It pairs local or cloud AI language models with deep worldbuilding systems, persistent character sheets, dice mechanics, and authoritative real-time multiplayer.

---

## Key Features

- **Windows 11 Native UI:** Modern WinUI 3 desktop client featuring Windows App SDK, Mica backdrops, fluid animations, and dedicated management workspaces.
- **Local & Cloud AI Freedom:** Direct integration with **KoboldCpp** (Vulkan/CUDA acceleration), **Ollama**, **LM Studio**, as well as cloud providers (**Google Gemini**, **OpenAI ChatGPT**, **Anthropic Claude**, **OpenRouter**, **DeepSeek**, and **Groq**).
- **Multi-User Co-Op Engine:** Authoritative TCP/UDP game server allowing friends and couples to roleplay together in real-time over home LAN or virtual networks (Tailscale, ZeroTier).
- **Mobile Companion (Android & Web):** Connect to PC-hosted sessions directly from your mobile device via a lightweight native Android APK or mobile web browser.
- **Deep Worldbuilding System:** Dynamic lorebooks with keyword-triggered injection, multi-region locations, faction disposition tracking, inventory states, and world variables.
- **Secure by Design:** Windows DPAPI credential encryption (`%LOCALAPPDATA%\Aetheria\credentials.vault`). API keys are never stored in plain text, shared across sessions, or leaked in export bundles.
- **Portable & Standalone:** Zero runtime requirements—runs out-of-the-box without requiring separate Python or .NET installations.

---

## Quick Start Guide

### 1. Installation

Download the latest release from the [Releases](https://github.com/mintmuffin/aetheria/releases) section:

| Package | Description | Recommended For |
| :--- | :--- | :--- |
| **`Aetheria-Setup-win-x64.exe`** | Windows Setup installer with Start Menu & Desktop shortcuts | Standard desktop installation |
| **`Aetheria-Portable-win-x64.zip`** | Standalone portable bundle—extract and run `Aetheria.exe` | Zero-install / USB drive usage |
| **`Aetheria-Roleplay-v1.0.0.apk`** | Lightweight native Android client for mobile co-op | Android phones and tablets |

> **Note on Windows SmartScreen:** Because release binaries are compiled locally without an expensive commercial code-signing certificate, Windows SmartScreen may show *"Windows protected your PC"*. Click **More info** &rarr; **Run anyway**.

---

### 2. Setting Up an AI Backend

Aetheria supports both offline local models and cloud API providers. You can choose whichever fits your hardware:

#### Option A: Local AI via KoboldCpp (Recommended for Privacy & Offline Play)
1. Download [KoboldCpp](https://github.com/LostRuins/koboldcpp/releases).
2. Download any recommended GGUF model (e.g., Llama 3.1 8B, Mistral Nemo 12B, or Stheno 8B).
3. Start KoboldCpp with GPU acceleration (Vulkan or CUDA):
   ```bash
   koboldcpp.exe --model <path-to-model>.gguf --usevulkan --gpulayers 33 --contextsize 8192 --port 5001
   ```
4. In Aetheria, open **Models** and select the **KoboldCpp Local** preset (`http://127.0.0.1:5001/v1`).

#### Option B: Local AI via Ollama
1. Install [Ollama](https://ollama.com) and pull your preferred model:
   ```bash
   ollama run llama3.1:8b
   ```
2. In Aetheria, select the **Ollama Local** preset (`http://127.0.0.1:11434`).

#### Option C: Cloud AI Providers (Zero GPU Required)
1. Navigate to the **Models** workspace in Aetheria.
2. Select your provider (**Google Gemini**, **OpenAI**, **Anthropic Claude**, **OpenRouter**, or **DeepSeek**).
3. Enter your API key and click **Save & Encrypt**. Your credentials will be encrypted using Windows DPAPI.

---

### 3. Multiplayer & Co-Op Sessions

Aetheria allows two or more players to participate in shared roleplay sessions with synchronized turns, character management, and live AI responses.

#### Hosting a Session (Host PC)
1. Launch **Aetheria** and navigate to the **Multiplayer** workspace.
2. Choose your **Game Mode**:
   - **Two-Person Private:** Designed for intimate two-player roleplay with optional AI narration.
   - **Cooperative PvE:** Join forces against adversary characters and boss encounters.
   - **Freeform / Sandbox:** Unconstrained open roleplaying.
3. Click **Start Hosting Server** (default port `7777`).
4. Click **Copy Invite Details** to copy your local IP, port, and session invite code.

#### Joining from a Second PC
1. Launch Aetheria on the second PC and go to **Multiplayer**.
2. If on the same Wi-Fi, click **Scan LAN (UDP)** to auto-discover the host.
3. If connecting remotely, enter the host's IP address (or virtual LAN address via Tailscale/ZeroTier).
4. Enter the **Invite Token**, pick a persona, and click **Connect to Host**.

#### Joining from a Mobile Device (Android / Phone)
- **Using the Android App:** Install `Aetheria-Roleplay-v1.0.0.apk`. Tap **Scan LAN** to find your host computer, or enter your host PC's Wi-Fi IP address (e.g. `192.168.1.X:7777`) and tap **Connect**.
- **Using Any Mobile Web Browser:** While hosting on PC, open your phone's browser and go to `http://<HOST_IP>:7778` for an instant web companion interface with real-time AI streaming, dice roller, and chat.

---

## Hardware Recommendations (Local Models)

| Hardware Tier | Recommended Model | Layers to GPU | Target Speed |
| :--- | :--- | :--- | :--- |
| **8GB VRAM (Mid-range GPU)** | 7B – 8B GGUF (`Q4_K_M`) | 100% offload (33 layers) | ~15–25 tokens/sec |
| **12GB – 16GB VRAM** | 12B – 14B GGUF (`Q4_K_M` / `Q5_K_M`) | 100% offload | ~20–35 tokens/sec |
| **CPU Only / Integrated Graphics** | 3B – 7B GGUF or Cloud APIs | CPU threads = Physical cores | ~4–8 tokens/sec |

*Note: For systems with modest GPUs, utilizing Vulkan or OpenCL backends in KoboldCpp ensures broad compatibility across AMD, Intel, and NVIDIA cards.*

---

## File Formats (`AETHERIA_RP_FORMAT` v1.0)

Aetheria uses a documented JSON schema format for easy sharing and backup:

- **`.rpchar`** — Standalone character sheet with personality, speech style, lore hooks, and first message.
- **`.rppersona`** — Player persona definition.
- **`.rpworld`** — Complete world setting, locations, factions, and keyword-triggered lorebook entries.
- **`.rpmodel`** — AI provider configuration and generation parameter presets (never contains credentials).
- **`.rpsession`** — Saved session state with conversation history, turn queue, and world state variables.
- **`.rpack`** — Bundled package containing multiple characters, worlds, and personas.

Full schema details are available in [`SCHEMA_SPECIFICATION.md`](SCHEMA_SPECIFICATION.md).

---

## Building from Source

### Prerequisites
- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (with *.NET Desktop Development* and *Windows App SDK C# Tools*) or VS Code

### Build Commands
```powershell
# Clone the repository
git clone https://github.com/mintmuffin/aetheria.git
cd aetheria

# Restore dependencies and build solution
dotnet restore Aetheria.sln
dotnet build Aetheria.sln -c Release

# Run automated tests
dotnet test tests/Aetheria.Tests/Aetheria.Tests.csproj

# Build standalone distribution installer & packages
powershell -ExecutionPolicy Bypass -File .\packaging\build-release.ps1
```

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
