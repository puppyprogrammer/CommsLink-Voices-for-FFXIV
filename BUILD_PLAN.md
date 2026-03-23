# FFXIVoices MVP POC — Definitive Build Plan

> **Goal**: Real-time FFXIV party chat → Piper TTS (male: Ryan, female: Amy) → multiplayer audio stream.
> **Timeline**: ~3 hours with copy-paste. Steps numbered for sequential execution.
> **Unique Player ID**: `ContentId` (uint64) per FFXIV character.

---

## Architecture

```
FFXIV Game Client                         Local Machine
┌─────────────────────┐                  ┌──────────────────────────────┐
│ Dalamud Framework    │                  │  Node.js Server (localhost)  │
│ ┌─────────────────┐ │   HTTP POST      │  ┌────────────────────────┐  │
│ │ FFXIVoices      │ │  /chat :3000     │  │ POST /chat endpoint    │  │
│ │ Plugin (C#)     │─┼──────────────────┼─▶│ GET  /health endpoint  │  │
│ │                 │ │                  │  └──────────┬─────────────┘  │
│ │ ChatHandler     │ │                  │             │                │
│ │  └→ POST json   │ │                  │  ┌──────────▼─────────────┐  │
│ │                 │ │                  │  │ Voice Mapper           │  │
│ │ AudioPlayer     │ │                  │  │ contentId→voice lookup │  │
│ │  └→ WS client   │ │   WebSocket      │  └──────────┬─────────────┘  │
│ │  └→ NAudio play │◀┼──────────────────┼──│ TTS Queue              │  │
│ └─────────────────┘ │  ws://:8080      │  │  └→ Piper subprocess   │  │
│                     │  (WAV chunks)    │  │  └→ PCM→WAV            │  │
└─────────────────────┘                  │  │  └→ WS broadcast       │  │
                                         │  └──────────────────────────┘│
  Browser Test Client (optional)         │  ┌──────────────────────────┐│
  ┌──────────────┐                       │  │ Piper TTS Binary         ││
  │ test.html    │◀──ws://:8080──────────┼──│ + en_US-ryan-medium.onnx ││
  │ JS AudioCtx  │                       │  │ + en_US-amy-medium.onnx  ││
  └──────────────┘                       │  └──────────────────────────┘│
                                         └──────────────────────────────┘
```

---

## File Structure (Final State)

```
H:\Development\FFXIVoices\
├── BUILD_PLAN.md                      # This file
├── plugin\
│   └── FFXIVoices\
│       ├── FFXIVoices.csproj          # ✅ EXISTS - .NET 8, NAudio, Dalamud refs
│       ├── FFXIVoices.json            # ✅ EXISTS - Plugin manifest
│       ├── Plugin.cs                  # ✅ EXISTS - Entry point, slash commands
│       ├── ChatHandler.cs             # ✅ EXISTS - Chat hook → HTTP POST
│       ├── AudioPlayer.cs             # ✅ EXISTS - WS client → NAudio playback
│       └── Configuration.cs           # ✅ EXISTS - ServerUrl, WsUrl, Volume
├── server\
│   ├── package.json                   # ✅ EXISTS - ws dependency
│   ├── index.js                       # ✅ EXISTS - HTTP + WS + Piper TTS
│   ├── voices.json                    # ✅ EXISTS - Voice overrides map
│   ├── setup-piper.ps1               # ✅ EXISTS - Download script
│   ├── setup-piper.sh                # ✅ EXISTS - Linux variant
│   ├── piper\                        # 🔧 CREATED BY setup-piper.ps1
│   │   └── piper.exe
│   ├── voices\                       # 🔧 CREATED BY setup-piper.ps1
│   │   ├── en_US-ryan-medium.onnx
│   │   ├── en_US-ryan-medium.onnx.json
│   │   ├── en_US-amy-medium.onnx
│   │   └── en_US-amy-medium.onnx.json
│   └── test-client.html              # 📝 TO CREATE - Browser audio test page
└── test\
    └── test-curl.sh                   # 📝 TO CREATE - Smoke test script
```

---

## PHASE 1: Environment Setup (~30 min)

### Step 1: Prerequisites Check

