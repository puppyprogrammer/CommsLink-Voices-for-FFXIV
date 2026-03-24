using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace FFXIVoices;

public sealed class AudioPlayer : IDisposable
{
    private readonly Configuration config;
    private readonly IPluginLog log;
    private ClientWebSocket? ws;
    private CancellationTokenSource? cts;
    private Task? receiveTask;
    private float volume;

    public bool IsConnected => ws?.State == WebSocketState.Open;

    /// <summary>Online users received from WS.</summary>
    public List<OnlineUser> OnlineUsers { get; private set; } = new();

    public AudioPlayer(Configuration config, IPluginLog log)
    {
        this.config = config;
        this.log = log;
        this.volume = config.Volume;
    }

    public void Connect()
    {
        if (IsConnected) return;

        cts = new CancellationTokenSource();
        receiveTask = Task.Run(() => ReceiveLoop(cts.Token));
    }

    public void Disconnect()
    {
        cts?.Cancel();
        try
        {
            ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(2000);
        }
        catch { }
        ws?.Dispose();
        ws = null;
        OnlineUsers.Clear();
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(config.WebSocketUrl), token);
                log.Information("[CommsLink Voices] WebSocket connected");

                // Send auth handshake if we have a token
                if (!string.IsNullOrEmpty(config.AuthToken))
                {
                    var authMsg = JsonSerializer.Serialize(new { type = "auth", token = config.AuthToken, hearSelf = config.HearSelf });
                    var authBytes = Encoding.UTF8.GetBytes(authMsg);
                    await ws.SendAsync(new ArraySegment<byte>(authBytes), WebSocketMessageType.Text, true, token);
                    log.Information("[CommsLink Voices] WS auth handshake sent");
                }

                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    // 1. Read JSON header
                    var headerJson = await ReceiveString(token);
                    if (headerJson == null) break;

                    // Check message type
                    if (headerJson.Contains("\"type\":\"auth\""))
                    {
                        log.Information("[CommsLink Voices] WS auth response: {0}", headerJson);
                        // Send full hearing settings after auth
                        SendSettings();
                        continue;
                    }

                    if (headerJson.Contains("\"type\":\"settings\""))
                    {
                        log.Information("[CommsLink Voices] Settings confirmed: {0}", headerJson);
                        continue;
                    }

                    if (headerJson.Contains("\"type\":\"users\""))
                    {
                        ParseOnlineUsers(headerJson);
                        continue;
                    }

                    if (headerJson.Contains("\"type\":\"error\""))
                    {
                        log.Warning("[CommsLink Voices] WS error: {0}", headerJson);
                        continue;
                    }

                    // Parse audio format and distance from metadata header
                    var audioFormat = "wav";
                    float audioDistance = 0;
                    try
                    {
                        var metaDoc = JsonDocument.Parse(headerJson);
                        if (metaDoc.RootElement.TryGetProperty("format", out var fmtEl))
                            audioFormat = fmtEl.GetString() ?? "wav";
                        if (metaDoc.RootElement.TryGetProperty("distance", out var distEl))
                            audioDistance = distEl.GetSingle();
                    }
                    catch { }

                    // Read binary audio data
                    var audioData = await ReceiveBinary(token);
                    if (audioData == null) break;

                    PlayAudio(audioData, audioFormat, audioDistance);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.Warning("[CommsLink Voices] WS error: {0}. Reconnecting in 3s...", ex.Message);
                try { await Task.Delay(3000, token); } catch { break; }
            }
        }
    }

    private void ParseOnlineUsers(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("users", out var usersArr))
            {
                var users = new List<OnlineUser>();
                foreach (var u in usersArr.EnumerateArray())
                {
                    users.Add(new OnlineUser
                    {
                        UserId = u.TryGetProperty("userId", out var id) ? id.GetString() ?? "" : "",
                        Username = u.TryGetProperty("username", out var name) ? name.GetString() ?? "" : "",
                        CharName = u.TryGetProperty("charName", out var cn) ? cn.GetString() : null,
                        VoiceId = u.TryGetProperty("voiceId", out var v) ? v.GetString() : null,
                    });
                }
                OnlineUsers = users;
                log.Information("[CommsLink Voices] Online users: {0}", users.Count);
            }
        }
        catch (Exception ex)
        {
            log.Warning("[CommsLink Voices] Failed to parse users: {0}", ex.Message);
        }
    }

    /// <summary>Send all hearing settings to the server.</summary>
    public async void SendSettings()
    {
        if (!IsConnected) return;
        try
        {
            var msg = JsonSerializer.Serialize(new
            {
                type = "settings",
                hearSelf = config.HearSelf,
                hearAll = config.HearAll,
                muted = config.MutedUserIds,
                heard = config.HeardUserIds,
            });
            var bytes = Encoding.UTF8.GetBytes(msg);
            await ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
    }

    /// <summary>Send position update to server for proximity filtering.</summary>
    public async void SendPosition(uint zone, uint mapId, float x, float y, float z)
    {
        if (!IsConnected) return;
        try
        {
            var msg = JsonSerializer.Serialize(new
            {
                type = "pos",
                zone = (int)zone,
                mapId = (int)mapId,
                x,
                y,
                z,
            });
            var bytes = Encoding.UTF8.GetBytes(msg);
            await ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
    }

    /// <summary>Request online user list from WS.</summary>
    public async void RequestOnlineUsers()
    {
        if (!IsConnected) return;
        try
        {
            var msg = JsonSerializer.Serialize(new { type = "users" });
            var bytes = Encoding.UTF8.GetBytes(msg);
            await ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
    }

    private async Task<string?> ReceiveString(CancellationToken token)
    {
        var buffer = new byte[4096];
        var result = await ws!.ReceiveAsync(buffer, token);
        if (result.MessageType == WebSocketMessageType.Close) return null;
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private async Task<byte[]?> ReceiveBinary(CancellationToken token)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await ws!.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return ms.ToArray();
    }

    private void PlayAudio(byte[] audioData, string format, float distance)
    {
        try
        {
            using var ms = new MemoryStream(audioData);
            WaveStream reader = format == "mp3"
                ? new Mp3FileReader(ms)
                : new WaveFileReader(ms);

            using (reader)
            {
                // Inverse square volume attenuation based on distance
                // Reference distance = 5 yalms (full volume), max = 50 yalms
                const float refDist = 5f;
                const float maxDist = 50f;
                float distanceScale = 1f;
                if (distance > refDist)
                {
                    // Inverse square: (ref/dist)^2, clamped to a minimum so it doesn't go silent
                    distanceScale = (refDist * refDist) / (distance * distance);
                    distanceScale = Math.Max(distanceScale, 0.05f);
                }
                else if (distance > 0)
                {
                    distanceScale = 1f;
                }

                ISampleProvider sampleProvider = reader.ToSampleProvider();
                var volumeProvider = new NAudio.Wave.SampleProviders.VolumeSampleProvider(sampleProvider)
                {
                    Volume = volume * distanceScale,
                };

                using var outputDevice = new WaveOutEvent();
                outputDevice.Init(volumeProvider);
                outputDevice.Play();

                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(50);
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("[CommsLink Voices] Audio playback error ({0}): {1}", format, ex.Message);
        }
    }

    public void SetVolume(float vol)
    {
        volume = Math.Clamp(vol, 0f, 1f);
    }

    public void Dispose()
    {
        Disconnect();
        cts?.Dispose();
    }
}

public class OnlineUser
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? CharName { get; set; }
    public string? VoiceId { get; set; }
}
