using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Dalamud.Plugin.Services;

namespace FFXIVoices;

public sealed class MainWindow : Window
{
    private readonly Configuration config;
    private readonly AuthClient authClient;
    private readonly AudioPlayer audioPlayer;
    private readonly ApiClient apiClient;
    private readonly UpdateChecker updateChecker;
    private readonly IClientState clientState;
    private readonly Action saveConfig;
    private readonly Action<string, string> onLogin;
    private readonly Action<string, string> onRegister;
    private readonly Action onLogout;
    private readonly Action onOpenSettings;

    private string usernameInput = string.Empty;
    private string passwordInput = string.Empty;
    private string statusMessage = string.Empty;
    private bool statusIsError;
    private bool isRegistering;
    private int selectedVoiceIdx = -1;
    private float refreshTimer;
    private bool voicesFetched;

    public MainWindow(
        Configuration config,
        AuthClient authClient,
        AudioPlayer audioPlayer,
        ApiClient apiClient,
        UpdateChecker updateChecker,
        IClientState clientState,
        Action saveConfig,
        Action<string, string> onLogin,
        Action<string, string> onRegister,
        Action onLogout,
        Action onOpenSettings)
        : base("CommsLink Voices##Main", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysVerticalScrollbar)
    {
        this.config = config;
        this.authClient = authClient;
        this.audioPlayer = audioPlayer;
        this.apiClient = apiClient;
        this.updateChecker = updateChecker;
        this.clientState = clientState;
        this.saveConfig = saveConfig;
        this.onLogin = onLogin;
        this.onRegister = onRegister;
        this.onLogout = onLogout;
        this.onOpenSettings = onOpenSettings;


        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 550),
            MaximumSize = new Vector2(440, 900),
        };
    }

    public void SetStatus(string msg, bool isError = false)
    {
        statusMessage = msg;
        statusIsError = isError;
    }

    public override void PreDraw()
    {
        ImGui.PushStyleColor(ImGuiCol.TitleBg, CommsLinkTheme.BgSecondary);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, CommsLinkTheme.BgSecondary);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 2f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(1);
        ImGui.PopStyleColor(2);
    }

    public override void Draw()
    {
        var colorCount = CommsLinkTheme.PushStyle();
        var varCount = CommsLinkTheme.PushVars();

        CommsLinkTheme.DrawHeader();

        // User info in header bar (only when logged in)
        if (authClient.IsLoggedIn)
        {
            ImGui.SameLine();
            var connDot = audioPlayer.IsConnected ? CommsLinkTheme.Success : CommsLinkTheme.Error;
            ImGui.TextColored(connDot, "\u25CF");
            ImGui.SameLine();
            ImGui.TextColored(CommsLinkTheme.TextBright, config.Username ?? "");
            if (!string.IsNullOrEmpty(config.CharName))
            {
                ImGui.SameLine();
                ImGui.TextColored(CommsLinkTheme.TextMuted, "//");
                ImGui.SameLine();
                ImGui.TextColored(CommsLinkTheme.PrimaryLight, config.CharName);
            }

            // Right-aligned profile + logout icon buttons
            var headerRight = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX();
            ImGui.SameLine();
            ImGui.SetCursorPosX(headerRight - 72);
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, CommsLinkTheme.WithAlpha(CommsLinkTheme.TextPrimary, 0.1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, CommsLinkTheme.WithAlpha(CommsLinkTheme.TextPrimary, 0.2f));
            if (ImGuiComponents.IconButton("header_profile", FontAwesomeIcon.UserCircle))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = "https://commslink.net/profile", UseShellExecute = true }); }
                catch { }
            }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.Text("Profile"); ImGui.EndTooltip(); }
            ImGui.SameLine();
            if (ImGuiComponents.IconButton("header_logout", FontAwesomeIcon.SignOutAlt))
            {
                onLogout();
                usernameInput = string.Empty;
                passwordInput = string.Empty;
                voicesFetched = false;
            }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.Text("Logout"); ImGui.EndTooltip(); }
            ImGui.PopStyleColor(3);
        }

        ImGui.Spacing();

        // Update notification banner
        if (updateChecker.UpdatePending && updateChecker.NewVersion != null)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, CommsLinkTheme.WithAlpha(CommsLinkTheme.Purple, 0.2f));
            ImGui.BeginChild("update_banner", new Vector2(ImGui.GetContentRegionAvail().X, 28), false);
            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(8, 4));
            ImGui.TextColored(CommsLinkTheme.PurpleAccent, $"Update v{updateChecker.NewVersion} ready — reload plugin to apply");
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        if (authClient.IsLoggedIn)
        {
            if (!voicesFetched)
            {
                voicesFetched = true;
                _ = apiClient.FetchVoicesAsync();
                _ = apiClient.FetchCreditsAsync();
                audioPlayer.RequestOnlineUsers();
                SyncVoiceSelection();
            }

            // Clear stale "Connecting..." once actually connected
            if (audioPlayer.IsConnected && statusMessage == "Connecting...")
                statusMessage = string.Empty;

            refreshTimer += ImGui.GetIO().DeltaTime;
            if (refreshTimer > 30f)
            {
                refreshTimer = 0;
                audioPlayer.RequestOnlineUsers();
                _ = apiClient.FetchCreditsAsync();

                // Send position update for proximity filtering
                var lp = clientState.LocalPlayer;
                if (lp != null)
                    audioPlayer.SendPosition(clientState.TerritoryType, clientState.MapId,
                        lp.Position.X, lp.Position.Y, lp.Position.Z);
            }

            DrawDashboard();
        }
        else
        {
            voicesFetched = false;
            DrawAuthView();
        }

        // Footer
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Separator, CommsLinkTheme.WithAlpha(CommsLinkTheme.Border, 0.5f));
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.TextColored(CommsLinkTheme.TextMuted, "CommsLink Voices v0.7.0 — commslink.net");

        CommsLinkTheme.PopVars(varCount);
        CommsLinkTheme.PopStyle(colorCount);
    }

    private void SyncVoiceSelection()
    {
        if (!string.IsNullOrEmpty(config.VoiceId) && apiClient.Voices.Count > 0)
        {
            for (int i = 0; i < apiClient.Voices.Count; i++)
            {
                if (apiClient.Voices[i].VoiceId == config.VoiceId)
                {
                    selectedVoiceIdx = i;
                    return;
                }
            }
        }
        selectedVoiceIdx = -1;
    }

    private void DrawAuthView()
    {
        var contentWidth = ImGui.GetContentRegionAvail().X;

        // Data consent notice (must accept before login)
        if (!config.HasAcceptedDataNotice)
        {
            CommsLinkTheme.BeginCard();
            ImGui.TextColored(CommsLinkTheme.TextBright, "Data Notice");
            ImGui.Spacing();
            ImGui.TextWrapped("CommsLink Voices sends your chat messages to commslink.net for TTS generation. " +
                "Your character position is sent periodically for proximity-based audio. " +
                "Character IDs are hashed before transmission. No data is shared with third parties.");
            ImGui.Spacing();
            ImGui.TextWrapped("By continuing, you consent to this data being transmitted to the CommsLink server.");
            ImGui.Spacing();
            if (CommsLinkTheme.CyanButton("I Understand & Accept", new Vector2(contentWidth - 8, 28)))
            {
                config.HasAcceptedDataNotice = true;
                saveConfig();
            }
            CommsLinkTheme.EndCard();
            return;
        }

        CommsLinkTheme.BeginCard();

        ImGui.TextColored(CommsLinkTheme.TextBright,
            isRegistering ? "Create Account" : "Sign In");
        ImGui.Spacing();
        ImGui.TextColored(CommsLinkTheme.TextSecondary,
            isRegistering
                ? "Register to start using CommsLink Voices."
                : "Log in with your CommsLink account.");

        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.TextColored(CommsLinkTheme.TextSecondary, "Username");
        ImGui.SetNextItemWidth(contentWidth - 40);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CommsLinkTheme.BgBase);
        ImGui.InputText("##username", ref usernameInput, 256);
        ImGui.PopStyleColor();

        ImGui.Spacing();

        ImGui.TextColored(CommsLinkTheme.TextSecondary, "Password");
        ImGui.SetNextItemWidth(contentWidth - 40);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CommsLinkTheme.BgBase);
        ImGui.InputText("##password", ref passwordInput, 256, ImGuiInputTextFlags.Password);
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Spacing();

        var buttonWidth = (contentWidth - 50) / 2;
        if (isRegistering)
        {
            if (CommsLinkTheme.CyanButton("Register", new Vector2(buttonWidth, 28)))
            {
                if (!string.IsNullOrWhiteSpace(usernameInput) && !string.IsNullOrWhiteSpace(passwordInput))
                    onRegister(usernameInput, passwordInput);
            }
            ImGui.SameLine();
            if (CommsLinkTheme.OutlineButton("Back to Login", new Vector2(buttonWidth, 28)))
            {
                isRegistering = false;
                statusMessage = string.Empty;
            }
        }
        else
        {
            if (CommsLinkTheme.CyanButton("Login", new Vector2(buttonWidth, 28)))
            {
                if (!string.IsNullOrWhiteSpace(usernameInput) && !string.IsNullOrWhiteSpace(passwordInput))
                    onLogin(usernameInput, passwordInput);
            }
            ImGui.SameLine();
            if (CommsLinkTheme.OutlineButton("Create Account", new Vector2(buttonWidth, 28)))
            {
                isRegistering = true;
                statusMessage = string.Empty;
            }
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.Spacing();
            var color = statusIsError ? CommsLinkTheme.Error : CommsLinkTheme.Success;
            ImGui.TextColored(color, statusMessage);
        }

        CommsLinkTheme.EndCard();
    }

    private void DrawDashboard()
    {
        var contentWidth = ImGui.GetContentRegionAvail().X;

        // ── Credits ──
        CommsLinkTheme.BeginCard();

        // Credits display
        if (apiClient.Credits >= 0)
        {
            var credColor = apiClient.Credits > 1000 ? CommsLinkTheme.Cyan
                : apiClient.Credits > 0 ? CommsLinkTheme.Warning
                : CommsLinkTheme.Error;
            ImGui.TextColored(CommsLinkTheme.TextSecondary, "Credits:");
            ImGui.SameLine();
            ImGui.TextColored(credColor, $"{apiClient.Credits:N0}");

            // Next free credits countdown
            if (apiClient.NextFreeCredits.HasValue)
            {
                var remaining = apiClient.NextFreeCredits.Value - DateTime.UtcNow;
                string refreshLabel;
                if (remaining.TotalSeconds <= 0)
                    refreshLabel = "now";
                else if (remaining.TotalHours < 1)
                    refreshLabel = $"{(int)remaining.TotalMinutes}m";
                else if (remaining.TotalDays < 1)
                    refreshLabel = $"{(int)remaining.TotalHours}h";
                else
                    refreshLabel = $"{(int)remaining.TotalDays}d";

                ImGui.SameLine();
                ImGui.TextColored(CommsLinkTheme.TextMuted, $"(+1k in {refreshLabel})");
            }

            // Credit explanation
            ImGui.TextColored(CommsLinkTheme.TextSecondary, "1,000 free credits monthly.");
            ImGui.TextColored(CommsLinkTheme.TextSecondary, "Standard voices ~1cr/msg.");
            ImGui.TextColored(CommsLinkTheme.Purple, "Donor voices ~18cr/50 chars.");
            ImGui.Spacing();
            if (CommsLinkTheme.CyanButton("Get More Credits", new Vector2(ImGui.GetContentRegionAvail().X, 24)))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = "https://commslink.net/credits", UseShellExecute = true }); }
                catch { }
            }
        }

        CommsLinkTheme.EndCard();

        // ── Profile ──
        CommsLinkTheme.SectionHeader("Profile");
        CommsLinkTheme.BeginCard();

        // Voice selection
        ImGui.TextColored(CommsLinkTheme.TextSecondary, "Voice");
        if (apiClient.VoicesLoaded && apiClient.Voices.Count > 0)
        {
            if (selectedVoiceIdx < 0)
                SyncVoiceSelection();

            ImGui.SetNextItemWidth(contentWidth - 40);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, CommsLinkTheme.BgBase);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, CommsLinkTheme.BgCard);

            var currentLabel = selectedVoiceIdx >= 0 && selectedVoiceIdx < apiClient.Voices.Count
                ? apiClient.Voices[selectedVoiceIdx].Name
                : config.VoiceId ?? "Select a voice...";

            if (ImGui.BeginCombo("##voicesel", currentLabel))
            {
                for (int i = 0; i < apiClient.Voices.Count; i++)
                {
                    var voice = apiClient.Voices[i];
                    var isSelected = i == selectedVoiceIdx;
                    var label = voice.CreditCost > 0
                        ? $"{voice.Name}  ({voice.CreditCost}cr)"
                        : voice.Name;
                    var isEL = voice.Provider == "elevenlabs";
                    if (isEL)
                        ImGui.PushStyleColor(ImGuiCol.Text, CommsLinkTheme.Purple);
                    if (ImGui.Selectable(label, isSelected))
                    {
                        selectedVoiceIdx = i;
                        config.VoiceId = voice.VoiceId;
                        saveConfig();
                        SetStatus($"Setting voice to {voice.Name}...");
                        var vid = voice.VoiceId;
                        var vname = voice.Name;
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            var err = await apiClient.SelectVoiceAsync(vid);
                            if (err != null)
                                SetStatus($"Voice error: {err}", true);
                            else
                                SetStatus($"Voice: {vname}");
                        });
                    }
                    if (isEL)
                        ImGui.PopStyleColor();
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopStyleColor(2);
        }
        else
        {
            ImGui.TextColored(CommsLinkTheme.TextMuted, "Loading voices...");
            ImGui.SameLine();
            if (CommsLinkTheme.OutlineButton("Retry##voices", new Vector2(50, 0)))
                _ = apiClient.FetchVoicesAsync();
        }

        CommsLinkTheme.EndCard();

        // ── TTS Controls ──
        CommsLinkTheme.SectionHeader("TTS Controls");
        CommsLinkTheme.BeginCard();

        var col = contentWidth / 3;

        var partyChat = config.EnablePartyChat;
        if (ImGui.Checkbox("Party", ref partyChat))
        { config.EnablePartyChat = partyChat; saveConfig(); }
        ImGui.SameLine(); ImGui.SetCursorPosX(ImGui.GetCursorPosX() + col - 80);

        var sayChat = config.EnableSayChat;
        if (ImGui.Checkbox("Say", ref sayChat))
        { config.EnableSayChat = sayChat; saveConfig(); }
        ImGui.SameLine(); ImGui.SetCursorPosX(ImGui.GetCursorPosX() + col - 80);

        var shoutChat = config.EnableShoutChat;
        if (ImGui.Checkbox("Shout/Yell", ref shoutChat))
        { config.EnableShoutChat = shoutChat; saveConfig(); }

        var whisperChat = config.EnableWhisperChat;
        if (ImGui.Checkbox("Whisper", ref whisperChat))
        { config.EnableWhisperChat = whisperChat; saveConfig(); }
        ImGui.SameLine(); ImGui.SetCursorPosX(ImGui.GetCursorPosX() + col - 80);

        var hearSelf = config.HearSelf;
        if (ImGui.Checkbox("Hear Self", ref hearSelf))
        { config.HearSelf = hearSelf; saveConfig(); audioPlayer.SendSettings(); }

        ImGui.Spacing();

        ImGui.TextColored(CommsLinkTheme.TextSecondary, "Volume");
        var volume = (int)(config.Volume * 100);
        ImGui.SetNextItemWidth(contentWidth - 40);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CommsLinkTheme.BgBase);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, CommsLinkTheme.Cyan);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, CommsLinkTheme.CyanHover);
        if (ImGui.SliderInt("##volume", ref volume, 0, 100, "%d%%"))
        {
            config.Volume = volume / 100f;
            audioPlayer.SetVolume(config.Volume);
            saveConfig();
        }
        ImGui.PopStyleColor(3);

        CommsLinkTheme.EndCard();

        // ── Action Bar ──
        var btnWidth = (contentWidth - 8) / 2;

        if (audioPlayer.IsConnected)
        {
            if (CommsLinkTheme.DangerButton("Disconnect", new Vector2(btnWidth, 26)))
            {
                audioPlayer.Disconnect();
                SetStatus("Disconnected");
            }
        }
        else
        {
            if (CommsLinkTheme.CyanButton("Connect", new Vector2(btnWidth, 26)))
            {
                audioPlayer.Connect();
                SetStatus("Connecting...");
            }
        }
        ImGui.SameLine();
        if (CommsLinkTheme.OutlineButton("Settings", new Vector2(btnWidth, 26)))
            onOpenSettings();

        // Status message
        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.Spacing();
            var color = statusIsError ? CommsLinkTheme.Error : CommsLinkTheme.Cyan;
            ImGui.TextColored(color, statusMessage);
        }
    }
}
