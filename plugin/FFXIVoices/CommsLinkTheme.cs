using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

namespace FFXIVoices;

/// <summary>CommsLink design system colors and helpers for ImGui.</summary>
public static class CommsLinkTheme
{
    /// <summary>Shared logo texture loaded from embedded resource. Set by Plugin on init.</summary>
    public static ISharedImmediateTexture? LogoTexture { get; set; }
    // Backgrounds
    public static readonly Vector4 BgBase = FromHex(0x0d1117);
    public static readonly Vector4 BgSecondary = FromHex(0x0a0e14);
    public static readonly Vector4 BgCard = FromHex(0x161b22);
    public static readonly Vector4 BgSurface = FromHex(0x1c2128);
    public static readonly Vector4 BgInput = FromHex(0x161b22);

    // Primary
    public static readonly Vector4 Primary = FromHex(0x569cd6);
    public static readonly Vector4 PrimaryDark = FromHex(0x4080bf);
    public static readonly Vector4 PrimaryLight = FromHex(0x9cdcfe);
    public static readonly Vector4 PrimaryButton = FromHex(0x007acc);
    public static readonly Vector4 PrimaryButtonHover = FromHex(0x005fa3);

    // Accent - Cyan/Teal
    public static readonly Vector4 Cyan = FromHex(0x4dd8d0);
    public static readonly Vector4 CyanHover = FromHex(0x5de8e0);
    public static readonly Vector4 CyanDark = FromHex(0x3ab8b0);

    // Accent - Purple
    public static readonly Vector4 Purple = FromHex(0xb388ff);
    public static readonly Vector4 PurpleDark = FromHex(0x7c4dff);
    public static readonly Vector4 PurpleAccent = FromHex(0xe0b0ff);

    // Text
    public static readonly Vector4 TextPrimary = FromHex(0xcccccc);
    public static readonly Vector4 TextSecondary = FromHex(0x858585);
    public static readonly Vector4 TextMuted = FromHex(0x5a5a5a);
    public static readonly Vector4 TextBright = FromHex(0xe0e8f0);

    // Semantic
    public static readonly Vector4 Success = FromHex(0x3fb950);
    public static readonly Vector4 Error = FromHex(0xf85149);
    public static readonly Vector4 Warning = FromHex(0xcca700);
    public static readonly Vector4 Border = FromHex(0x333333);

    // Orange (terminal response)
    public static readonly Vector4 Orange = FromHex(0xf0883e);

    private static Vector4 FromHex(uint hex)
    {
        var r = ((hex >> 16) & 0xFF) / 255f;
        var g = ((hex >> 8) & 0xFF) / 255f;
        var b = (hex & 0xFF) / 255f;
        return new Vector4(r, g, b, 1.0f);
    }

    public static Vector4 WithAlpha(Vector4 color, float alpha)
    {
        return new Vector4(color.X, color.Y, color.Z, alpha);
    }

