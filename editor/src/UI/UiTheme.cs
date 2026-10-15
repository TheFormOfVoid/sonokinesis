using Godot;

namespace Sonokinesis.Editor.UI;

/// <summary>Colors and the Godot theme for the editor. Dark ink panels with pink and cyan accents.</summary>
public static class UiTheme
{
    public static readonly Color Ink = new("0b0d12");
    public static readonly Color Panel = new("13161d");
    public static readonly Color Panel2 = new("1a1e27");
    public static readonly Color Line = new("272c38");
    public static readonly Color Text = new("e8eaf0");
    public static readonly Color Dim = new("8a91a3");
    public static readonly Color Pink = new("ff2a6d");
    public static readonly Color Cyan = new("33e5ff");
    public static readonly Color Gold = new("ffd23f");
    public static readonly Color Good = new("4ade80");
    public static readonly Color Bad = new("ff8a4c");

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 14 };

        StyleBoxFlat Box(Color bg, int radius = 4, Color? border = null, int bw = 0, int pad = 6)
        {
            var sb = new StyleBoxFlat
            {
                BgColor = bg,
                CornerRadiusTopLeft = radius,
                CornerRadiusTopRight = radius,
                CornerRadiusBottomLeft = radius,
                CornerRadiusBottomRight = radius,
                ContentMarginLeft = pad + 2,
                ContentMarginRight = pad + 2,
                ContentMarginTop = pad,
                ContentMarginBottom = pad,
            };
            if (border is Color b)
            {
                sb.BorderColor = b;
                sb.BorderWidthBottom = sb.BorderWidthTop = sb.BorderWidthLeft = sb.BorderWidthRight = bw;
            }
            return sb;
        }

        t.SetStylebox("panel", "Panel", Box(Panel, 0, null, 0, 0));
        t.SetStylebox("panel", "PanelContainer", Box(Panel, 0, null, 0, 8));

        foreach (var type in new[] { "Button", "OptionButton", "MenuButton", "ColorPickerButton", "CheckButton" })
        {
            t.SetStylebox("normal", type, Box(Panel2, 4, Line, 1));
            t.SetStylebox("hover", type, Box(new Color("232837"), 4, Cyan.Darkened(0.4f), 1));
            t.SetStylebox("pressed", type, Box(new Color("2b3142"), 4, Pink, 1));
            t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), 4, Cyan.Darkened(0.2f), 1));
            t.SetStylebox("disabled", type, Box(Panel, 4, Line, 1));
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Pink.Lightened(0.3f));
            t.SetColor("font_disabled_color", type, Dim.Darkened(0.3f));
        }
        t.SetStylebox("normal", "LineEdit", Box(Ink, 3, Line, 1, 4));
        t.SetStylebox("focus", "LineEdit", Box(Ink, 3, Cyan, 1, 4));
        t.SetColor("font_color", "LineEdit", Text);
        t.SetStylebox("normal", "TextEdit", Box(Ink, 3, Line, 1, 4));
        t.SetStylebox("focus", "TextEdit", Box(Ink, 3, Cyan, 1, 4));
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_color", "CheckBox", Text);
        t.SetStylebox("panel", "PopupMenu", Box(Panel2, 4, Line, 1));
        t.SetStylebox("panel", "PopupPanel", Box(Panel2, 4, Line, 1));
        t.SetStylebox("panel", "TooltipPanel", Box(new Color("0f1218"), 4, Cyan.Darkened(0.4f), 1));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetStylebox("panel", "AcceptDialog", Box(Panel, 6, Line, 1, 10));
        t.SetStylebox("grabber", "HSplitContainer", new StyleBoxFlat { BgColor = Line });
        t.SetConstant("separation", "HSplitContainer", 6);
        t.SetConstant("separation", "VSplitContainer", 6);
        t.SetStylebox("tab_selected", "TabContainer", Box(Panel2, 4, Pink, 0));
        t.SetStylebox("tab_unselected", "TabContainer", Box(Panel, 4));
        t.SetStylebox("panel", "TabContainer", Box(Panel, 0, null, 0, 6));
        t.SetStylebox("scroll", "VScrollBar", Box(Ink, 2, null, 0, 2));
        t.SetStylebox("grabber", "VScrollBar", Box(Line, 2, null, 0, 2));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(Dim, 2, null, 0, 2));
        return t;
    }

    public static Label Heading(string text, int size = 12, Color? color = null)
    {
        var l = new Label { Text = text.ToUpperInvariant() };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Dim);
        return l;
    }

    public static Label Small(string text, Color? color = null)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", color ?? Dim);
        return l;
    }

    /// <summary>
    /// Shows a dialog sized to its content. Wrapping labels only know their height after a layout pass,
    /// so the dialog is shrunk to fit once that has happened.
    /// </summary>
    public static void PopupFitted(Window dlg)
    {
        dlg.ResetSize();
        dlg.PopupCentered();
        void Fit()
        {
            if (!GodotObject.IsInstanceValid(dlg) || !dlg.Visible) return;
            dlg.Size = new Vector2I(dlg.Size.X, 1);
            dlg.ResetSize();
            dlg.MoveToCenter();
        }
        var tree = dlg.GetTree();
        tree.CreateTimer(0).Timeout += Fit;
        tree.CreateTimer(0.05).Timeout += Fit;
    }

    public static Color ParseHex(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#') return fallback;
        try { return new Color(hex); } catch { return fallback; }
    }

    public static string ToHex(Color c) => "#" + c.ToHtml(c.A < 0.999f);
}