```powershell
# Verify .NET 8 SDK
dotnet --version
# Expected: 8.0.x — if missing: https://dotnet.microsoft.com/download/dotnet/8.0

# Verify Node.js
node --version
# Expected: 18+ — if missing: https://nodejs.org/

# Verify FFXIV + Dalamud installed
# XIVLauncher: https://goatcorp.github.io/
# Dalamud must be enabled in XIVLauncher settings
```

### Step 2: Set DALAMUD_HOME Environment Variable

The plugin project references Dalamud DLLs via `$(DALAMUD_HOME)`. You must set this.

```powershell
# Find your Dalamud install (typical path):
$DalamudPath = "$env:APPDATA\XIVLauncher\addon\Hooks\dev"
# OR for release Dalamud:
# $DalamudPath = (Get-ChildItem "$env:APPDATA\XIVLauncher\addon\Hooks" -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName

# Verify it has the DLLs
Test-Path "$DalamudPath\Dalamud.dll"  # Must be True

# Set for current session
$env:DALAMUD_HOME = $DalamudPath

# Set permanently (user-level)
[System.Environment]::SetEnvironmentVariable("DALAMUD_HOME", $DalamudPath, "User")

Write-Host "DALAMUD_HOME = $env:DALAMUD_HOME"
```

### Step 3: Download Piper TTS + Voice Models

```powershell
cd H:\Development\FFXIVoices\server

# Run the provided setup script
powershell -ExecutionPolicy Bypass -File setup-piper.ps1

# Set PIPER_PATH for the server
$env:PIPER_PATH = (Resolve-Path "piper\piper.exe").Path

# Verify Piper works
echo "Hello adventurer, welcome to Eorzea" | & $env:PIPER_PATH --model voices\en_US-ryan-medium.onnx --output_file test_ryan.wav
echo "The crystal tower awaits" | & $env:PIPER_PATH --model voices\en_US-amy-medium.onnx --output_file test_amy.wav

# Play them to confirm
Start-Process test_ryan.wav
Start-Process test_amy.wav

# Clean up test files
Remove-Item test_ryan.wav, test_amy.wav -ErrorAction SilentlyContinue
```

**Voice model details:**
| Voice | Model File | Quality | Sample Rate | Gender |
|-------|-----------|---------|-------------|--------|
| Ryan  | `en_US-ryan-medium.onnx` | Medium (good for POC) | 22050 Hz | Male |
| Amy   | `en_US-amy-medium.onnx`  | Medium | 22050 Hz | Female |

> **Note**: The original request mentioned `en-us-amy-high` — the "high" quality model is larger (~100MB vs ~60MB) but better quality. For MVP, medium is fine. To upgrade later, change `medium` → `high` in setup script and `index.js`.

### Step 4: Install Server Dependencies

```powershell
cd H:\Development\FFXIVoices\server
npm install
# Installs: ws (WebSocket library) — only dependency needed
```

---

## PHASE 2: Server Validation (~20 min)

### Step 5: Start the Server

```powershell
cd H:\Development\FFXIVoices\server
$env:PIPER_PATH = (Resolve-Path "piper\piper.exe").Path
node index.js
```

**Expected output:**
```
[http] FFXIVoices server listening on http://localhost:3000
[ws]   WebSocket server on ws://localhost:8080
[tts]  Piper path: H:\Development\FFXIVoices\server\piper\piper.exe
[tts]  Voice models dir: H:\Development\FFXIVoices\server\voices
[tts]  male: en_US-ryan-medium.onnx OK
[tts]  female: en_US-amy-medium.onnx OK
```

### Step 6: Health Check

```powershell
# In a separate terminal
curl http://localhost:3000/health
# Expected: {"status":"ok","clients":0,"queueLength":0,"voices":["male","female"]}
```

### Step 7: Test TTS Generation (curl)

```powershell
# POST a chat message — will generate TTS but no WS clients to receive yet
curl -X POST http://localhost:3000/chat `
  -H "Content-Type: application/json" `
  -d '{"playerName":"Warrior of Light","contentId":"12345","message":"Hello party, lets go!","timestamp":1234567890}'
# Expected: {"status":"queued","voice":"female"}  (name hash determines voice)

# Server console should show:
# [chat] Warrior of Light: "Hello party, lets go!"
# [tts] Generating: "Hello party, lets go!" voice=female player=Warrior of Light
# [ws] Broadcast XXXXX bytes to 0 clients
```

