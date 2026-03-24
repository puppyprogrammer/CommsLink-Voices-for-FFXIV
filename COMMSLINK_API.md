# FFXIVoices — CommsLink API Integration Guide

> This document describes the live API that the FFXIVoices Dalamud plugin should connect to. The API is running on CommsLink's production server.

---

## Server URLs

| Environment | HTTP API | WebSocket |
|-------------|----------|-----------|
| **Production** | `https://commslink.net/api/v1/ffxiv` | `ws://3.134.145.169:8080` |
| **Test** | `http://3.134.145.169:4000/api/v1/ffxiv` | `ws://3.134.145.169:8080` |

> **Note:** WebSocket is currently exposed on the raw IP:8080 (no SSL yet). For testing this is fine. Production WSS will be added later via nginx proxy.

---

## Authentication

### POST /api/v1/ffxiv/register

Creates a new FFXIVoices account. Returns a JWT token (30-day expiry).

**Request:**
```json
POST https://commslink.net/api/v1/ffxiv/register
Content-Type: application/json

{
  "username": "WarriorOfLight",
  "password": "securepass123",
  "contentId": "268435459",
  "charName": "Warrior of Light"
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| username | string | yes | 3-30 characters |
| password | string | yes | Minimum 6 characters |
| contentId | string | no | FFXIV character object ID |
| charName | string | no | FFXIV character name |

**Success Response (201):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "user": {
    "id": "2c75a439-6401-4d30-8378-b2b587683b37",
    "username": "WarriorOfLight",
    "charName": "Warrior of Light",
    "voiceId": "Joanna",
    "credits": 500
  }
}
```

**Error Response (400):**
```json
{"error": "Email already registered"}
```

**Rate limit:** 3 registrations per 60 seconds per IP.

---

### POST /api/v1/ffxiv/login

Authenticates an existing user. Optionally updates contentId and charName.

**Request:** Same shape as register.

**Success Response (200):** Same shape as register.

**Error Response (401):**
```json
{"error": "Invalid credentials"}
```

**Rate limit:** 5 logins per 60 seconds per IP.

---

### Token Usage

After login/register, include the JWT in all authenticated requests:

```
Authorization: Bearer <JWT>
```

The JWT payload contains `{ id, username, type: "ffxiv" }`. Tokens expire after 30 days.

---

## Chat / TTS

### POST /api/v1/ffxiv/chat

Submit a chat message for TTS generation. Returns immediately with 202. Audio is delivered asynchronously via WebSocket.

**Request (authenticated):**
```json
POST https://commslink.net/api/v1/ffxiv/chat
Content-Type: application/json
Authorization: Bearer <JWT>

{
  "message": "Hello party, let's go!",
  "zone": 132,
  "mapId": 5,
  "x": 100.5,
  "y": 20.0,
  "z": -50.3
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| message | string | yes | Max 500 characters |
| zone | number | no | FFXIV territory/zone ID |
| mapId | number | no | Map within zone |
| x | float | no | Listener's X position |
| y | float | no | Listener's Y position |
| z | float | no | Listener's Z position |

**Success Response (202):**
```json
{
  "status": "queued",
  "voice": "Joanna",
  "jobId": "tts-2c75a439-1711152000"
}
```

**How it works:**
1. Server validates JWT, identifies the sender
2. Server returns 202 immediately
3. Server generates WAV audio via Amazon Polly using the sender's selected voice
4. Server broadcasts audio to all nearby WebSocket clients (same zone, within 50 yalms)
5. 1 credit is deducted per 50 characters (minimum 1 credit)

**Error Responses:**
- 401: Missing or invalid token
- 402: Insufficient credits
- 400: Validation error (message too long, etc.)

---

## Voices

### GET /api/v1/ffxiv/voices

List all available TTS voices. No authentication required.

**Response (200):**
```json
[
  { "voice_id": "Joanna", "name": "Joanna (Female)" },
  { "voice_id": "Matthew", "name": "Matthew (Male)" },
  { "voice_id": "Salli", "name": "Salli (Female)" },
  { "voice_id": "Ivy", "name": "Ivy (Female)" },
  { "voice_id": "Kimberly", "name": "Kimberly (Female)" },
  { "voice_id": "Kendra", "name": "Kendra (Female)" },
  { "voice_id": "Ruth", "name": "Ruth (Female)" },
  { "voice_id": "Stephen", "name": "Stephen (Male)" }
]
```

These are Amazon Polly voices. Voice IDs are simple names like "Joanna", "Matthew", etc.

---

### PUT /api/v1/ffxiv/voices/select

Set the user's preferred voice. Authenticated.

**Request:**
```json
PUT https://commslink.net/api/v1/ffxiv/voices/select
Content-Type: application/json
Authorization: Bearer <JWT>

