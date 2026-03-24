const http = require("http");
const { WebSocketServer, WebSocket } = require("ws");
const { spawn } = require("child_process");
const path = require("path");
const fs = require("fs");

// --- Config ---
const HTTP_PORT = 3000;
const WS_PORT = 8080;
const VOICES_DIR = path.join(__dirname, "voices");
const PIPER_PATH = process.env.PIPER_PATH || "piper"; // Expects piper in PATH or set env var

const VOICE_MODELS = {
  male: path.join(VOICES_DIR, "en_US-ryan-medium.onnx"),
  female: path.join(VOICES_DIR, "en_US-amy-medium.onnx"),
};

// --- Voice Preferences ---
let voiceOverrides = { byContentId: {}, byPlayerName: {} };
const overridesPath = path.join(__dirname, "voices.json");
const VALID_VOICES = Object.keys(VOICE_MODELS);

function loadVoiceOverrides() {
  try {
    const data = JSON.parse(fs.readFileSync(overridesPath, "utf8"));
    voiceOverrides = {
      byContentId: data.byContentId || {},
      byPlayerName: data.byPlayerName || {},
    };
    console.log(
      `[voices] Loaded ${Object.keys(voiceOverrides.byContentId).length} contentId + ` +
      `${Object.keys(voiceOverrides.byPlayerName).length} playerName overrides`
    );
  } catch {
    console.log("[voices] No overrides file, using defaults");
  }
}

function saveVoiceOverrides() {
  const output = {
    _comment: "Manual voice overrides by contentId or playerName. Unmatched players use name-hash parity.",
    byContentId: voiceOverrides.byContentId,
    byPlayerName: voiceOverrides.byPlayerName,
  };
  fs.writeFileSync(overridesPath, JSON.stringify(output, null, 2) + "\n");
}

// Initial load
loadVoiceOverrides();

// Hot-reload on file change
fs.watch(overridesPath, { persistent: false }, (eventType) => {
  if (eventType === "change") {
    console.log("[voices] voices.json changed, reloading...");
    loadVoiceOverrides();
  }
});

function getVoice(playerName, contentId) {
  // Priority: contentId override > playerName override > name hash parity
  if (contentId && voiceOverrides.byContentId[contentId]) {
    return voiceOverrides.byContentId[contentId];
  }
  if (playerName && voiceOverrides.byPlayerName[playerName]) {
    return voiceOverrides.byPlayerName[playerName];
  }
  // Simple hash: sum char codes, even = male, odd = female
  const hash = playerName
    .split("")
    .reduce((sum, ch) => sum + ch.charCodeAt(0), 0);
  return hash % 2 === 0 ? "male" : "female";
}

// --- WebSocket Server ---
const wss = new WebSocketServer({ port: WS_PORT });
const clients = new Set();

wss.on("connection", (ws) => {
  clients.add(ws);
  console.log(`[ws] Client connected (${clients.size} total)`);

  ws.on("close", () => {
    clients.delete(ws);
    console.log(`[ws] Client disconnected (${clients.size} total)`);
  });

  ws.on("error", (err) => {
    console.error("[ws] Client error:", err.message);
    clients.delete(ws);
  });
});

function broadcastAudio(wavBuffer, metadata) {
  // Send JSON metadata header then binary audio
  const header = JSON.stringify({
    type: "audio",
    playerName: metadata.playerName,
    message: metadata.message,
    format: "wav",
    size: wavBuffer.length,
  });

  for (const client of clients) {
    if (client.readyState === WebSocket.OPEN) {
      client.send(header);
      client.send(wavBuffer);
    }
  }
  console.log(`[ws] Broadcast ${wavBuffer.length} bytes to ${clients.size} clients`);
}