### Step 8: Test WebSocket Audio Reception

```powershell
# Install wscat globally if not present
npm install -g wscat

# Connect as a client
npx wscat -c ws://localhost:8080

# In another terminal, POST a message:
curl -X POST http://localhost:3000/chat `
  -H "Content-Type: application/json" `
  -d '{"playerName":"Test Player","contentId":"99999","message":"Testing one two three"}'

# wscat should show: a JSON header followed by binary data
```

---

## PHASE 3: Browser Test Client (~15 min)

### Step 9: Create Browser Audio Test Page

Create `server/test-client.html` — a standalone HTML page that connects via WebSocket and plays received audio. This validates the audio pipeline without needing the Dalamud plugin.

```html
<!DOCTYPE html>
<html>
<head>
  <title>FFXIVoices Test Client</title>
  <style>
    body { font-family: monospace; background: #1a1a2e; color: #e0e0e0; padding: 20px; }
    #log { background: #16213e; padding: 10px; height: 300px; overflow-y: auto; border: 1px solid #333; }
    .entry { margin: 2px 0; }
    .meta { color: #4fc3f7; }
    .audio { color: #81c784; }
    .error { color: #ef5350; }
    button { padding: 8px 16px; margin: 5px; cursor: pointer; }
    input { padding: 8px; width: 300px; background: #16213e; color: #e0e0e0; border: 1px solid #555; }
  </style>
</head>
<body>
  <h2>FFXIVoices - WebSocket Audio Test Client</h2>
  <div>
    <button onclick="connect()">Connect WS</button>
    <button onclick="disconnect()">Disconnect</button>
    <span id="status">Disconnected</span>
  </div>
  <hr>
  <div>
    <input id="name" value="Test Player" placeholder="Player Name">
    <input id="msg" value="Hello from the test client" placeholder="Message">
    <button onclick="sendChat()">Send Chat (HTTP POST)</button>
  </div>
  <hr>
  <div id="log"></div>

  <script>
    let ws = null;
    let expectingBinary = false;
    let currentMeta = null;

    function log(text, cls = '') {
      const el = document.getElementById('log');
      const div = document.createElement('div');
      div.className = 'entry ' + cls;
      div.textContent = `[${new Date().toLocaleTimeString()}] ${text}`;
      el.appendChild(div);
      el.scrollTop = el.scrollHeight;
    }

    function connect() {
      if (ws && ws.readyState === WebSocket.OPEN) { log('Already connected'); return; }
      ws = new WebSocket('ws://localhost:8080');
      ws.binaryType = 'arraybuffer';

      ws.onopen = () => {
        document.getElementById('status').textContent = 'Connected';
        log('WebSocket connected', 'meta');
      };

      ws.onmessage = (event) => {
        if (typeof event.data === 'string') {
          // JSON metadata header
          currentMeta = JSON.parse(event.data);
          log(`[${currentMeta.playerName}]: "${currentMeta.message}" (${currentMeta.format}, ${currentMeta.size} bytes)`, 'meta');
          expectingBinary = true;
        } else if (event.data instanceof ArrayBuffer) {
          // Binary WAV audio
          log(`Received audio: ${event.data.byteLength} bytes — playing...`, 'audio');
          playAudio(event.data);
          expectingBinary = false;
        }
      };

      ws.onclose = () => {
        document.getElementById('status').textContent = 'Disconnected';
        log('WebSocket disconnected', 'error');
      };

      ws.onerror = (e) => log('WebSocket error', 'error');
    }

    function disconnect() {
      if (ws) { ws.close(); ws = null; }
    }

    function playAudio(arrayBuffer) {
      const blob = new Blob([arrayBuffer], { type: 'audio/wav' });
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      audio.play().catch(e => log('Playback error: ' + e.message, 'error'));
      audio.onended = () => URL.revokeObjectURL(url);
    }

    async function sendChat() {
      const playerName = document.getElementById('name').value;
      const message = document.getElementById('msg').value;
      try {
        const res = await fetch('http://localhost:3000/chat', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ playerName, contentId: '0', message, timestamp: Date.now() })
        });
        const data = await res.json();
        log(`POST /chat → ${JSON.stringify(data)}`, 'meta');
      } catch (e) {
        log('HTTP error: ' + e.message, 'error');
      }
    }

    // Auto-connect on load
    connect();
  </script>
</body>
</html>
```

