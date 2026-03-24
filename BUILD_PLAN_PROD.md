# FFXIVoices Production Build Plan

> **Target**: EC2 `ubuntu@3.142.247.115` (t2.medium, us-east-2)
> **SSH**: `ssh -i PuppyCo.pem ec2-user@3.142.247.115` (Amazon Linux 2023, not Ubuntu)
> **PEM location**: `H:\Development\AIMMO\PuppyCo.pem`

---

## Architecture (Production)

```
FFXIV Game Client                           EC2 (3.142.247.115)
┌─────────────────────┐                    ┌──────────────────────────────┐
│ Dalamud Framework    │                    │  Nginx :80/:443 (reverse)    │
│ ┌─────────────────┐ │                    │  ┌────────────────────────┐  │
│ │ FFXIVoices v2   │ │   HTTP (JWT)       │  │ Express :3000          │  │
│ │ Plugin (C#)     │─┼────────────────────┼─▶│  /api/register         │  │
│ │                 │ │                    │  │  /api/login            │  │
│ │ AuthClient      │ │                    │  │  /api/chat             │  │
│ │  └→ reg/login   │ │                    │  │  /api/voices           │  │
│ │  └→ JWT stored  │ │                    │  │  /api/credits          │  │
│ │                 │ │                    │  │  /api/admin            │  │
│ │ ChatHandler     │ │                    │  └──────────┬─────────────┘  │
│ │  └→ POST /chat  │ │                    │             │                │
│ │  └→ zone/pos    │ │                    │  ┌──────────▼─────────────┐  │
│ │                 │ │                    │  │ BullMQ + Redis Queue   │  │
│ │ AudioPlayer     │ │   WebSocket :8080  │  │  └→ TTS Worker (x2)   │  │
│ │  └→ WS auth     │◀┼────────────────────┼──│  └→ Piper subprocess  │  │
│ │  └→ NAudio play │ │   (proximity filt) │  │  └→ Proximity filter  │  │
│ └─────────────────┘ │                    │  └──────────────────────────┘│
└─────────────────────┘                    │  ┌──────────────────────────┐│
                                           │  │ MongoDB 7.0             ││
                                           │  │  users, voices          ││
                                           │  └──────────────────────────┘│
                                           │  ┌──────────────────────────┐│
                                           │  │ Piper TTS /opt/piper     ││
                                           │  │  ryan-medium + amy-medium││
                                           │  └──────────────────────────┘│
                                           └──────────────────────────────┘
```

---

## Phases Completed

### PHASE 1: EC2 Bootstrap ✅
- **SSH**: `ec2-user@3.142.247.115` via `PuppyCo.pem` (Amazon Linux 2023, NOT Ubuntu)
- **Installed**: Node.js 18.20.8, PM2 6.0.14, Nginx 1.28.2, Redis 6.2.20, MongoDB 7.0.31
- **Piper TTS**: `/opt/piper/piper` v1.2.0 with ryan-medium + amy-medium voices
- **PM2**: Auto-restart on boot, saved process list
- **Nginx**: Reverse proxy :80 → :3000 (HTTP) and /ws → :8080 (WebSocket)
- **Ports**: Controlled via AWS Security Group (80, 443, 3000, 8080, 22)

### PHASE 2: Server v2 ✅
- **Stack**: Express + Mongoose + BullMQ + ioredis + JWT + Stripe
- **Code**: `/opt/ffxivoices-prod/src/`
- **DB Schemas**: User (email, pwHash, contentId, charName, voiceId, credits, zone/world/pos), Voice (voiceId, model, isPremium, creditCost, provider)
- **Auth**: `/api/register`, `/api/login` → JWT (7-day expiry)
- **Endpoints**: `/api/chat`, `/api/voices`, `/api/voices/select`, `/api/credits`, `/api/credits/buy`, `/api/admin/*`
- **WS Auth**: Client sends `{type:"auth", token:"..."}` on connect → linked to PlayerIndex
- **TTS Queue**: BullMQ on Redis, 2 concurrent workers

### PHASE 3: Plugin v2 ✅
- **New files**: `AuthClient.cs` (register/login via HTTP, token storage)
- **Updated**: `Configuration.cs` (Email, AuthToken, CharName, ContentId), `ChatHandler.cs` (auth + zone/pos), `AudioPlayer.cs` (WS auth handshake), `Plugin.cs` (login/register/logout commands)
- **Commands**: `/ffxivoices login <email> <pw>`, `/ffxivoices register <email> <pw>`, `/ffxivoices logout`, `/ffxivoices on|off|status|volume|server`
- **Build**: 0 errors, 23 warnings (nullable + deprecated LocalPlayer)

### PHASE 4: Proximity Logic ✅
- **PlayerIndex**: In-memory Map of userId → {contentId, charName, zone, mapId, x, y, z}
- **WS registration**: Auth'd WS clients linked to userId in index
- **Proximity filter**: Same zone + same mapId + distance < 50 yalms (Dalamud coords)
- **Fallback**: Zone-only filter if no position, broadcast-all if no zone
- **Cleanup**: Stale entries removed after 5 minutes

