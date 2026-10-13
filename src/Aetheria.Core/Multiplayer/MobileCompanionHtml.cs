using System;

namespace Aetheria.Core.Multiplayer;

public static class MobileCompanionHtml
{
    public static string GetManifestJson()
    {
        return """
        {
          "name": "Aetheria AI Roleplay Mobile",
          "short_name": "Aetheria",
          "description": "Windows 11 AI Roleplay Platform - Android Companion",
          "start_url": "/",
          "display": "standalone",
          "background_color": "#12121A",
          "theme_color": "#181822",
          "orientation": "portrait",
          "icons": [
            {
              "src": "data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><rect width='100' height='100' rx='20' fill='%236248D4'/><path d='M50 18 L76 78 L58 78 L50 56 L42 78 L24 78 Z' fill='white'/><circle cx='50' cy='42' r='5' fill='%2338BDF8'/></svg>",
              "sizes": "192x192 512x512",
              "type": "image/svg+xml",
              "purpose": "any maskable"
            }
          ]
        }
        """;
    }

    public static string GetMobileAppHtml(int tcpPort, int httpPort, string inviteToken)
    {
        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover">
          <meta name="theme-color" content="#181822">
          <meta name="apple-mobile-web-app-capable" content="yes">
          <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
          <title>Aetheria Mobile Companion</title>
          <link rel="manifest" href="/manifest.json">
          <style>
            :root {
              --bg: #12121A;
              --surface: #1A1A26;
              --card: #232334;
              --card-border: #313148;
              --primary: #8B6CEE;
              --primary-glow: rgba(139, 108, 238, 0.25);
              --text: #F3F4F6;
              --text-muted: #9CA3AF;
              --accent-speak: #A78BFA;
              --accent-act: #38BDF8;
              --accent-think: #F472B6;
              --accent-narrate: #FBBF24;
              --accent-ooc: #9CA3AF;
              --danger: #EF4444;
              --success: #10B981;
            }
            * { box-sizing: border-box; margin: 0; padding: 0; -webkit-tap-highlight-color: transparent; }
            body {
              background-color: var(--bg);
              color: var(--text);
              font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
              font-size: 15px;
              height: 100vh;
              display: flex;
              flex-direction: column;
              overflow: hidden;
            }
            header {
              background: var(--surface);
              border-bottom: 1px solid var(--card-border);
              padding: 10px 14px;
              display: flex;
              align-items: center;
              justify-content: space-between;
              z-index: 10;
              box-shadow: 0 4px 12px rgba(0,0,0,0.3);
            }
            .header-info { display: flex; flex-direction: column; min-width: 0; flex: 1; margin-right: 10px; }
            .session-title { font-size: 15px; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
            .session-meta { font-size: 12px; color: var(--text-muted); display: flex; gap: 8px; align-items: center; margin-top: 2px; }
            .badge { display: inline-block; padding: 2px 7px; border-radius: 10px; font-size: 10px; font-weight: 600; text-transform: uppercase; background: var(--card); border: 1px solid var(--card-border); }
            .badge-live { background: rgba(16, 185, 129, 0.15); color: var(--success); border-color: rgba(16, 185, 129, 0.3); }
            .header-actions { display: flex; gap: 6px; }
            .btn-icon { background: var(--card); border: 1px solid var(--card-border); color: var(--text); width: 36px; height: 36px; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 16px; cursor: pointer; }
            .btn-icon:active { background: var(--card-border); }
            #chat-container {
              flex: 1;
              overflow-y: auto;
              padding: 14px;
              display: flex;
              flex-direction: column;
              gap: 12px;
              scroll-behavior: smooth;
            }
            .msg-card {
              background: var(--card);
              border: 1px solid var(--card-border);
              border-radius: 12px;
              padding: 10px 12px;
              max-width: 90%;
              animation: fadeIn 0.2s ease-out;
            }
            @keyframes fadeIn { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: translateY(0); } }
            .msg-user { align-self: flex-end; background: #2A2346; border-color: #4C3C78; }
            .msg-ai { align-self: flex-start; background: #1C2333; border-color: #2F3E5D; }
            .msg-narrator { align-self: center; background: rgba(30, 26, 15, 0.7); border-color: rgba(251, 191, 36, 0.3); width: 96%; font-style: italic; }
            .msg-whisper { align-self: center; background: rgba(80, 20, 40, 0.5); border-color: rgba(244, 114, 182, 0.4); width: 94%; }
            .msg-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 5px; font-size: 11.5px; color: var(--text-muted); }
            .sender-name { font-weight: 700; color: var(--text); }
            .sender-role { font-size: 10px; margin-left: 4px; padding: 1px 5px; border-radius: 4px; background: rgba(255,255,255,0.08); }
            .btn-trans { background: rgba(255,255,255,0.08); border: 1px solid var(--card-border); color: var(--text-muted); padding: 1px 6px; border-radius: 4px; font-size: 10px; cursor: pointer; margin-left: auto; }
            .btn-trans:active { background: var(--primary); color: white; }
            .msg-body { font-size: 14px; line-height: 1.45; word-break: break-word; }
            .typing-bubble {
              align-self: flex-start;
              background: #1C2333;
              border: 1px dashed var(--accent-act);
              border-radius: 12px;
              padding: 10px 14px;
              color: #BAE6FD;
              font-size: 13.5px;
              display: none;
              align-items: center;
              gap: 8px;
            }
            .typing-pulse { width: 8px; height: 8px; border-radius: 50%; background: var(--accent-act); animation: pulse 1s infinite alternate; }
            @keyframes pulse { from { opacity: 0.3; transform: scale(0.8); } to { opacity: 1; transform: scale(1.2); } }
            #compose-bar {
              background: var(--surface);
              border-top: 1px solid var(--card-border);
              padding: 10px 12px 16px;
              display: flex;
              flex-direction: column;
              gap: 8px;
            }
            .action-chips { display: flex; gap: 6px; overflow-x: auto; padding-bottom: 2px; scrollbar-width: none; }
            .action-chips::-webkit-scrollbar { display: none; }
            .chip {
              padding: 5px 11px;
              border-radius: 16px;
              font-size: 12px;
              font-weight: 600;
              background: var(--card);
              border: 1px solid var(--card-border);
              color: var(--text-muted);
              cursor: pointer;
              white-space: nowrap;
              transition: all 0.15s;
            }
            .chip.active { background: var(--primary); color: white; border-color: var(--primary); box-shadow: 0 0 10px var(--primary-glow); }
            .input-row { display: flex; gap: 8px; align-items: flex-end; }
            .text-box {
              flex: 1;
              background: var(--card);
              border: 1px solid var(--card-border);
              border-radius: 20px;
              padding: 9px 14px;
              color: var(--text);
              font-size: 14px;
              font-family: inherit;
              resize: none;
              max-height: 100px;
              outline: none;
            }
            .text-box:focus { border-color: var(--primary); }
            .btn-send {
              background: var(--primary);
              border: none;
              color: white;
              width: 42px;
              height: 42px;
              border-radius: 50%;
              display: flex;
              align-items: center;
              justify-content: center;
              font-size: 18px;
              cursor: pointer;
              flex-shrink: 0;
              box-shadow: 0 3px 10px var(--primary-glow);
            }
            .btn-send:active { transform: scale(0.95); }
            .modal-overlay {
              position: fixed;
              top: 0; left: 0; right: 0; bottom: 0;
              background: rgba(0,0,0,0.7);
              display: none;
              align-items: flex-end;
              justify-content: center;
              z-index: 100;
              backdrop-filter: blur(4px);
            }
            .modal-content {
              background: var(--surface);
              border-top: 1px solid var(--card-border);
              border-radius: 20px 20px 0 0;
              width: 100%;
              max-height: 85vh;
              overflow-y: auto;
              padding: 20px 16px;
              animation: slideUp 0.25s ease-out;
            }
            @keyframes slideUp { from { transform: translateY(100%); } to { transform: translateY(0); } }
            .modal-title { font-size: 17px; font-weight: 700; margin-bottom: 14px; display: flex; justify-content: space-between; align-items: center; }
            .form-group { margin-bottom: 12px; }
            .form-label { font-size: 12px; color: var(--text-muted); margin-bottom: 4px; display: block; font-weight: 600; }
            .form-control {
              width: 100%;
              background: var(--card);
              border: 1px solid var(--card-border);
              border-radius: 10px;
              padding: 8px 12px;
              color: white;
              font-size: 14px;
              font-family: inherit;
              outline: none;
            }
            .form-control:focus { border-color: var(--primary); }
            .btn-primary {
              width: 100%;
              background: var(--primary);
              color: white;
              border: none;
              padding: 12px;
              border-radius: 12px;
              font-weight: 700;
              font-size: 15px;
              cursor: pointer;
              margin-top: 8px;
            }
            .dice-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 10px; }
            .btn-dice { background: var(--card); border: 1px solid var(--card-border); color: white; padding: 12px; border-radius: 12px; font-weight: 700; cursor: pointer; text-align: center; }
            .btn-dice:active { background: var(--card-border); }
          </style>
        </head>
        <body>
          <header>
            <div class="header-info">
              <div class="session-title" id="hdr-title">Connecting to Aetheria...</div>
              <div class="session-meta">
                <span class="badge badge-live" id="hdr-status">Online</span>
                <span id="hdr-location">Tokyo HQ</span>
                <span id="hdr-turn">Turn 1</span>
              </div>
            </div>
            <div class="header-actions">
              <button class="btn-icon" id="btn-ready" title="Ready for Round (Auto-Resolves when both ready)" onclick="toggleMobileReady()">✓</button>
              <button class="btn-icon" id="btn-autotrans" title="Auto-Translate into Turkish" onclick="toggleAutoTrans()">🌐</button>
              <button class="btn-icon" title="Accessibility, Text Size &amp; Zoom" onclick="openModal('modal-a11y')">👁️</button>
              <button class="btn-icon" title="Dice Roller" onclick="openModal('modal-dice')">🎲</button>
              <button class="btn-icon" title="Characters &amp; Identity" onclick="openModal('modal-char')">👤</button>
              <button class="btn-icon" title="World Lore" onclick="openModal('modal-lore')">📖</button>
              <button class="btn-icon" title="Connection Settings" onclick="openModal('modal-conn')">⚙️</button>
            </div>
          </header>

          <div id="chat-container">
            <div class="msg-card msg-narrator">
              <div class="msg-header"><span class="sender-name">Aetheria System</span></div>
              <div class="msg-body">Welcome to Aetheria Mobile Companion. Connected to PC host session! Choose an action mode below (Speak, Act, Think, Narrate, OOC) to roleplay with your phone.</div>
            </div>
          </div>

          <div class="typing-bubble" id="typing-bubble">
            <div class="typing-pulse"></div>
            <span id="typing-text">AI is writing response...</span>
          </div>

          <div id="compose-bar">
            <div class="action-chips">
              <div class="chip active" data-action="Speak" onclick="setAction('Speak')">💬 Speak</div>
              <div class="chip" data-action="Act" onclick="setAction('Act')">⚔️ Act</div>
              <div class="chip" data-action="Think" onclick="setAction('Think')">💭 Think</div>
              <div class="chip" data-action="Narrate" onclick="setAction('Narrate')">📜 Narrate</div>
              <div class="chip" data-action="OutOfCharacter" onclick="setAction('OutOfCharacter')">⚙️ OOC</div>
            </div>
            <div class="input-row">
              <textarea id="msg-input" class="text-box" rows="1" placeholder="Say something as your character..." oninput="autoGrow(this)"></textarea>
              <button class="btn-icon" style="width:auto; padding:0 10px; border-radius:18px; font-size:11px; font-weight:600;" title="Translate input to Turkish" onclick="translateInputMobile()">🌐 TR</button>
              <button class="btn-send" onclick="submitAction()">➤</button>
            </div>
          </div>

          <!-- Accessibility & Zoom Modal -->
          <div class="modal-overlay" id="modal-a11y">
            <div class="modal-content">
              <div class="modal-title"><span>Accessibility &amp; Zoom</span><button class="btn-icon" onclick="closeModal('modal-a11y')">✕</button></div>
              <div class="form-group">
                <label class="form-label">Message Text Size</label>
                <div style="display:flex; align-items:center; gap:10px;">
                  <button class="btn-dice" style="flex:1; padding:8px;" onclick="changeMobileFontSize(-1)">A- Smaller</button>
                  <span id="a11y-size-lbl" style="font-weight:700; font-size:14px; min-width:48px; text-align:center;">14px</span>
                  <button class="btn-dice" style="flex:1; padding:8px;" onclick="changeMobileFontSize(1)">A+ Larger</button>
                </div>
              </div>
              <div class="form-group" style="display:flex; justify-content:space-between; align-items:center; margin-top:14px;">
                <div>
                  <div style="font-size:13px; font-weight:600;">High Contrast Theme</div>
                  <div style="font-size:11px; color:var(--text-muted);">Crisp borders &amp; pure black backdrop</div>
                </div>
                <input type="checkbox" id="a11y-chk-contrast" style="width:20px; height:20px;" onchange="toggleMobileContrast(this.checked)">
              </div>
              <div class="form-group" style="display:flex; justify-content:space-between; align-items:center; margin-top:14px;">
                <div>
                  <div style="font-size:13px; font-weight:600;">Dyslexia-Friendly Font</div>
                  <div style="font-size:11px; color:var(--text-muted);">High-legibility typeface</div>
                </div>
                <input type="checkbox" id="a11y-chk-dyslexic" style="width:20px; height:20px;" onchange="toggleMobileDyslexic(this.checked)">
              </div>
              <div class="form-group" style="display:flex; justify-content:space-between; align-items:center; margin-top:14px;">
                <div>
                  <div style="font-size:13px; font-weight:600;">Expanded Line Spacing</div>
                  <div style="font-size:11px; color:var(--text-muted);">Relaxed reading line height (1.85x)</div>
                </div>
                <input type="checkbox" id="a11y-chk-spacing" style="width:20px; height:20px;" onchange="toggleMobileSpacing(this.checked)">
              </div>
              <button class="btn-primary" style="margin-top:12px;" onclick="closeModal('modal-a11y')">Done</button>
            </div>
          </div>

          <!-- Connection Modal -->
          <div class="modal-overlay" id="modal-conn">
            <div class="modal-content">
              <div class="modal-title"><span>Connection Settings</span><button class="btn-icon" onclick="closeModal('modal-conn')">✕</button></div>
              <div class="form-group">
                <label class="form-label">Host PC IP / Address</label>
                <input class="form-control" id="cfg-host" value="127.0.0.1">
              </div>
              <div class="form-group">
                <label class="form-label">Invite Token</label>
                <input class="form-control" id="cfg-token" value="{{inviteToken}}">
              </div>
              <div class="form-group">
                <label class="form-label">Your Player / Persona Name</label>
                <input class="form-control" id="cfg-player" value="Mobile Traveler">
              </div>
              <button class="btn-primary" onclick="saveConfigAndReconnect()">Save & Reconnect</button>
            </div>
          </div>

          <!-- Characters & Identity Modal -->
          <div class="modal-overlay" id="modal-char">
            <div class="modal-content">
              <div class="modal-title"><span>Character & Persona</span><button class="btn-icon" onclick="closeModal('modal-char')">✕</button></div>
              <div class="form-group">
                <label class="form-label">Active Character</label>
                <select class="form-control" id="char-select" onchange="onCharacterSelected()"></select>
              </div>
              <hr style="border:0; border-top:1px solid var(--card-border); margin:14px 0;">
              <div class="modal-title" style="font-size:15px;"><span>Create & Sync Custom Character</span></div>
              <div class="form-group">
                <label class="form-label">Character Name</label>
                <input class="form-control" id="sync-name" placeholder="e.g. Dennis / Kobeni / Kira Hunter">
              </div>
              <div class="form-group">
                <label class="form-label">Title / Role</label>
                <input class="form-control" id="sync-title" placeholder="e.g. Devil Hunter Rookie">
              </div>
              <div class="form-group">
                <label class="form-label">Appearance & Personality</label>
                <textarea class="form-control" id="sync-desc" rows="3" placeholder="Describe your character's look and quirks..."></textarea>
              </div>
              <button class="btn-primary" onclick="syncCustomCharacter()">⚡ Sync Character to PC Host</button>
            </div>
          </div>

          <!-- World Lore Modal -->
          <div class="modal-overlay" id="modal-lore">
            <div class="modal-content">
              <div class="modal-title"><span>World Lore & Canon</span><button class="btn-icon" onclick="closeModal('modal-lore')">✕</button></div>
              <div id="lore-content" style="font-size:13.5px; line-height:1.5; color:var(--text-muted);">
                Loading world lore from host...
              </div>
            </div>
          </div>

          <!-- Dice Modal -->
          <div class="modal-overlay" id="modal-dice">
            <div class="modal-content">
              <div class="modal-title"><span>Dice Roller</span><button class="btn-icon" onclick="closeModal('modal-dice')">✕</button></div>
              <div class="dice-grid">
                <button class="btn-dice" onclick="rollDice(20)">Roll D20 🎲</button>
                <button class="btn-dice" onclick="rollDice(100)">Roll D100 🎯</button>
              </div>
            </div>
          </div>

          <script>
            let currentAction = 'Speak';
            let activeSession = null;
            let sseSource = null;
            let participantId = localStorage.getItem('aetheria_pId') || ('p_' + Math.random().toString(36).substring(2, 9));
            localStorage.setItem('aetheria_pId', participantId);

            function setAction(type) {
              currentAction = type;
              document.querySelectorAll('.chip').forEach(c => c.classList.toggle('active', c.dataset.action === type));
              const input = document.getElementById('msg-input');
              if (type === 'Speak') input.placeholder = 'Say something as your character...';
              else if (type === 'Act') input.placeholder = 'Describe an action (e.g. *draws chainsaw cord*)...';
              else if (type === 'Think') input.placeholder = 'Inner thoughts (hidden or reflective)...';
              else if (type === 'Narrate') input.placeholder = 'World narration / scene event...';
              else input.placeholder = 'Out of character message to other players...';
            }

            function autoGrow(el) {
              el.style.height = 'auto';
              el.style.height = (el.scrollHeight) + 'px';
            }

            function openModal(id) { document.getElementById(id).style.display = 'flex'; }
            function closeModal(id) { document.getElementById(id).style.display = 'none'; }

            async function fetchSession() {
              try {
                const res = await fetch('/api/session');
                if (!res.ok) return;
                activeSession = await res.json();
                renderSession();
              } catch (e) {
                console.error(e);
              }
            }

            function renderSession() {
              if (!activeSession) return;
              document.getElementById('hdr-title').textContent = activeSession.Title || 'Aetheria Roleplay';
              document.getElementById('hdr-location').textContent = activeSession.CurrentLocation || 'Tokyo';
              document.getElementById('hdr-turn').textContent = 'Turn ' + (activeSession.TurnNumber || 1);

              const select = document.getElementById('char-select');
              select.innerHTML = '';
              if (activeSession.Characters) {
                activeSession.Characters.forEach(c => {
                  const opt = document.createElement('option');
                  opt.value = c.Id;
                  opt.textContent = c.Name + ' (' + c.Title + ')';
                  select.appendChild(opt);
                });
              }

              // Render Lore
              if (activeSession.World) {
                const w = activeSession.World;
                let loreHtml = '<b>' + w.Name + '</b><br><p style="margin:6px 0 12px;">' + w.Description + '</p>';
                if (w.Locations && w.Locations.length > 0) {
                  loreHtml += '<b style="color:var(--text)">Locations:</b><ul style="margin:4px 0 12px 18px;">';
                  w.Locations.forEach(loc => loreHtml += '<li><b>' + loc.Name + '</b>: ' + loc.Description + '</li>');
                  loreHtml += '</ul>';
                }
                if (w.Factions && w.Factions.length > 0) {
                  loreHtml += '<b style="color:var(--text)">Factions:</b><ul style="margin:4px 0 12px 18px;">';
                  w.Factions.forEach(f => loreHtml += '<li><b>' + f.Name + '</b>: ' + f.Description + '</li>');
                  loreHtml += '</ul>';
                }
                document.getElementById('lore-content').innerHTML = loreHtml;
              }

              // Render Messages
              const container = document.getElementById('chat-container');
              container.innerHTML = '';
              if (activeSession.Messages) {
                activeSession.Messages.forEach(m => appendMessage(m));
              }
            }

            let autoTranslateTr = false;
            let isMobileReady = false;

            function toggleAutoTrans() {
              autoTranslateTr = !autoTranslateTr;
              const btn = document.getElementById('btn-autotrans');
              if (autoTranslateTr) {
                btn.classList.add('badge-live');
                btn.style.color = 'var(--success)';
              } else {
                btn.classList.remove('badge-live');
                btn.style.color = '';
              }
              document.querySelectorAll('.msg-card').forEach(card => {
                const btn = card.querySelector('.btn-trans');
                if (btn && autoTranslateTr) translateMsg(btn);
              });
            }

            async function toggleMobileReady() {
              isMobileReady = !isMobileReady;
              const btn = document.getElementById('btn-ready');
              if (isMobileReady) {
                btn.style.background = 'var(--success)';
                btn.style.color = 'white';
              } else {
                btn.style.background = '';
                btn.style.color = '';
              }
              const token = document.getElementById('cfg-token').value.trim();
              try {
                await fetch('/api/lobby/ready', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({
                    inviteToken: token,
                    participantId: participantId,
                    isReady: isMobileReady
                  })
                });
              } catch(e) {}
            }

            async function translateInputMobile() {
              const input = document.getElementById('msg-input');
              const text = input.value.trim();
              if (!text) return;
              try {
                const res = await fetch('/api/translate?text=' + encodeURIComponent(text) + '&target=tr');
                if (res.ok) {
                  const data = await res.json();
                  input.value = data.translated;
                }
              } catch(e) {}
            }

            async function translateMsg(btn) {
              const card = btn.closest('.msg-card');
              const body = card.querySelector('.msg-body');
              const transBox = card.querySelector('.msg-trans');
              if (!body || !transBox) return;

              if (transBox.style.display !== 'none' && !autoTranslateTr) {
                transBox.style.display = 'none';
                return;
              }

              btn.textContent = '...';
              try {
                const res = await fetch('/api/translate?text=' + encodeURIComponent(body.textContent) + '&target=tr');
                if (res.ok) {
                  const data = await res.json();
                  transBox.textContent = '🇹🇷 ' + data.translated;
                  transBox.style.display = 'block';
                  btn.textContent = '✓ TR';
                } else {
                  btn.textContent = '🌐 TR';
                }
              } catch(e) {
                btn.textContent = '🌐 TR';
              }
            }

            function appendMessage(msg) {
              const container = document.getElementById('chat-container');
              const div = document.createElement('div');
              let cls = 'msg-card ';
              if (msg.Role === 'narrator') cls += 'msg-narrator';
              else if (msg.IsPrivateWhisper) cls += 'msg-whisper';
              else if (msg.SenderId === participantId || msg.SenderName === document.getElementById('cfg-player').value) cls += 'msg-user';
              else cls += 'msg-ai';
              div.className = cls;

              const roleLabel = msg.Role === 'narrator' ? 'Narrator' : (msg.ActionType || 'Speech');
              div.innerHTML = '<div class="msg-header">' +
                '<span class="sender-name">' + escapeHtml(msg.SenderName) + '</span>' +
                '<span class="sender-role">' + roleLabel + '</span>' +
                '<button class="btn-trans" onclick="translateMsg(this)">🌐 TR</button>' +
                '</div>' +
                '<div class="msg-body">' + escapeHtml(msg.Content) + '</div>' +
                '<div class="msg-trans" style="display:none; margin-top:6px; padding:6px 8px; border-radius:6px; background:rgba(255,255,255,0.06); font-size:13px; font-style:italic; color:#FCD34D;"></div>';

              const sz = parseInt(localStorage.getItem('a11y_size') || '14', 10);
              const spacing = localStorage.getItem('a11y_spacing') === 'true';
              const bodyEl = div.querySelector('.msg-body');
              if (bodyEl) {
                bodyEl.style.fontSize = sz + 'px';
                bodyEl.style.lineHeight = spacing ? '1.85' : '1.45';
              }

              container.appendChild(div);
              container.scrollTop = container.scrollHeight;

              if (autoTranslateTr) {
                const transBtn = div.querySelector('.btn-trans');
                if (transBtn) translateMsg(transBtn);
              }
            }

            function escapeHtml(text) {
              if (!text) return '';
              return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
            }

            async function submitAction() {
              const input = document.getElementById('msg-input');
              const text = input.value.trim();
              if (!text) return;
              input.value = '';
              input.style.height = 'auto';

              const token = document.getElementById('cfg-token').value.trim();
              const player = document.getElementById('cfg-player').value.trim();

              try {
                if (navigator.vibrate) navigator.vibrate(15);
                const res = await fetch('/api/action', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({
                    inviteToken: token,
                    content: text,
                    actionType: currentAction,
                    playerName: player,
                    participantId: participantId
                  })
                });
                if (!res.ok) {
                  const err = await res.json();
                  alert('Action failed: ' + (err.error || 'Server error'));
                }
              } catch (e) {
                alert('Connection error: ' + e.message);
              }
            }

            async function syncCustomCharacter() {
              const name = document.getElementById('sync-name').value.trim();
              const title = document.getElementById('sync-title').value.trim();
              const desc = document.getElementById('sync-desc').value.trim();
              if (!name) { alert('Enter character name'); return; }

              const token = document.getElementById('cfg-token').value.trim();
              const charObj = {
                Id: 'custom-mobile-' + Math.random().toString(36).substring(2, 9),
                Name: name,
                Title: title || 'Mobile Player Character',
                Description: desc,
                Personality: desc,
                Appearance: desc,
                Background: 'Created via Aetheria Mobile Companion on phone.'
              };

              try {
                const res = await fetch('/api/sync-character', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({
                    inviteToken: token,
                    character: charObj
                  })
                });
                if (res.ok) {
                  alert("Character '" + name + "' synced to PC Host!");
                  closeModal('modal-char');
                  fetchSession();
                } else {
                  alert('Sync failed');
                }
              } catch (e) {
                alert('Error: ' + e.message);
              }
            }

            async function onCharacterSelected() {
              const select = document.getElementById('char-select');
              const charId = select.value;
              const charName = select.options[select.selectedIndex]?.text?.split(' (')[0] || '';
              if (!charName) return;

              document.getElementById('cfg-player').value = charName;
              localStorage.setItem('aetheria_player', charName);

              const token = document.getElementById('cfg-token').value.trim();
              try {
                await fetch('/api/lobby/identity', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({
                    inviteToken: token,
                    participantId: participantId,
                    personaName: charName,
                    displayName: charName,
                    characterId: charId,
                    isReady: true
                  })
                });
              } catch (e) { console.error(e); }
            }

            function rollDice(sides) {
              const roll = Math.floor(Math.random() * sides) + 1;
              const text = '*Rolls D' + sides + ': [' + roll + ']*';
              closeModal('modal-dice');
              document.getElementById('msg-input').value = text;
              setAction('Act');
              submitAction();
            }

            function setupEventStream() {
              if (sseSource) sseSource.close();
              sseSource = new EventSource('/api/stream');
              sseSource.addEventListener('message', e => {
                const msg = JSON.parse(e.data);
                appendMessage(msg);
                document.getElementById('typing-bubble').style.display = 'none';
              });
              sseSource.addEventListener('ai_delta', e => {
                const delta = JSON.parse(e.data);
                const bubble = document.getElementById('typing-bubble');
                bubble.style.display = 'flex';
                document.getElementById('typing-text').textContent = delta.delta;
                const container = document.getElementById('chat-container');
                container.scrollTop = container.scrollHeight;
              });
              sseSource.addEventListener('session_state', e => {
                fetchSession();
              });
            }

            function saveConfigAndReconnect() {
              localStorage.setItem('aetheria_token', document.getElementById('cfg-token').value);
              localStorage.setItem('aetheria_player', document.getElementById('cfg-player').value);
              closeModal('modal-conn');
              setupEventStream();
              fetchSession();
            }

            function changeMobileFontSize(delta) {
              let sz = parseInt(localStorage.getItem('a11y_size') || '14', 10);
              sz = Math.max(11, Math.min(26, sz + delta));
              localStorage.setItem('a11y_size', sz);
              applyMobileA11y();
            }

            function toggleMobileContrast(val) {
              localStorage.setItem('a11y_contrast', val ? 'true' : 'false');
              applyMobileA11y();
            }

            function toggleMobileDyslexic(val) {
              localStorage.setItem('a11y_dyslexic', val ? 'true' : 'false');
              applyMobileA11y();
            }

            function toggleMobileSpacing(val) {
              localStorage.setItem('a11y_spacing', val ? 'true' : 'false');
              applyMobileA11y();
            }

            function applyMobileA11y() {
              const sz = parseInt(localStorage.getItem('a11y_size') || '14', 10);
              const lbl = document.getElementById('a11y-size-lbl');
              if (lbl) lbl.textContent = sz + 'px';

              const contrast = localStorage.getItem('a11y_contrast') === 'true';
              const dyslexic = localStorage.getItem('a11y_dyslexic') === 'true';
              const spacing = localStorage.getItem('a11y_spacing') === 'true';

              const chkC = document.getElementById('a11y-chk-contrast');
              if (chkC) chkC.checked = contrast;
              const chkD = document.getElementById('a11y-chk-dyslexic');
              if (chkD) chkD.checked = dyslexic;
              const chkS = document.getElementById('a11y-chk-spacing');
              if (chkS) chkS.checked = spacing;

              document.querySelectorAll('.msg-body').forEach(b => {
                b.style.fontSize = sz + 'px';
                b.style.lineHeight = spacing ? '1.85' : '1.45';
              });

              if (dyslexic) {
                document.body.style.fontFamily = "'Comic Sans MS', sans-serif";
              } else {
                document.body.style.fontFamily = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif";
              }

              if (contrast) {
                document.documentElement.style.setProperty('--bg', '#000000');
                document.documentElement.style.setProperty('--card', '#0a0a0a');
                document.documentElement.style.setProperty('--card-border', '#38bdf8');
              } else {
                document.documentElement.style.setProperty('--bg', '#0E0D14');
                document.documentElement.style.setProperty('--card', '#171622');
                document.documentElement.style.setProperty('--card-border', '#282637');
              }
            }

            window.addEventListener('DOMContentLoaded', () => {
              const savedToken = localStorage.getItem('aetheria_token');
              if (savedToken) document.getElementById('cfg-token').value = savedToken;
              const savedPlayer = localStorage.getItem('aetheria_player');
              if (savedPlayer) document.getElementById('cfg-player').value = savedPlayer;

              applyMobileA11y();
              fetchSession();
              setupEventStream();
            });
          </script>
        </body>
        </html>
        """;
    }
}