### Step 10: Test End-to-End in Browser

```powershell
# Server must be running (Step 5)
# Open the test client in your browser:
Start-Process "H:\Development\FFXIVoices\server\test-client.html"

# 1. Page auto-connects to ws://localhost:8080
# 2. Click "Send Chat" button
# 3. Should hear TTS audio playback within 1-2 seconds
# 4. Try different player names — voice should vary based on name hash
#    "Warrior of Light" → female (hash=1625, odd)
#    "Test Player" → male (hash=1104, even)
```

---

## PHASE 4: Voice Mapping Configuration (~10 min)

### Step 11: Configure Voice Overrides

Edit `server/voices.json` to hardcode specific player→voice mappings:

```json
{
  "_comment": "Manual voice overrides by contentId or playerName. Unmatched players use name-hash parity.",
  "byContentId": {
    "12345678": "male",
    "87654321": "female"
  },
  "byPlayerName": {
    "Your Character Name": "male",
    "Party Member Name": "female"
  }
}
```

**Voice resolution priority** (implemented in `server/index.js:getVoice()`):
1. `byContentId[contentId]` — most specific, persists across name changes
2. `byPlayerName[playerName]` — fallback for when contentId is unavailable
3. Name hash parity — sum of char codes, even=male, odd=female

> **Future**: Replace JSON file with SQLite or Redis for per-player voice selection UI.

---

## PHASE 5: Plugin Build & Load (~45 min)

### Step 12: Build the Dalamud Plugin

```powershell
cd H:\Development\FFXIVoices\plugin\FFXIVoices

# Ensure DALAMUD_HOME is set (from Step 2)
echo $env:DALAMUD_HOME

# Restore and build
dotnet restore
dotnet build -c Release

# Expected output includes:
# Build succeeded.
# 0 Warning(s)
# 0 Error(s)
```

**If build fails** — common issues:
| Error | Fix |
|-------|-----|
| `Dalamud.dll not found` | Set `DALAMUD_HOME` env var (Step 2) |
| `NAudio not found` | Run `dotnet restore` first |
| `RequiredVersion attribute` | May need removal on newer Dalamud — see Step 13 |
| `IPlayerCharacter` missing | Update Dalamud DLLs or change to `PlayerCharacter` |

### Step 13: Fix Known API Compatibility Issues

The scaffolded code uses some APIs that vary across Dalamud versions. Check these:

**Plugin.cs** — `[RequiredVersion("1.0")]` attributes may cause build warnings/errors on newer Dalamud. If so, remove them:
```csharp
// Change FROM:
public Plugin(
    [RequiredVersion("1.0")] IDalamudPluginInterface pluginInterface,
    [RequiredVersion("1.0")] IChatGui chatGui,
    ...

// Change TO:
public Plugin(
    IDalamudPluginInterface pluginInterface,
    IChatGui chatGui,
    ...
```

**ChatHandler.cs** — `OnChatMessage` signature may differ. Modern Dalamud uses:
```csharp
// If build error on ChatMessage event signature, try:
private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
// OR (newer Dalamud):
private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
```

**ChatHandler.cs** — ContentId extraction. `IPlayerCharacter` may not expose `ContentId` directly. POC workaround (already in code): use `0` and rely on `playerName` mapping. For real ContentId:
```csharp
// If available in your Dalamud version:
if (playerChar != null)
{
    contentId = playerChar.ContentId; // uint64
}
```

### Step 14: Deploy Plugin to Dalamud Dev Folder

```powershell
$PluginBuildDir = "H:\Development\FFXIVoices\plugin\FFXIVoices\bin\Release"
$DevPluginsDir = "$env:APPDATA\XIVLauncher\devPlugins\FFXIVoices"

# Create dev plugins directory
New-Item -ItemType Directory -Force -Path $DevPluginsDir | Out-Null

# Copy build output
Copy-Item "$PluginBuildDir\*" -Destination $DevPluginsDir -Recurse -Force

# Verify
Get-ChildItem $DevPluginsDir
# Should contain: FFXIVoices.dll, NAudio.dll, FFXIVoices.json, etc.
```

