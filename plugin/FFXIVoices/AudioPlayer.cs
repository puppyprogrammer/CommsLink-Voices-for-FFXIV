using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
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
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(config.WebSocketUrl), token);
                log.Information("[FFXIVoices] WebSocket connected");

                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    // 1. Read JSON header
                    var headerJson = await ReceiveString(token);
                    if (headerJson == null) break;

                    // 2. Read binary WAV data
                    var wavData = await ReceiveBinary(token);
                    if (wavData == null) break;

                    // 3. Play audio
                    PlayWav(wavData);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.Warning("[FFXIVoices] WS error: {0}. Reconnecting in 3s...", ex.Message);
                try { await Task.Delay(3000, token); } catch { break; }
            }
        }
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

    private void PlayWav(byte[] wavData)
    {
        try
        {
            using var ms = new MemoryStream(wavData);
            using var reader = new WaveFileReader(ms);
            var volumeProvider = new VolumeWaveProvider16(reader) { Volume = volume };
            using var outputDevice = new WaveOutEvent();
            outputDevice.Init(volumeProvider);
            outputDevice.Play();

            while (outputDevice.PlaybackState == PlaybackState.Playing)
            {
                Thread.Sleep(50);
            }
        }
        catch (Exception ex)
        {
            log.Error("[FFXIVoices] Audio playback error: {0}", ex.Message);
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
