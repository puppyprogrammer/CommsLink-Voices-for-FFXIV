# FFXIVoices Dalamud Plugin — Server Integration Spec

> This document describes how the FFXIVoices Dalamud plugin (C#) communicates with the backend server. Use this to build out the auth API and voice/TTS API on the commslink project.

---

## Overview

FFXIVoices is a Dalamud plugin for FFXIV that intercepts in-game chat (party/say), sends it to a server for TTS generation via Piper, and plays back audio through a WebSocket connection. The plugin handles auth, chat submission, and audio playback. The server handles user management, TTS generation, and proximity-based audio delivery.

---

## Plugin Architecture

```
Plugin (C#, runs inside FFXIV)
├── Plugin.cs          — Entry point, slash command handler, DI wiring
├── Configuration.cs   — Persisted settings (server URL, WS URL, auth token, volume)
├── AuthClient.cs      — HTTP auth (register/login/logout), JWT token storage
├── ChatHandler.cs     — Hooks FFXIV chat events, POSTs messages to server
└── AudioPlayer.cs     — WebSocket client, receives WAV audio, plays via NAudio
```

---

## Configuration (stored client-side by Dalamud)

```csharp
public string ServerUrl = "http://3.142.247.115";       // HTTP API base
public string WebSocketUrl = "ws://3.142.247.115:8080";  // WS for audio
public bool EnablePartyChat = true;
public bool EnableSayChat = false;
public float Volume = 0.8f;
public string? Email;        // Stored after login
public string? AuthToken;    // JWT — stored after login, sent with all requests
public string? CharName;     // FFXIV character name
public string? ContentId;    // FFXIV character ID (GameObjectId)
```

---

## Auth Flow

The plugin requires login before it does anything. No auth = no chat processing, no WS connection.

### POST /api/register

Plugin sends when user types `/ffxivoices register <email> <password>`.

**Request:**
```json
{
  "email": "player@example.com",
  "password": "securepass123",
  "contentId": "268435459",
  "charName": "Warrior of Light"
}
```

- `contentId` and `charName` are grabbed from the local player object at time of register/login. They may be null if the player isn't loaded yet.
- `contentId` is currently `IGameObject.GameObjectId` (uint64 as string). This is NOT the persistent ContentId — it's a session object ID. Future versions may use the real ContentId via FFXIVClientStructs.

**Expected Response 201:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "user": {
    "id": "6651a...",
    "email": "player@example.com",
    "charName": "Warrior of Light",
    "voiceId": "male",
    "credits": 0
  }
}
```

**Expected Error 400:**
```json
{"error": "Email already registered"}
```

The plugin stores `token` in `config.AuthToken` and `email` in `config.Email`, then auto-connects the WebSocket.

### POST /api/login

Same as register. Plugin sends when user types `/ffxivoices login <email> <password>`.

**Request:** Same shape as register.

**Expected Response 200:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "user": { ... }
}
```

**Expected Error 401:**
```json
{"error": "Invalid credentials"}
```

### Logout

Client-side only. Plugin clears `config.AuthToken`, disconnects WS. No server call.

### Token Usage

After login, ALL authenticated HTTP requests include:
```
Authorization: Bearer <JWT>
```

The plugin creates a new `HttpClient` per request via `AuthClient.GetAuthedClient()` which sets this header.

---

## Chat Flow

### What triggers a chat message

The plugin hooks `IChatGui.ChatMessage`. It fires for every chat message the player sees. The plugin filters to:

- `XivChatType.Party` or `XivChatType.CrossParty` (if `EnablePartyChat` is true)
- `XivChatType.Say` (if `EnableSayChat` is true)

Everything else is ignored.

### POST /api/chat

**Request (authed):**
```json
{
  "message": "Hello party, let's go!",
  "zone": 132,
  "mapId": 5,
  "x": 100.5,
  "y": 20.0,
  "z": -50.3
}
```

| Field | Type | Source | Notes |
|-------|------|--------|-------|
| message | string | `SeString.TextValue` from chat event | The actual chat text. Max 500 chars server-side. |
| zone | int | `IClientState.TerritoryType` | FFXIV territory/zone ID |
| mapId | int | `IClientState.MapId` | Map within zone |
| x, y, z | float | `IPlayerCharacter.Position` | Local player's position (NOT sender's) |

**Important:** The plugin sends the LOCAL player's position, not the sender's position. The server uses this for proximity filtering — it needs to know where the LISTENER is to decide whether to send them audio.

**Note:** The plugin does NOT send `playerName` or `contentId` in the chat payload. The server identifies the sender from the JWT token. The sender's name should come from the user profile on the server side.

**Expected Response 202:**
```json
{
  "status": "queued",
  "voice": "male",
  "jobId": "tts-6651a-1711152000"
}
```

Non-success responses are logged but not shown to the user.

---

