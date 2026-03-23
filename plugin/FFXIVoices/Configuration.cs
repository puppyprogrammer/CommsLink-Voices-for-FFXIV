using Dalamud.Configuration;
using System;

namespace FFXIVoices;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    /// <summary>HTTP endpoint for the TTS server.</summary>
    public string ServerUrl { get; set; } = "http://localhost:3000";

    /// <summary>WebSocket endpoint for receiving audio.</summary>
    public string WebSocketUrl { get; set; } = "ws://localhost:8080";

    /// <summary>Whether to process party chat.</summary>
    public bool EnablePartyChat { get; set; } = true;

    /// <summary>Whether to process say chat.</summary>
    public bool EnableSayChat { get; set; } = false;

    /// <summary>Audio volume (0.0 - 1.0).</summary>
    public float Volume { get; set; } = 0.8f;
}