// --- TTS via Piper ---
function generateTTS(text, voiceType) {
  return new Promise((resolve, reject) => {
    const model = VOICE_MODELS[voiceType];
    if (!model) {
      return reject(new Error(`Unknown voice type: ${voiceType}`));
    }
    if (!fs.existsSync(model)) {
      return reject(
        new Error(`Voice model not found: ${model}. Download Piper voices first.`)
      );
    }

    // Sanitize text: remove special chars that could break shell piping
    const sanitized = text.replace(/[^\w\s.,!?'";\-:()]/g, "").substring(0, 500);

    const piper = spawn(PIPER_PATH, [
      "--model", model,
      "--output-raw",
    ]);

    const chunks = [];
    let stderr = "";

    piper.stdout.on("data", (chunk) => chunks.push(chunk));
    piper.stderr.on("data", (data) => (stderr += data.toString()));

    piper.on("close", (code) => {
      if (code !== 0) {
        return reject(new Error(`Piper exited ${code}: ${stderr}`));
      }
      // Convert raw PCM (16-bit mono 22050Hz) to WAV
      const pcm = Buffer.concat(chunks);
      const wav = pcmToWav(pcm, 22050, 1, 16);
      resolve(wav);
    });

    piper.on("error", (err) => {
      reject(new Error(`Failed to spawn piper: ${err.message}. Is piper installed?`));
    });

    // Feed text to piper's stdin
    piper.stdin.write(sanitized);
    piper.stdin.end();
  });
}

// --- PCM to WAV conversion ---
function pcmToWav(pcmData, sampleRate, numChannels, bitsPerSample) {
  const byteRate = (sampleRate * numChannels * bitsPerSample) / 8;
  const blockAlign = (numChannels * bitsPerSample) / 8;
  const dataSize = pcmData.length;
  const headerSize = 44;
  const buffer = Buffer.alloc(headerSize + dataSize);

  // RIFF header
  buffer.write("RIFF", 0);
  buffer.writeUInt32LE(36 + dataSize, 4);
  buffer.write("WAVE", 8);

  // fmt chunk
  buffer.write("fmt ", 12);
  buffer.writeUInt32LE(16, 16); // chunk size
  buffer.writeUInt16LE(1, 20); // PCM format
  buffer.writeUInt16LE(numChannels, 22);
  buffer.writeUInt32LE(sampleRate, 24);
  buffer.writeUInt32LE(byteRate, 28);
  buffer.writeUInt16LE(blockAlign, 32);
  buffer.writeUInt16LE(bitsPerSample, 34);

  // data chunk
  buffer.write("data", 36);
  buffer.writeUInt32LE(dataSize, 40);
  pcmData.copy(buffer, headerSize);

  return buffer;
}

// --- TTS Queue (serialize requests to avoid piper contention) ---
const ttsQueue = [];
let processing = false;

async function enqueueTTS(chatData) {
  ttsQueue.push(chatData);
  if (!processing) {
    processQueue();
  }
}

async function processQueue() {
  processing = true;
  while (ttsQueue.length > 0) {
    const data = ttsQueue.shift();
    try {
      const voice = getVoice(data.playerName, data.contentId);
      console.log(`[tts] Generating: "${data.message}" voice=${voice} player=${data.playerName}`);
      const wavBuffer = await generateTTS(data.message, voice);
      broadcastAudio(wavBuffer, data);
    } catch (err) {
      console.error(`[tts] Error: ${err.message}`);
    }
  }
  processing = false;
}

// --- HTTP Server ---
const server = http.createServer((req, res) => {
  // CORS headers for local dev
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");

  if (req.method === "OPTIONS") {
    res.writeHead(204);
    return res.end();
  }

  if (req.method === "GET" && req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    return res.end(
      JSON.stringify({
        status: "ok",
        clients: clients.size,
        queueLength: ttsQueue.length,
        voices: Object.keys(VOICE_MODELS),
      })
    );
  }

  if (req.method === "POST" && req.url === "/chat") {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      try {
        const data = JSON.parse(body);
        const { playerName, contentId, message, timestamp } = data;

        if (!playerName || !message) {
          res.writeHead(400, { "Content-Type": "application/json" });
          return res.end(JSON.stringify({ error: "playerName and message required" }));
        }

        console.log(`[chat] ${playerName}: "${message}"`);
        enqueueTTS({ playerName, contentId: contentId || "", message, timestamp });

        res.writeHead(202, { "Content-Type": "application/json" });
        res.end(
          JSON.stringify({
            status: "queued",
            voice: getVoice(playerName, contentId),
          })
        );
      } catch (err) {
        res.writeHead(400, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ error: "Invalid JSON" }));
      }
    });
    return;
  }

  // GET /voices — list all overrides and available voices
  if (req.method === "GET" && req.url === "/voices") {
    res.writeHead(200, { "Content-Type": "application/json" });
    return res.end(
      JSON.stringify({
        available: VALID_VOICES,
        overrides: voiceOverrides,
      })
    );
  }

  // PUT /voices — update overrides (merge, not replace)
  // Body: { "byContentId": { "123": "male" }, "byPlayerName": { "Name": "female" } }
  if (req.method === "PUT" && req.url === "/voices") {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      try {
        const data = JSON.parse(body);

        // Validate voice values
        const allEntries = [
          ...Object.entries(data.byContentId || {}),
          ...Object.entries(data.byPlayerName || {}),
        ];
        for (const [key, voice] of allEntries) {
          if (!VALID_VOICES.includes(voice)) {
            res.writeHead(400, { "Content-Type": "application/json" });
            return res.end(
              JSON.stringify({ error: `Invalid voice "${voice}" for "${key}". Valid: ${VALID_VOICES.join(", ")}` })
            );
          }
        }

        // Merge overrides
        if (data.byContentId) {
          Object.assign(voiceOverrides.byContentId, data.byContentId);
        }
        if (data.byPlayerName) {
          Object.assign(voiceOverrides.byPlayerName, data.byPlayerName);
        }

        saveVoiceOverrides();
        console.log("[voices] Overrides updated via API");

        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ status: "updated", overrides: voiceOverrides }));
      } catch {
        res.writeHead(400, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ error: "Invalid JSON" }));
      }
    });
    return;
  }

  // DELETE /voices — remove specific overrides
  // Body: { "byContentId": ["123"], "byPlayerName": ["Name"] }
  if (req.method === "DELETE" && req.url === "/voices") {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      try {
        const data = JSON.parse(body);
        let removed = 0;

        if (data.byContentId) {
          for (const id of data.byContentId) {
            if (voiceOverrides.byContentId[id] !== undefined) {
              delete voiceOverrides.byContentId[id];
              removed++;
            }
          }
        }
        if (data.byPlayerName) {
          for (const name of data.byPlayerName) {
            if (voiceOverrides.byPlayerName[name] !== undefined) {
              delete voiceOverrides.byPlayerName[name];
              removed++;
            }
          }
        }

        saveVoiceOverrides();
        console.log(`[voices] Removed ${removed} override(s) via API`);

        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ status: "deleted", removed, overrides: voiceOverrides }));
      } catch {
        res.writeHead(400, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ error: "Invalid JSON" }));
      }
    });
    return;
  }

  // GET /voices/resolve?playerName=X&contentId=Y — check what voice would be assigned
  if (req.method === "GET" && req.url.startsWith("/voices/resolve")) {
    const url = new URL(req.url, `http://localhost:${HTTP_PORT}`);
    const playerName = url.searchParams.get("playerName");
    const contentId = url.searchParams.get("contentId") || "";

    if (!playerName) {
      res.writeHead(400, { "Content-Type": "application/json" });
      return res.end(JSON.stringify({ error: "playerName query param required" }));
    }

    const voice = getVoice(playerName, contentId);
    const source = contentId && voiceOverrides.byContentId[contentId]
      ? "contentId"
      : voiceOverrides.byPlayerName[playerName]
        ? "playerName"
        : "hash";

    res.writeHead(200, { "Content-Type": "application/json" });
    return res.end(JSON.stringify({ playerName, contentId, voice, source }));
  }

  res.writeHead(404, { "Content-Type": "application/json" });
  res.end(JSON.stringify({ error: "Not found" }));
});

server.listen(HTTP_PORT, () => {
  console.log(`[http] FFXIVoices server listening on http://localhost:${HTTP_PORT}`);
  console.log(`[ws]   WebSocket server on ws://localhost:${WS_PORT}`);
  console.log(`[tts]  Piper path: ${PIPER_PATH}`);
  console.log(`[tts]  Voice models dir: ${VOICES_DIR}`);

  // Check if voice models exist
  for (const [name, modelPath] of Object.entries(VOICE_MODELS)) {
    const exists = fs.existsSync(modelPath);
    console.log(`[tts]  ${name}: ${path.basename(modelPath)} ${exists ? "OK" : "MISSING"}`);
  }
});
