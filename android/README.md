# Aetheria Android App — Mobile Roleplay Companion

**Aetheria Mobile** is the official Android companion application for the **Aetheria Windows 11 AI Roleplay Platform**. It lets you connect to multiplayer roleplay sessions hosted on your PC directly from your Android phone, with full feature compliance to the PC version.

---

## 🌟 Features & Parity with PC Version

- **Feature Compliant with PC Version**:
  - Connects to any active session (Chainsaw Man, Death Note, Custom Worlds)
  - Supports **Couple Roleplay** (`TwoPersonPrivate`) and **Co-Op PvE** (`CooperativePvE`)
  - **Live AI Streaming**: Real-time token streaming directly from KoboldCpp 8B local LLM
  - **5 Action Modes**:
    - 💬 **Speak** — Dialog in quotes
    - ⚔️ **Act** — Physical actions & combat maneuvers
    - 💭 **Think** — Internal character thoughts
    - 📜 **Narrate** — World state & GM narration
    - ⚙️ **OutOfCharacter (OOC)** — Player table talk
  - **Multiplayer Character Sync**: Create custom characters on your phone and sync them instantly to the PC host!
  - **World Lore & Canon Browser**: Inspect Tokyo Division 4, Gun Devil, Soviet Union, Kanto Police, SPK, and canon lore entries while playing.
  - **Interactive Dice Roller**: Roll D20 and D100 dice directly with results broadcast to all players in chat.
  - **Auto-Discovery**: Auto-detects your PC host on the local Wi-Fi via UDP broadcast.

---

## 🚀 Two Ways to Run on Your Phone

### Option 1: Instant 5-Second Play via Mobile Web / PWA (Zero Compiling Required)

Aetheria on your PC includes a built-in companion web server that runs simultaneously whenever you host a session:

1. On your PC, click **Host Session** in Aetheria (default port is `7777`, web companion port is `7778`).
2. On your Android phone, connect to the same Wi-Fi network.
3. Open **Google Chrome** on your phone and navigate to:
   ```
   http://<YOUR_PC_LOCAL_IP>:7778/
   ```
   *(Example: `http://192.168.1.100:7778` — you can copy your exact IP from Aetheria's Multiplayer screen on PC!)*
4. In Chrome, tap the **⋮ (three dots)** menu and select **"Install App"** or **"Add to Home screen"**.
5. The **Aetheria Roleplay** icon appears on your phone's home screen as a standalone Android app with full offline styling and hardware acceleration!

---

### Option 2: Compile the Native Android Studio App

The `android/` directory is a clean, standard Android Studio / Gradle project.

#### Requirements
- Android Studio (Hedgehog, Iguana, Jellyfish, or newer) **OR** Android SDK Command-line Tools
- JDK 17 or higher
- Android phone running Android 8.0 (Oreo) or newer (API 26+)

#### Step-by-Step Compilation in Android Studio:
1. Open **Android Studio**.
2. Select **File -> Open...** and browse to this directory:
   ```
   c:\Users\Administrator\Documents\antigravity\friendly-lavoisier\android
   ```
3. Wait for Gradle to sync dependencies.
4. Enable **Developer Options** and **USB Debugging** on your phone, then connect it via USB cable (or start an Android Emulator).
5. Click the green **Run (▶)** button or press `Shift + F10`.
6. The app will compile and install onto your phone!

#### Command Line Compilation (Terminal):
```powershell
cd android
./gradlew assembleDebug
```
The output APK will be generated at:
```
android/app/build/outputs/apk/debug/app-debug.apk
```
You can install it directly to a connected phone using:
```bash
adb install android/app/build/outputs/apk/debug/app-debug.apk
```
Or simply transfer `app-debug.apk` to your phone via USB or Google Drive and tap to install!

---

## 📱 How to Connect from Your Phone

1. **Start Host on PC**:
   - In Aetheria on PC, go to **Multiplayer**.
   - Select your persona (e.g., *Denji* or *Light Yagami*).
   - Click **Start Host Server**.
   - Note the **Invite Token** displayed on screen (e.g., `A3F91B2C8D4E`).

2. **Open App on Phone**:
   - The app will attempt to auto-discover your PC.
   - If prompted, enter:
     - **Host IP**: Your PC's local IP (e.g. `192.168.1.100` or Hamachi/Radmin IP if playing over VPN).
     - **Port**: `7778`
     - **Invite Token**: The token from your PC screen.
     - **Your Name**: Any name you want to play as (e.g. *Reze* or *Misa*).
   - Tap **Connect**!

3. **Enjoy Playing!**:
   - Type messages in Speak, Act, Think, or Narrate mode.
   - Watch the PC's local 8B KoboldCpp AI generate responses in real time.
   - Tap 👤 to create and sync custom characters to the PC host!
