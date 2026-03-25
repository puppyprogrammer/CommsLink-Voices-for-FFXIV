using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace FFXIVoices;

public sealed class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly Action saveConfig;

    private string serverUrlInput;
    private string wsUrlInput;
    private bool saved;

    public ConfigWindow(Configuration config, Action saveConfig)
        : base("CommsLink Voices Settings##Config", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar)
    {
        this.config = config;
        this.saveConfig = saveConfig;

        serverUrlInput = config.ServerUrl;
        wsUrlInput = config.WebSocketUrl;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 260),
            MaximumSize = new Vector2(450, 400),
        };
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
        ImGui.Spacing();

        var contentWidth = ImGui.GetContentRegionAvail().X;

        // ── Server Config Card ──
        CommsLinkTheme.SectionHeader("Server Configuration");

        CommsLinkTheme.BeginCard();

        ImGui.TextColored(CommsLinkTheme.TextSecondary, "HTTP Server URL");
        ImGui.SetNextItemWidth(contentWidth - 40);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CommsLinkTheme.BgBase);
        ImGui.InputText("##serverUrl", ref serverUrlInput, 512);
        ImGui.PopStyleColor();

        ImGui.Spacing();

        ImGui.TextColored(CommsLinkTheme.TextSecondary, "WebSocket URL");
        ImGui.SetNextItemWidth(contentWidth - 40);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CommsLinkTheme.BgBase);
        ImGui.InputText("##wsUrl", ref wsUrlInput, 512);
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Spacing();

        var buttonWidth = (contentWidth - 56) / 3;

        if (CommsLinkTheme.CyanButton("Save", new Vector2(buttonWidth, 26)))
        {
            config.ServerUrl = serverUrlInput;
            config.WebSocketUrl = wsUrlInput;
            saveConfig();
            saved = true;
        }

        ImGui.SameLine();
        if (CommsLinkTheme.OutlineButton("Use Test", new Vector2(buttonWidth, 26)))
        {
            serverUrlInput = "http://3.134.145.169:4000/api/v1/ffxiv";
            wsUrlInput = "ws://3.134.145.169:8080";
            saved = false;
        }

        ImGui.SameLine();
        if (CommsLinkTheme.OutlineButton("Use Prod", new Vector2(buttonWidth, 26)))
        {
            serverUrlInput = "https://commslink.net/api/v1/ffxiv";
            wsUrlInput = "ws://3.134.145.169:8080";
            saved = false;
        }

        if (saved)
        {
            ImGui.Spacing();
            ImGui.TextColored(CommsLinkTheme.Success, "Saved. Changes take effect on next connect.");
        }

        CommsLinkTheme.EndCard();

        // Footer
        ImGui.Spacing();
        ImGui.TextColored(CommsLinkTheme.TextMuted, "Changes to server URLs require reconnecting.");

        CommsLinkTheme.PopVars(varCount);
        CommsLinkTheme.PopStyle(colorCount);
    }
}