### PHASE 5: Premium Voices ✅
- **Voice model**: DB collection with `isPremium`, `creditCost`, `provider` (piper/elevenlabs)
- **Credits**: User.credits field, deducted on chat POST for premium voices
- **Stripe**: `/api/credits/buy` → Stripe Checkout, webhook for credit fulfillment
- **Packages**: 100 ($1.99), 500 ($7.99), 1000 ($12.99) credits
- **Note**: Stripe keys not yet configured (env vars ready: `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`)

### PHASE 6: Admin Dashboard ✅
- **Endpoints**: `/api/admin/users` (paginated), `/api/admin/stats`, `/api/admin/voices` (CRUD), `/api/admin/users/:id/credits`
- **Auth**: JWT + `isAdmin` flag on User document
- **Stats**: Total users, active (24h), total credits in circulation, voice count

### PHASE 7: Tests ✅
- **Test suite**: `test/test-prod-api.sh` — 17 tests, all passing against live EC2
- **Coverage**: Health, register, duplicate reg, login, bad credentials, authenticated access, unauthorized access, voices list, chat queue, unauth chat, voice selection, position update, 404, credits
- **TTS verified**: Piper generates WAV (~100KB) in ~1 second on t2.medium

### PHASE 8: Polish ✅
- **Docs**: This file (BUILD_PLAN_PROD.md)
- **PM2**: ecosystem.prod.json with env vars, log paths, memory limit
- **Monitoring**: `pm2 monit`, `pm2 logs ffxivoices`
- **Plugin distribution**: Build output in `plugin/FFXIVoices/bin/Release/`

---

## API Reference

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/health` | No | Server health + component status |
| POST | `/api/register` | No | `{email, password, contentId?, charName?}` → JWT |
| POST | `/api/login` | No | `{email, password, contentId?, charName?}` → JWT |
| GET | `/api/me` | JWT | Current user profile |
| GET | `/api/voices` | No | List available voices |
| PUT | `/api/voices/select` | JWT | `{voiceId}` — select voice |
| POST | `/api/chat` | JWT | `{message, zone?, mapId?, x?, y?, z?}` → TTS queue |
| POST | `/api/chat/pos` | JWT | `{zone, mapId, world?, x, y, z}` — update position |
| GET | `/api/credits` | JWT | Balance + available packages |
| POST | `/api/credits/buy` | JWT | `{packageId}` → Stripe checkout URL |
| GET | `/api/admin/users` | Admin | Paginated user list |
| GET | `/api/admin/stats` | Admin | Dashboard stats |
| POST | `/api/admin/voices` | Admin | Create/update voice |
| DELETE | `/api/admin/voices/:id` | Admin | Disable voice |
| PUT | `/api/admin/users/:id/credits` | Admin | Add/set credits |

## WebSocket Protocol

1. Connect to `ws://3.142.247.115:8080`
2. Send auth: `{"type":"auth","token":"<JWT>"}`
3. Receive auth response: `{"type":"auth","status":"ok","userId":"..."}`
4. Send position updates: `{"type":"pos","zone":132,"mapId":5,"x":100,"y":20,"z":-50}`
5. Receive TTS audio:
   - Text frame: `{"type":"audio","playerName":"...","message":"...","format":"wav","size":N}`
   - Binary frame: WAV file bytes

## Quick Commands

```bash
# SSH to server
ssh -i /tmp/puppyco.pem ec2-user@3.142.247.115

# View logs
pm2 logs ffxivoices

# Restart server
pm2 restart ffxivoices

# Deploy new code
scp -i /tmp/puppyco.pem -r server-prod/src/* ec2-user@3.142.247.115:/opt/ffxivoices-prod/src/
ssh -i /tmp/puppyco.pem ec2-user@3.142.247.115 "pm2 restart ffxivoices"

# Run tests
bash test/test-prod-api.sh

# Build plugin
export DALAMUD_HOME="H:/Development/FFXIVoices/dalamud"
cd plugin/FFXIVoices && dotnet build -c Release
```

## Environment Variables (ecosystem.prod.json)

| Variable | Value | Description |
|----------|-------|-------------|
| `PORT` | 3000 | Express HTTP port |
| `WS_PORT` | 8080 | WebSocket port |
| `MONGO_URI` | mongodb://127.0.0.1:27017/ffxivoices | MongoDB connection |
| `REDIS_URL` | redis://127.0.0.1:6379 | Redis/BullMQ connection |
| `PIPER_PATH` | /opt/piper/piper | Piper TTS binary |
| `VOICES_DIR` | /opt/ffxivoices-prod/voices | Voice model directory |
| `JWT_SECRET` | (generated) | JWT signing secret |
| `STRIPE_SECRET_KEY` | (not set) | Stripe API key for credits |
| `STRIPE_WEBHOOK_SECRET` | (not set) | Stripe webhook verification |