{
  "voiceId": "Matthew"
}
```

**Response (200):**
```json
{
  "success": true,
  "voiceId": "Matthew"
}
```

---

## WebSocket — Audio Delivery

### Connection

Connect to `ws://3.134.145.169:8080` after successful login.

### Auth Handshake

Immediately after connecting, send:

```json
{"type": "auth", "token": "eyJhbGciOiJIUzI1NiIs..."}
```

**Server responds:**
```json
{"type": "auth", "status": "ok", "userId": "2c75a439..."}
```

If auth fails, the server closes the connection.

### Position Updates

Send position updates so the server knows where the player is (for proximity filtering):

```json
{"type": "pos", "zone": 132, "mapId": 5, "x": 100.5, "y": 20.0, "z": -50.3}
```

The server also updates position from the `POST /api/v1/ffxiv/chat` payload.

### Receiving Audio

When another player speaks and TTS is generated, the server pushes two frames:

**Frame 1 — Text (JSON metadata):**
```json
{
  "type": "audio",
  "playerName": "Warrior of Light",
  "message": "Hello party!",
  "format": "wav",
  "size": 44100
}
```

**Frame 2 — Binary (WAV file bytes):**

Complete WAV file (RIFF header + PCM data, 16kHz, 16-bit, mono). Play directly with NAudio or any WAV player.

### Proximity Rules

Audio is only delivered to players who are:
1. In the same `zone` AND `mapId` as the sender
2. Within 50 yalms (euclidean distance in 3D)
3. If no position data is available, falls back to zone-only matching
4. If no zone data, broadcasts to all connected players

The sender does NOT receive their own audio.

---

## Plugin Configuration Changes

Update the plugin's default configuration:

```csharp
public string ServerUrl = "https://commslink.net/api/v1/ffxiv";
public string WebSocketUrl = "ws://3.134.145.169:8080";
```

### Plugin Auto-Update

The server hosts plugin releases for auto-update.

**`GET /api/v1/ffxiv/update/version`** — Check for updates (no auth).

Response:
```json
{
  "version": "0.2.0",
  "changelog": "Selective hearing, mute/unmute players",
  "sha256": "a1b2c3d4...",
  "size": 12345,
  "updatedAt": "2026-03-23T12:00:00.000Z"
}
```
Returns 404 if no release exists.

**`GET /api/v1/ffxiv/update/download`** — Download latest plugin zip (no auth). Returns `application/zip`. 404 if no release.

**`POST /api/v1/ffxiv/update/upload?version=X&changelog=Y`** — Upload a new release (admin auth required). Body is raw zip bytes, `Content-Type: application/octet-stream`. Max 20MB. Verifies PK zip magic bytes, computes SHA-256.

Upload example:
```bash
curl -X POST "https://commslink.net/api/v1/ffxiv/update/upload?version=0.2.0&changelog=Selective+hearing" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/octet-stream" \
  --data-binary @latest.zip
```

### Endpoint Mapping

| Old Plugin Endpoint | New CommsLink Endpoint |
|---------------------|----------------------|
| `POST /api/register` | `POST /api/v1/ffxiv/register` |
| `POST /api/login` | `POST /api/v1/ffxiv/login` |
| `POST /api/chat` | `POST /api/v1/ffxiv/chat` |
| `GET /api/voices` | `GET /api/v1/ffxiv/voices` |
| `PUT /api/voices/select` | `PUT /api/v1/ffxiv/voices/select` |
| `GET /update/version` | `GET /api/v1/ffxiv/update/version` |
| `GET /update/download` | `GET /api/v1/ffxiv/update/download` |
| `POST /update/upload` | `POST /api/v1/ffxiv/update/upload` |

### Key Differences from Original Spec

1. **Base path** changed from `/api/` to `/api/v1/ffxiv/`
2. **TTS engine** is Amazon Polly (not Piper) — voices are Polly voice names like "Joanna", "Matthew"
3. **Voice IDs** are simple strings ("Joanna") not UUIDs
4. **Auth response** includes a `user` object with `id`, `email`, `charName`, `voiceId`, `credits`
5. **Credits** start at 500 (1 credit ≈ 50 characters of TTS)
6. **WAV format** is 16kHz, 16-bit, mono PCM with RIFF header

---

## Credits System

- New accounts start with **500 credits**
- TTS costs **1 credit per 50 characters** (minimum 1 credit per message)
- Credits are deducted after TTS generation
- If credits reach 0, the `/chat` endpoint returns 402
- Credit top-up will be available via `commslink.net/credits` (shared payment system, coming soon)

---

## Error Format

All errors follow this shape:

```json
{
  "error": "Human-readable error message"
}
```

Or for Hapi validation errors:

```json
{
  "statusCode": 400,
  "error": "Bad Request",
  "message": "\"email\" must be a valid email"
}
```

The plugin should check for the `error` field first, then fall back to `message`.
