using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace FFXIVoices;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly ITextureProvider textureProvider;
    private readonly Configuration config;
    private readonly AuthClient authClient;
    private readonly ApiClient apiClient;
    private readonly ChatHandler chatHandler;
    private readonly AudioPlayer audioPlayer;
    private readonly UpdateChecker updateChecker;

    private readonly WindowSystem windowSystem = new("CommsLinkVoices");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IChatGui chatGui,
        IObjectTable objectTable,
        IClientState clientState,
        ICommandManager commandManager,
        ITextureProvider textureProvider,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.log = log;
        this.textureProvider = textureProvider;

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Auto-updater: apply any previously staged update, then check for new ones
        updateChecker = new UpdateChecker(config, pluginInterface, log, chatGui);
        updateChecker.ApplyStagedUpdate();

        authClient = new AuthClient(config, log);
        apiClient = new ApiClient(config, log);
        chatHandler = new ChatHandler(chatGui, objectTable, clientState, config, authClient, log);
        audioPlayer = new AudioPlayer(config, log);

        // Build UI windows
        mainWindow = new MainWindow(
            config, authClient, audioPlayer, apiClient, updateChecker, clientState,
            SaveConfig,
            (email, pass) => HandleLogin(email, pass),
            (email, pass) => HandleRegister(email, pass),
            () =>
            {
                authClient.Logout();
                audioPlayer.Disconnect();
                SaveConfig();
                chatGui.Print("[CommsLink Voices] Logged out");
            },
            () => configWindow.Toggle());

        configWindow = new ConfigWindow(config, SaveConfig);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        // Load logo from embedded resource
        CommsLinkTheme.LogoTexture = textureProvider.GetFromManifestResource(
            Assembly.GetExecutingAssembly(), "FFXIVoices.Resources.logo.png");

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;

        // Auto-connect WS if we have a saved token
        if (authClient.IsLoggedIn)
        {
            audioPlayer.Connect();
        }

        commandManager.AddHandler("/ffxivoices", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "/ffxivoices [login|register|logout|on|off|status|volume|server] — CommsLink Voices TTS",
        });

        var status = authClient.IsLoggedIn ? $"Logged in as {config.Username}" : "Not logged in. Use /ffxivoices login <username> <password>";
        chatGui.Print($"[CommsLink Voices] Loaded! Server: {config.ServerUrl} | {status}");

        // Check for plugin updates in background
        _ = updateChecker.CheckForUpdateAsync();
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            mainWindow.Toggle();
            return;
        }

        switch (parts[0].ToLower())
        {
            case "register" when parts.Length >= 3:
                HandleRegister(parts[1], parts[2]);
                break;

            case "login" when parts.Length >= 3:
                HandleLogin(parts[1], parts[2]);
                break;

            case "logout":
                authClient.Logout();
                audioPlayer.Disconnect();
                SaveConfig();
                chatGui.Print("[CommsLink Voices] Logged out");
                break;

            case "on":
                if (!authClient.IsLoggedIn)
                {
                    chatGui.Print("[CommsLink Voices] Login first: /ffxivoices login <username> <password>");
                    break;
                }
                config.EnablePartyChat = true;
                audioPlayer.Connect();
                SaveConfig();
                chatGui.Print("[CommsLink Voices] Enabled");
                break;

            case "off":
                config.EnablePartyChat = false;
                audioPlayer.Disconnect();
                SaveConfig();
                chatGui.Print("[CommsLink Voices] Disabled");
                break;

            case "volume" when parts.Length > 1 && int.TryParse(parts[1], out var vol):
                audioPlayer.SetVolume(vol / 100f);
                config.Volume = vol / 100f;
                SaveConfig();
                chatGui.Print($"[CommsLink Voices] Volume: {vol}%");
                break;

            case "server" when parts.Length > 1:
                config.ServerUrl = parts[1];
                SaveConfig();
                chatGui.Print($"[CommsLink Voices] Server: {config.ServerUrl}");
                break;

            case "config":
            case "settings":
                configWindow.Toggle();
                break;

            case "status":
                PrintStatus();
                break;

            case "debug":
                PrintDebug();
                break;

            default:
                chatGui.Print("[CommsLink Voices] Usage: /ffxivoices [login|register|logout|on|off|status|volume|server|config]");
                break;
        }
    }

    private void PrintStatus()
    {
        var auth = authClient.IsLoggedIn ? $"LoggedIn({config.Username})" : "NotLoggedIn";
        chatGui.Print(
            $"[CommsLink Voices] Auth={auth} Party={config.EnablePartyChat} Say={config.EnableSayChat} " +
            $"WS={(audioPlayer.IsConnected ? "connected" : "disconnected")} Vol={config.Volume:P0}");
    }

    private void PrintDebug()
    {
        chatGui.Print($"[DEBUG] Username: {config.Username ?? "null"}");
        chatGui.Print($"[DEBUG] CharName: {config.CharName ?? "null"}");
        chatGui.Print($"[DEBUG] VoiceId: {config.VoiceId ?? "null"}");
        chatGui.Print($"[DEBUG] Token: {(string.IsNullOrEmpty(config.AuthToken) ? "null" : config.AuthToken[..20] + "...")}");
        chatGui.Print($"[DEBUG] ServerUrl: {config.ServerUrl}");
        chatGui.Print($"[DEBUG] WsUrl: {config.WebSocketUrl}");
        chatGui.Print($"[DEBUG] WS Connected: {audioPlayer.IsConnected}");
        chatGui.Print($"[DEBUG] Voices loaded: {apiClient.VoicesLoaded} count={apiClient.Voices.Count}");
        chatGui.Print($"[DEBUG] Online users: {audioPlayer.OnlineUsers.Count}");
        foreach (var v in apiClient.Voices)
            chatGui.Print($"[DEBUG]   Voice: {v.VoiceId} = {v.Name}");
        foreach (var u in audioPlayer.OnlineUsers)
            chatGui.Print($"[DEBUG]   User: {u.Username} ({u.CharName}) voice={u.VoiceId}");
    }

    private async void HandleRegister(string email, string password)
    {
        mainWindow.SetStatus("Registering...");
        chatGui.Print("[CommsLink Voices] Registering...");

        string? contentId = null;
        string? charName = null;
        string? gender = null;
        var player = clientState.LocalPlayer;
        if (player != null)
        {
            contentId = HashContentId(player.GameObjectId.ToString());
            charName = player.Name.TextValue;
            gender = player.Customize[(int)CustomizeIndex.Gender] == 0 ? "male" : "female";
            config.CharName = charName;
            config.ContentId = contentId;
        }

        var result = await authClient.RegisterAsync(email, password, contentId, charName, gender);
        if (result.Success)
        {
            SaveConfig();
            audioPlayer.Connect();
            mainWindow.SetStatus(result.Message);
            chatGui.Print($"[CommsLink Voices] {result.Message}! WS connecting...");
        }
        else
        {
            mainWindow.SetStatus($"Error: {result.Message}");
            chatGui.Print($"[CommsLink Voices] Register failed: {result.Message}");
        }
    }

    private async void HandleLogin(string email, string password)
    {
        mainWindow.SetStatus("Logging in...");
        chatGui.Print("[CommsLink Voices] Logging in...");

        string? contentId = null;
        string? charName = null;
        string? gender = null;
        var player = clientState.LocalPlayer;
        if (player != null)
        {
            contentId = HashContentId(player.GameObjectId.ToString());
            charName = player.Name.TextValue;
            gender = player.Customize[(int)CustomizeIndex.Gender] == 0 ? "male" : "female";
            config.CharName = charName;
            config.ContentId = contentId;
        }

        var result = await authClient.LoginAsync(email, password, contentId, charName, gender);
        if (result.Success)
        {
            SaveConfig();
            audioPlayer.Connect();
            mainWindow.SetStatus(result.Message);
            chatGui.Print($"[CommsLink Voices] {result.Message}! WS connecting...");
        }
        else
        {
            mainWindow.SetStatus($"Error: {result.Message}");
            chatGui.Print($"[CommsLink Voices] Login failed: {result.Message}");
        }
    }

    private static string HashContentId(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void OnOpenConfigUi()
    {
        configWindow.Toggle();
    }

    private void OnOpenMainUi()
    {
        mainWindow.Toggle();
    }

    private void SaveConfig()
    {
        pluginInterface.SavePluginConfig(config);
    }

    public void Dispose()
    {
        CommsLinkTheme.LogoTexture = null;
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
        windowSystem.RemoveAllWindows();
        commandManager.RemoveHandler("/ffxivoices");
        chatHandler.Dispose();
        audioPlayer.Dispose();
        apiClient.Dispose();
        authClient.Dispose();
        updateChecker.Dispose();
    }
}
