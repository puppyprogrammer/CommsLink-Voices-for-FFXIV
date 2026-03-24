using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FFXIVoices;

public sealed class ChatHandler : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly IObjectTable objectTable;
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly AuthClient authClient;
    private readonly IPluginLog log;

    public ChatHandler(IChatGui chatGui, IObjectTable objectTable, IClientState clientState,
        Configuration config, AuthClient authClient, IPluginLog log)
    {
        this.chatGui = chatGui;
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.config = config;
        this.authClient = authClient;
        this.log = log;

        this.chatGui.ChatMessage += OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!authClient.IsLoggedIn) return;

        bool isParty = type == XivChatType.Party || type == XivChatType.CrossParty;
        bool isSay = type == XivChatType.Say;
        bool isShout = type == XivChatType.Shout || type == XivChatType.Yell;
        bool isWhisper = type == XivChatType.TellOutgoing;

        if (isParty && !config.EnablePartyChat) return;
        if (isSay && !config.EnableSayChat) return;
        if (isShout && !config.EnableShoutChat) return;
        if (isWhisper && !config.EnableWhisperChat) return;
        if (!isParty && !isSay && !isShout && !isWhisper) return;

        var senderName = sender.TextValue;
        var messageText = message.TextValue;

        if (string.IsNullOrWhiteSpace(senderName) || string.IsNullOrWhiteSpace(messageText))
            return;

        // Only send our own messages — other plugin users send their own
        var localPlayer = clientState.LocalPlayer;
        if (localPlayer == null) return;

        var localName = localPlayer.Name.TextValue;
        if (!senderName.Contains(localName, StringComparison.OrdinalIgnoreCase))
            return;

        // Auto-sync character name if not set
        if (config.CharName != localName)
        {
            config.CharName = localName;
        }

        float x = localPlayer.Position.X;
        float y = localPlayer.Position.Y;
        float z = localPlayer.Position.Z;
        uint zone = clientState.TerritoryType;
        uint mapId = clientState.MapId;

        _ = SendToServerAsync(messageText, zone, mapId, x, y, z);
    }

    private async Task SendToServerAsync(string message, uint zone, uint mapId, float x, float y, float z)
    {
        try
        {
            using var client = authClient.GetAuthedClient();
            var payload = JsonSerializer.Serialize(new
            {
                message,
                zone = (int)zone,
                mapId = (int)mapId,
                x,
                y,
                z,
            });

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{config.ServerUrl}/chat", content);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                log.Warning("[CommsLink Voices] Chat POST {0}: {1}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            log.Error("[CommsLink Voices] Failed to send chat: {0}", ex.Message);
        }
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
    }
}