    /// <summary>Push the full CommsLink dark theme onto the ImGui style stack.</summary>
    public static int PushStyle()
    {
        int count = 0;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, BgBase);           count++;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, BgCard);            count++;
        ImGui.PushStyleColor(ImGuiCol.PopupBg, BgSurface);         count++;
        ImGui.PushStyleColor(ImGuiCol.Border, Border);             count++;
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, Vector4.Zero); count++;

        // Text
        ImGui.PushStyleColor(ImGuiCol.Text, TextPrimary);          count++;
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, TextMuted);    count++;

        // Frame (inputs, checkboxes)
        ImGui.PushStyleColor(ImGuiCol.FrameBg, BgInput);                          count++;
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, BgSurface);                 count++;
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, WithAlpha(PrimaryButton, 0.4f)); count++;

        // Title bar
        ImGui.PushStyleColor(ImGuiCol.TitleBg, BgSecondary);                      count++;
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, BgSecondary);                count++;
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, BgSecondary);             count++;

        // Buttons
        ImGui.PushStyleColor(ImGuiCol.Button, PrimaryButton);                     count++;
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, PrimaryButtonHover);         count++;
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, PrimaryDark);                 count++;

        // Header (collapsing headers, tree nodes)
        ImGui.PushStyleColor(ImGuiCol.Header, WithAlpha(PrimaryButton, 0.3f));     count++;
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, WithAlpha(PrimaryButton, 0.5f)); count++;
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, WithAlpha(PrimaryButton, 0.7f));   count++;

        // Separator
        ImGui.PushStyleColor(ImGuiCol.Separator, Border);                          count++;
        ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, PrimaryButton);            count++;
        ImGui.PushStyleColor(ImGuiCol.SeparatorActive, Primary);                   count++;

        // Slider
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Cyan);                           count++;
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, CyanHover);                count++;

        // Checkmark
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Cyan);                            count++;

        // Scrollbar
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, BgBase);                        count++;
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, WithAlpha(TextSecondary, 0.3f)); count++;
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, WithAlpha(TextSecondary, 0.5f)); count++;
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, WithAlpha(TextSecondary, 0.7f));  count++;

        // Tab
        ImGui.PushStyleColor(ImGuiCol.Tab, BgSurface);                            count++;
        ImGui.PushStyleColor(ImGuiCol.TabHovered, WithAlpha(PrimaryButton, 0.5f)); count++;

        return count;
    }

    public static void PopStyle(int count)
    {
        ImGui.PopStyleColor(count);
    }

    /// <summary>Push style vars for CommsLink look (minimal rounding, tight spacing).</summary>
    public static int PushVars()
    {
        int count = 0;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 2f);    count++;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);     count++;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 16)); count++;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));    count++;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));     count++;
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 8f);     count++;
        return count;
    }

    public static void PopVars(int count)
    {
        ImGui.PopStyleVar(count);
    }

    /// <summary>Draw a styled button with cyan CTA colors.</summary>
    public static bool CyanButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Cyan);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, CyanHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, CyanDark);
        ImGui.PushStyleColor(ImGuiCol.Text, BgBase);
        var result = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return result;
    }

    /// <summary>Draw a styled button with danger/red colors.</summary>
    public static bool DangerButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(Error, 0.7f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Error);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(Error, 0.5f));
        var result = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return result;
    }

    /// <summary>Draw a styled button with outline style (subtle).</summary>
    public static bool OutlineButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, WithAlpha(TextPrimary, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(TextPrimary, 0.15f));
        ImGui.PushStyleColor(ImGuiCol.Text, TextSecondary);
        var result = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return result;
    }

    /// <summary>Draw a section header with cyan accent.</summary>
    public static void SectionHeader(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(Cyan, label);
        ImGui.PushStyleColor(ImGuiCol.Separator, WithAlpha(Cyan, 0.3f));
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    /// <summary>Draw a status indicator dot + label.</summary>
    public static void StatusIndicator(string label, bool isOnline)
    {
        var color = isOnline ? Success : Error;
        var statusText = isOnline ? "Online" : "Offline";

        // Draw colored dot via text
        ImGui.TextColored(color, "\u25CF");
        ImGui.SameLine();
        ImGui.TextColored(TextPrimary, label);
        ImGui.SameLine();
        ImGui.TextColored(color, statusText);
    }

    /// <summary>Draw the CommsLink header with branding.</summary>
    public static void DrawHeader()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        // Header background bar
        drawList.AddRectFilled(
            pos,
            new Vector2(pos.X + width, pos.Y + 44),
            ImGui.ColorConvertFloat4ToU32(BgSecondary),
            2f);

        // Cyan accent line at top
        drawList.AddRectFilled(
            pos,
            new Vector2(pos.X + width, pos.Y + 2),
            ImGui.ColorConvertFloat4ToU32(Cyan));

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);

        // Logo image
        var logoWrap = LogoTexture?.GetWrapOrDefault();
        if (logoWrap != null)
        {
            ImGui.Image(logoWrap.Handle, new Vector2(28, 28));
            ImGui.SameLine();
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 12);
    }

    /// <summary>Begin a card-like region (simple indent + group).</summary>
    public static void BeginCard()
    {
        ImGui.Indent(4);
    }

    public static void EndCard()
    {
        ImGui.Unindent(4);
        ImGui.Spacing();
    }

    /// <summary>Begin a scrollable card with fixed height (for lists).</summary>
    public static bool BeginScrollCard(string id, float height)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, BgCard);
        ImGui.PushStyleColor(ImGuiCol.Border, Border);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        return ImGui.BeginChild(id, new Vector2(ImGui.GetContentRegionAvail().X, height), true);
    }

    public static void EndScrollCard()
    {
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }
}
