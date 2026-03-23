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
    private readonly HttpClient httpClient;
    private readonly Configuration config;
    private readonly IPluginLog log;

    public ChatHandler(IChatGui chatGui, IObjectTable objectTable, Configuration config, IPluginLog log)
    {
        this.chatGui = chatGui;
        this.objectTable = objectTable;
        this.config = config;
        this.log = log;
        this.httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        this.chatGui.ChatMessage += OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        bool isParty = type == XivChatType.Party || type == XivChatType.CrossParty;
        bool isSay = type == XivChatType.Say;

        if (isParty && !config.EnablePartyChat) return;
        if (isSay && !config.EnableSayChat) return;
        if (!isParty && !isSay) return;

        var senderName = sender.TextValue;
        var messageText = message.TextValue;

        if (string.IsNullOrWhiteSpace(senderName) || string.IsNullOrWhiteSpace(messageText))
            return;

        // Resolve GameObjectId as the best unique ID available in Dalamud v14
        ulong objectId = 0;
        var playerChar = objectTable
            .OfType<IPlayerCharacter>()
            .FirstOrDefault(pc => pc.Name.TextValue == senderName);
        if (playerChar != null)
            objectId = playerChar.GameObjectId;

        _ = SendToServerAsync(senderName, objectId.ToString(), messageText);
    }

    private async Task SendToServerAsync(string playerName, string contentId, string message)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                playerName,
                contentId,
                message,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync($"{config.ServerUrl}/chat", content);

            if (!response.IsSuccessStatusCode)
            {
                log.Warning("[FFXIVoices] Server returned {0} for message from {1}", response.StatusCode, playerName);
            }
        }
        catch (Exception ex)
        {
            log.Error("[FFXIVoices] Failed to send chat: {0}", ex.Message);
        }
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
        httpClient.Dispose();
    }
}
