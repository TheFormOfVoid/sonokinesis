using System;
using Godot;
using Sonokinesis.Editor.UI;

namespace Sonokinesis.Editor.Hub;

/// <summary>Look and building blocks of the app's pages: cards, titles and big friendly buttons.</summary>
public static class HubUi
{
    public static readonly Font Regular = MakeFont(400);
    public static readonly Font Bold = MakeFont(700);
    public static readonly Font Semi = MakeFont(600);

    private static Font MakeFont(int weight) => new SystemFont
    {
        FontNames = new[] { "Segoe UI", "Arial" },
        FontWeight = weight,
        Antialiasing = TextServer.FontAntialiasing.Gray,
        SubpixelPositioning = TextServer.SubpixelPositioning.Auto,
    };

    public static Theme BuildTheme()
    {
        var t = UiTheme.Build();
        t.DefaultFont = Regular;
        t.DefaultFontSize = 15;
        foreach (var type in new[] { "Button", "OptionButton", "CheckButton" })
        {
            t.SetStylebox("normal", type, Box(UiTheme.Panel2, 7, UiTheme.Line, 1, 9, 16));
            t.SetStylebox("hover", type, Box(new Color("232837"), 7, UiTheme.Cyan.Darkened(0.35f), 1, 9, 16));
            t.SetStylebox("pressed", type, Box(new Color("2b3142"), 7, UiTheme.Pink, 1, 9, 16));
            t.SetStylebox("disabled", type, Box(UiTheme.Panel, 7, UiTheme.Line, 1, 9, 16));
            t.SetStylebox("focus", type, new StyleBoxEmpty());
        }
        // Switches: no box around the row, a pink pill when on.
        foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            t.SetStylebox(st, "CheckButton", new StyleBoxEmpty { ContentMarginTop = 4, ContentMarginBottom = 4 });
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            t.SetColor(k, "CheckButton", UiTheme.Text);
        t.SetColor("font_disabled_color", "CheckButton", UiTheme.Dim.Darkened(0.2f));
        t.SetIcon("checked", "CheckButton", SwitchIcon(true, false));
        t.SetIcon("unchecked", "CheckButton", SwitchIcon(false, false));
        t.SetIcon("checked_disabled", "CheckButton", SwitchIcon(true, true));
        t.SetIcon("unchecked_disabled", "CheckButton", SwitchIcon(false, true));
        t.SetStylebox("normal", "LineEdit", Box(UiTheme.Ink, 7, UiTheme.Line, 1, 8, 12));
        t.SetStylebox("focus", "LineEdit", Box(UiTheme.Ink, 7, UiTheme.Cyan, 1, 8, 12));
        t.SetStylebox("background", "ProgressBar", Box(UiTheme.Ink, 5, null, 0, 0, 0));
        t.SetStylebox("fill", "ProgressBar", Box(UiTheme.Cyan, 5, null, 0, 0, 0));
        t.SetColor("font_color", "ProgressBar", UiTheme.Ink);
        t.SetStylebox("panel", "AcceptDialog", Box(UiTheme.Panel, 10, UiTheme.Line, 1, 16, 16));
        return t;
    }

