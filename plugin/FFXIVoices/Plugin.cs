using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;

namespace FFXIVoices;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly ChatHandler chatHandler;
    private readonly AudioPlayer audioPlayer;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IChatGui chatGui,
        IObjectTable objectTable,
        ICommandManager commandManager,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.log = log;

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        chatHandler = new ChatHandler(chatGui, objectTable, config, log);
        audioPlayer = new AudioPlayer(config, log);

        audioPlayer.Connect();

        commandManager.AddHandler("/ffxivoices", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "FFXIVoices settings. /ffxivoices [on|off|status|volume 0-100]",
        });

        chatGui.Print("[FFXIVoices] Loaded! Server: " + config.ServerUrl);
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            chatGui.Print("[FFXIVoices] Status: WS=" + (audioPlayer.IsConnected ? "connected" : "disconnected"));
            return;
        }

        switch (parts[0])
        {
            case "on":
                config.EnablePartyChat = true;
                audioPlayer.Connect();
                chatGui.Print("[FFXIVoices] Enabled");
                break;
            case "off":
                config.EnablePartyChat = false;
                audioPlayer.Disconnect();
                chatGui.Print("[FFXIVoices] Disabled");
                break;
            case "volume" when parts.Length > 1 && int.TryParse(parts[1], out var vol):
                audioPlayer.SetVolume(vol / 100f);
                config.Volume = vol / 100f;
                chatGui.Print($"[FFXIVoices] Volume: {vol}%");
                break;
            case "status":
                chatGui.Print(
                    $"[FFXIVoices] Party={config.EnablePartyChat} Say={config.EnableSayChat} WS={(audioPlayer.IsConnected ? "connected" : "disconnected")} Vol={config.Volume:P0}");
                break;
        }

        pluginInterface.SavePluginConfig(config);
    }

    public void Dispose()
    {
        commandManager.RemoveHandler("/ffxivoices");
        chatHandler.Dispose();
        audioPlayer.Dispose();
    }
}
