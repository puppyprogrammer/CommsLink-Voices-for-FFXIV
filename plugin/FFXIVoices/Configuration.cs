using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace FFXIVoices;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    /// <summary>HTTP API base URL (includes /api/v1/ffxiv path).</summary>
    public string ServerUrl { get; set; } = "https://commslink.net/api/v1/ffxiv";

    /// <summary>WebSocket endpoint for receiving audio.</summary>
    public string WebSocketUrl { get; set; } = "wss://commslink.net/ws";

    /// <summary>Whether to process party chat.</summary>
    public bool EnablePartyChat { get; set; } = true;

    /// <summary>Whether to process say chat.</summary>
    public bool EnableSayChat { get; set; } = false;

    /// <summary>Audio volume (0.0 - 1.0).</summary>
    public float Volume { get; set; } = 0.8f;

    /// <summary>Stored auth credentials.</summary>
    public string? Username { get; set; }
    public string? AuthToken { get; set; }
    public string? CharName { get; set; }
    public string? ContentId { get; set; }

    /// <summary>Whether the user has accepted the data transmission notice.</summary>
    public bool HasAcceptedDataNotice { get; set; }
    public string? VoiceId { get; set; }

    /// <summary>Whether to process shout/yell chat.</summary>
    public bool EnableShoutChat { get; set; } = true;

    /// <summary>Whether to process whisper/tell chat.</summary>
    public bool EnableWhisperChat { get; set; } = true;

    /// <summary>Whether to hear your own TTS messages.</summary>
    public bool HearSelf { get; set; } = true;

    /// <summary>Whether to hear all users by default (true) or only selected users (false).</summary>
    public bool HearAll { get; set; } = true;

    /// <summary>User IDs explicitly muted (used when HearAll=true).</summary>
    public List<string> MutedUserIds { get; set; } = new();

    /// <summary>User IDs explicitly enabled (used when HearAll=false).</summary>
    public List<string> HeardUserIds { get; set; } = new();
}