## WebSocket Audio Flow

### Connection

The plugin connects to the WebSocket URL (`ws://host:8080`) after successful login. It auto-reconnects on disconnect with a 3-second delay.

### Auth Handshake

Immediately after WS connect, the plugin sends:

```json
{"type": "auth", "token": "eyJhbGciOiJIUzI1NiIs..."}
```

**Expected server response (text frame):**
```json
{"type": "auth", "status": "ok", "userId": "6651a..."}
```

The plugin checks for `"type":"auth"` in received messages and skips them (doesn't try to play them as audio).

### Receiving Audio

After auth, the server pushes TTS audio as pairs of frames:

1. **Text frame** — JSON metadata:
```json
{
  "type": "audio",
  "playerName": "Warrior of Light",
  "message": "Hello party!",
  "format": "wav",
  "size": 44100
}
```

2. **Binary frame** — Complete WAV file bytes

The plugin reads these sequentially: text header first, then binary data. It plays the WAV using NAudio's `WaveOutEvent` with `VolumeWaveProvider16` for volume control.

**Playback is blocking** — the receive loop waits for each WAV to finish playing before reading the next message. This means messages queue up naturally on the client side.

### Position Updates (not yet implemented in plugin)

The API spec defines `{"type": "pos", ...}` messages for position updates over WS, and a `POST /api/chat/pos` endpoint. The current plugin does NOT send position updates over WS — it only sends position data with each chat POST. The server should handle both approaches.

---

## Proximity Filtering (server responsibility)

The server is responsible for deciding who receives audio. The spec says:

- Audio goes to players in the same `zone` + `mapId` within 50 yalms
- Falls back to zone-only if no position data
- Falls back to broadcast-all if no zone data

The plugin just sends its position with chat messages and trusts the server to filter.

---

## Slash Commands

The plugin registers `/ffxivoices` with these subcommands:

| Command | Action |
|---------|--------|
| `/ffxivoices` (no args) | Print status |
| `/ffxivoices register <email> <pass>` | POST /api/register, store token, connect WS |
| `/ffxivoices login <email> <pass>` | POST /api/login, store token, connect WS |
| `/ffxivoices logout` | Clear token, disconnect WS (client-only) |
| `/ffxivoices on` | Enable chat processing + connect WS |
| `/ffxivoices off` | Disable chat processing + disconnect WS |
| `/ffxivoices status` | Print auth state, party/say toggle, WS state, volume |
| `/ffxivoices volume <0-100>` | Set playback volume |
| `/ffxivoices server <url>` | Change server URL |

---

## What the Server Needs to Implement

### Auth API
- `POST /api/register` — Create user, return JWT. Store email, password (hashed), contentId, charName.
- `POST /api/login` — Validate credentials, return JWT. Optionally update contentId/charName.
- JWT should contain at minimum: `userId`, `email`, `iss` claim.
- Token is stored client-side indefinitely (no refresh flow yet).

### Chat/TTS API
- `POST /api/chat` (authed) — Accept message + position, queue TTS generation, return 202.
- Identify sender from JWT (not from request body).
- Generate WAV audio using Piper TTS with the user's selected voice.
- Broadcast audio to nearby WS clients (proximity filter by zone/map/position).

### WebSocket Server
- Accept connections on a configurable port (currently 8080).
- Handle `{"type": "auth", "token": "..."}` — validate JWT, associate connection with user.
- Send auth response: `{"type": "auth", "status": "ok", "userId": "..."}`.
- Push audio as text frame (metadata JSON) + binary frame (WAV bytes).
- Handle disconnects gracefully, clean up player state.

### Voice API (future, not yet consumed by plugin)
- `GET /api/voices` — List available voices.
- `PUT /api/voices/select` (authed) — Set user's preferred voice.
- The plugin doesn't call these yet but the server should support them for when UI is added.

---

## Response Format Expectations

The plugin deserializes these specific shapes:

```csharp
// Auth responses
public class AuthResponse {
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

public class ErrorResponse {
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
```

- On success: plugin looks for `token` field.
- On error: plugin looks for `error` field and displays it in chat.
- All other response fields (user object, etc.) are currently ignored by the plugin but should be included for future use.

---

## Network Summary

| What | Protocol | URL Pattern | Auth |
|------|----------|-------------|------|
| Register | HTTP POST | `{ServerUrl}/api/register` | None |
| Login | HTTP POST | `{ServerUrl}/api/login` | None |
| Chat submit | HTTP POST | `{ServerUrl}/api/chat` | Bearer JWT |
| Audio stream | WebSocket | `{WebSocketUrl}` | JSON auth msg after connect |

Default URLs:
- HTTP: `http://3.142.247.115` (port 80)
- WS: `ws://3.142.247.115:8080`
- Production: `https://ffxivoices.commslink.net` / `wss://ffxivoices.commslink.net/ws`