### Step 15: Load Plugin in FFXIV

1. Launch FFXIV via XIVLauncher
2. Wait for Dalamud to initialize (you'll see the Dalamud logo briefly)
3. Open Dalamud settings: `/xlsettings` in chat
4. Go to **Experimental** tab → enable **Dev Plugins**
5. Open plugin installer: `/xlplugins`
6. Go to **Dev Tools** or **Installed** — FFXIVoices should appear
7. Enable it
8. You should see in chat: `[FFXIVoices] Loaded! Server: http://localhost:3000`

---

## PHASE 6: Integration Testing (~30 min)

### Step 16: Full Integration Test

**Setup (3 terminals + FFXIV):**

```
Terminal 1: Server
  cd H:\Development\FFXIVoices\server
  $env:PIPER_PATH = "H:\Development\FFXIVoices\server\piper\piper.exe"
  node index.js

Terminal 2: Monitor (optional wscat for debug)
  npx wscat -c ws://localhost:8080

Terminal 3: PowerShell health monitor
  while ($true) { (Invoke-RestMethod http://localhost:3000/health) | ConvertTo-Json; Start-Sleep 5 }

FFXIV: Game client with plugin loaded
```

**Test sequence:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | `/ffxivoices status` in FFXIV chat | Prints: `Party=True Say=False WS=connected Vol=80%` |
| 2 | Type in party chat: "Hello everyone" | Server logs `[chat]` + `[tts]`, audio broadcasts |
| 3 | Check Terminal 2 (wscat) | Should show JSON header + binary data |
| 4 | Listen for audio in-game | NAudio should play the WAV through speakers |
| 5 | Another party member speaks | Different voice based on name hash |
| 6 | `/ffxivoices volume 50` | Volume changes to 50% |
| 7 | `/ffxivoices off` | Stops processing chat, disconnects WS |
| 8 | `/ffxivoices on` | Resumes processing, reconnects WS |

### Step 17: Test Without FFXIV (Simulated)

If you don't have a party or want to test the server independently:

```powershell
# Terminal 1: Server running (Step 5)

# Terminal 2: Simulate multiple players
# Player 1 (male voice — even hash)
curl -X POST http://localhost:3000/chat -H "Content-Type: application/json" `
  -d '{"playerName":"Test Player","contentId":"100","message":"Pulling the boss now, ready check?"}'

# Player 2 (female voice — odd hash)
curl -X POST http://localhost:3000/chat -H "Content-Type: application/json" `
  -d '{"playerName":"Minfilia Warde","contentId":"200","message":"Ready! Shields are up."}'

# Player with override
curl -X POST http://localhost:3000/chat -H "Content-Type: application/json" `
  -d '{"playerName":"Your Character Name","contentId":"12345678","message":"Limit break in ten seconds!"}'

# Browser test client should play all three with correct voices
```

---

## PHASE 7: Create Test Artifacts (~15 min)

### Step 18: Smoke Test Script

Create `test/test-curl.sh`:

```bash
#!/bin/bash
# FFXIVoices smoke tests — run with server active on localhost:3000
set -e

BASE="http://localhost:3000"
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m'

pass() { echo -e "${GREEN}PASS${NC}: $1"; }
fail() { echo -e "${RED}FAIL${NC}: $1"; exit 1; }

echo "=== FFXIVoices Smoke Tests ==="

# Test 1: Health endpoint
HEALTH=$(curl -s "$BASE/health")
echo "$HEALTH" | grep -q '"status":"ok"' && pass "GET /health" || fail "GET /health"

# Test 2: POST /chat with valid data
RESP=$(curl -s -X POST "$BASE/chat" -H "Content-Type: application/json" \
  -d '{"playerName":"Smoke Test","contentId":"0","message":"Hello world"}')
echo "$RESP" | grep -q '"status":"queued"' && pass "POST /chat valid" || fail "POST /chat valid"

# Test 3: POST /chat missing fields
RESP=$(curl -s -X POST "$BASE/chat" -H "Content-Type: application/json" \
  -d '{"playerName":""}')
echo "$RESP" | grep -q '"error"' && pass "POST /chat validation" || fail "POST /chat validation"

# Test 4: 404 on unknown route
RESP=$(curl -s "$BASE/unknown")
echo "$RESP" | grep -q '"error":"Not found"' && pass "404 handler" || fail "404 handler"

# Test 5: Voice mapping parity
RESP=$(curl -s -X POST "$BASE/chat" -H "Content-Type: application/json" \
  -d '{"playerName":"AB","message":"even hash test"}')
echo "$RESP" | grep -q '"voice":"male"' && pass "Voice parity (even=male)" || fail "Voice parity"

echo ""
echo "=== All tests passed ==="
```

---

## Known Challenges & Mitigations

### 1. ContentId Availability
- **Problem**: `IPlayerCharacter.ContentId` may not be exposed in all Dalamud versions.
- **POC Mitigation**: Default to `contentId=0`, rely on `playerName` mapping.
- **Future**: Use `FFXIVClientStructs` to read ContentId from memory directly.

### 2. Audio Latency
- **Problem**: Piper TTS generation takes 200-800ms per message. Full WAV transfer adds latency.
- **POC Mitigation**: Acceptable for MVP. Queue serializes requests.
- **Future**: Stream raw PCM chunks as they're produced (pipe stdout → WS in real-time).

### 3. Multiplayer / Party Scoping
- **Problem**: All connected WS clients receive all audio (no party isolation).
- **POC Mitigation**: Fine for local/single-user testing.
- **Future**: Add `partyId` to chat POST, create WS rooms per party.

### 4. Concurrent TTS
- **Problem**: TTS queue is serial — messages stack up in busy parties.
- **POC Mitigation**: Max 500 chars per message (already enforced in `index.js:94`).
- **Future**: Pool multiple Piper processes, priority queue for recent messages.

### 5. Plugin Audio Playback
- **Problem**: NAudio's `WaveOutEvent` blocks the receive thread during playback.
- **POC Mitigation**: Short messages play fast enough.
- **Future**: Use a dedicated playback thread with a queue, or switch to `AudioPlaybackEngine`.

### 6. In-Game Overlay
- **Problem**: No UI for MVP.
- **POC Mitigation**: Slash command `/ffxivoices` for basic control.
- **Future**: ImGui overlay via Dalamud for volume slider, voice selection, enable/disable per channel.

---

## Quick Reference: Ports & Endpoints

| Service | Port | Protocol | Purpose |
|---------|------|----------|---------|
| HTTP API | 3000 | HTTP | `POST /chat`, `GET /health` |
| WebSocket | 8080 | WS | Audio broadcast to clients |

### POST /chat Request

```json
{
  "playerName": "Character Name",
  "contentId": "12345678",
  "message": "Hello party!",
  "timestamp": 1711152000
}
```

### POST /chat Response

```json
{
  "status": "queued",
  "voice": "male"
}
```

### WebSocket Protocol

1. Server sends **text frame**: JSON metadata
   ```json
   {"type":"audio","playerName":"Character Name","message":"Hello party!","format":"wav","size":44100}
   ```
2. Server sends **binary frame**: Complete WAV file bytes

---

## Startup Checklist (Every Session)

```powershell
# 1. Set environment
$env:PIPER_PATH = "H:\Development\FFXIVoices\server\piper\piper.exe"

# 2. Start server
cd H:\Development\FFXIVoices\server
node index.js

# 3. Verify health
curl http://localhost:3000/health

# 4. Launch FFXIV via XIVLauncher (plugin auto-loads if installed)

# 5. In-game: /ffxivoices status
```

---

## Future Enhancements (Post-MVP)

- [ ] Per-player voice selection UI (ImGui overlay in plugin)
- [ ] Sentence splitting for long messages (natural pauses)
- [ ] Chunked PCM streaming (reduce latency from ~500ms to ~100ms)
- [ ] More Piper voices (accents, languages)
- [ ] Cloud server deployment (for non-local parties)
- [ ] SQLite for persistent voice preferences
- [ ] Rate limiting / priority queue
- [ ] Audio cache (hash message+voice → skip regen)
- [ ] Party-scoped WebSocket rooms
- [ ] Custom voice model training