    /// <summary>A switch drawn as a pill with a knob (antialiased by distance).</summary>
    private static Texture2D SwitchIcon(bool on, bool disabled)
    {
        const int w = 46, h = 26;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var track = on ? UiTheme.Pink : new Color("343a4a");
        var knob = on ? Colors.White : new Color("aab1c2");
        if (disabled) { track = track.Darkened(0.5f); knob = knob.Darkened(0.5f); }
        float r = h / 2f, kx = on ? w - r : r;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float cx = Math.Clamp(px, r, w - r);
            float dTrack = r - MathF.Sqrt((px - cx) * (px - cx) + (py - r) * (py - r));
            float dKnob = (r - 4) - MathF.Sqrt((px - kx) * (px - kx) + (py - r) * (py - r));
            var c = new Color(track, Math.Clamp(dTrack + 0.5f, 0, 1));
            float k = Math.Clamp(dKnob + 0.5f, 0, 1);
            if (k > 0) c = new Color(c.Lerp(knob, k), Math.Max(c.A, k));
            img.SetPixel(x, y, c);
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static StyleBoxFlat Box(Color bg, int radius, Color? border, int bw, int padV, int padH)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            ContentMarginTop = padV, ContentMarginBottom = padV, ContentMarginLeft = padH, ContentMarginRight = padH,
        };
        sb.SetCornerRadiusAll(radius);
        if (border is Color b) { sb.BorderColor = b; sb.SetBorderWidthAll(bw); }
        sb.AntiAliasing = true;
        return sb;
    }

    public static Label Text(string text, int size = 15, Color? color = null, Font font = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? UiTheme.Text);
        if (font != null) l.AddThemeFontOverride("font", font);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.CustomMinimumSize = new Vector2(80, 0); }
        return l;
    }

    public static Label PageTitle(string text) => Text(text, 32, UiTheme.Text, Bold);
    public static Label Heading(string text) => Text(text, 19, UiTheme.Text, Semi);
    public static Label Dim(string text, int size = 15) => Text(text, size, UiTheme.Dim, null, true);

    public static PanelContainer Card(out VBoxContainer body, int pad = 22, Color? border = null)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", Box(UiTheme.Panel, 12, border ?? UiTheme.Line, 1, pad, pad + 2));
        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        card.AddChild(body);
        return card;
    }

    /// <summary>A filled, colored button for the main action of a page.</summary>
    public static Button Primary(string text, Action onPress, Color? color = null, int fontSize = 16)
    {
        var c = color ?? UiTheme.Pink;
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeStyleboxOverride("normal", Box(c, 9, null, 0, 11, 22));
        b.AddThemeStyleboxOverride("hover", Box(c.Lightened(0.15f), 9, null, 0, 11, 22));
        b.AddThemeStyleboxOverride("pressed", Box(c.Darkened(0.15f), 9, null, 0, 11, 22));
        b.AddThemeStyleboxOverride("disabled", Box(c.Darkened(0.55f), 9, null, 0, 11, 22));
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            b.AddThemeColorOverride(k, UiTheme.Ink);
        b.AddThemeColorOverride("font_disabled_color", UiTheme.Ink.Lightened(0.25f));
        b.AddThemeFontOverride("font", Bold);
        b.AddThemeFontSizeOverride("font_size", fontSize);
        if (onPress != null) b.Pressed += onPress;
        return b;
    }

    public static Button Secondary(string text, Action onPress, string tooltip = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "", FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeFontOverride("font", Semi);
        if (onPress != null) b.Pressed += onPress;
        return b;
    }

    /// <summary>A text-only button, like a link.</summary>
    public static Button Link(string text, Action onPress)
    {
        var b = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeColorOverride("font_color", UiTheme.Cyan);
        b.AddThemeColorOverride("font_hover_color", UiTheme.Cyan.Lightened(0.3f));
        b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        if (onPress != null) b.Pressed += onPress;
        return b;
    }

    public static HBoxContainer Row(int gap = 10, params Control[] children)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", gap);
        foreach (var c in children) r.AddChild(c);
        return r;
    }

    public static VBoxContainer Column(int gap = 10)
    {
        var c = new VBoxContainer();
        c.AddThemeConstantOverride("separation", gap);
        return c;
    }

    public static Control Grow() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    public static Control Gap(int h) => new Control { CustomMinimumSize = new Vector2(0, h) };
}

/// <summary>A round status light.</summary>
public partial class StatusDot : Control
{
    private Color color;
    public StatusDot() : this(new Color("8a91a3")) { }
    public StatusDot(Color c) { color = c; CustomMinimumSize = new Vector2(14, 14); SizeFlagsVertical = SizeFlags.ShrinkCenter; }
    public void Set(Color c) { color = c; QueueRedraw(); }
    public override void _Draw()
    {
        DrawCircle(Size / 2, 7, new Color(color, 0.25f));
        DrawCircle(Size / 2, 4.5f, color);
    }
}
