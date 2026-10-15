using System;
using System.Collections.Generic;
using Sonokinesis.Loader.Runtime;
using UnityEngine;

namespace Sonokinesis.Loader.UI;

/// <summary>
/// The custom level list, drawn with Unity's immediate mode GUI and Sonokinesis's own styling.
/// Opened from the main menu with the menu key (F1 by default).
/// </summary>
internal sealed class LevelMenu
{
    private static readonly Color Ink = new(0.043f, 0.051f, 0.071f, 0.96f);
    private static readonly Color Panel = new(0.075f, 0.086f, 0.114f, 1f);
    private static readonly Color Row = new(0.11f, 0.125f, 0.16f, 1f);
    private static readonly Color Pink = new(1f, 0.165f, 0.427f, 1f);
    private static readonly Color Cyan = new(0.2f, 0.9f, 1f, 1f);
    private static readonly Color Text = new(0.93f, 0.94f, 0.96f, 1f);
    private static readonly Color Dim = new(0.6f, 0.63f, 0.7f, 1f);
    private static readonly Color Bad = new(1f, 0.55f, 0.3f, 1f);

    private readonly Launcher launcher;
    private int selected;
    private float scroll;
    private bool hard;
    private UI_MainMenu02 menuWithInputOff;
    private UI_Practice practiceWithInputOff;
    private readonly Dictionary<string, GUIStyle> styles = new();

    public bool Open { get; private set; }

    public LevelMenu(Launcher launcher) { this.launcher = launcher; }

    public void Show()
    {
        if (Open) return;
        Open = true;
        LevelLibrary.Rescan();
        menuWithInputOff = GameBridge.FindUi<UI_MainMenu02>();
        practiceWithInputOff = GameBridge.FindUi<UI_Practice>();
        try { menuWithInputOff?.enableInput(false); } catch { }
        try { practiceWithInputOff?.enableInput(false); } catch { }
    }

    public void Hide()
    {
        if (!Open) return;
        Open = false;
        try { menuWithInputOff?.enableInput(true); } catch { }
        try { practiceWithInputOff?.enableInput(true); } catch { }
        menuWithInputOff = null;
        practiceWithInputOff = null;
    }

    public void Update()
    {
        if (!Open)
        {
            if (Input.GetKeyDown(Settings.MenuKey) && GameBridge.InMenu() && !launcher.Busy && !CustomSession.Active)
            {
                if (GameBridge.InOnlineMode()) { onlineNoticeUntil = Time.unscaledTime + 4; return; }
                Show();
            }
            return;
        }
        if (launcher.Busy) return;
        var entries = LevelLibrary.Entries;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(Settings.MenuKey) || Input.GetKeyDown(KeyCode.JoystickButton1)) { Hide(); return; }
        if (Input.GetKeyDown(KeyCode.F5)) LevelLibrary.Rescan();
        if (entries == null || entries.Count == 0) return;
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) selected = (selected + 1) % entries.Count;
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) selected = (selected - 1 + entries.Count) % entries.Count;
        if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.Tab)) hard = !hard;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0))
            Play(entries);
    }

    /// <summary>Plays the entry at an index, as if chosen in the list (used by the dev console).</summary>
    public void PlayIndex(int index, bool hardMode)
    {
        var entries = LevelLibrary.Entries;
        if (entries == null || index < 0 || index >= entries.Count) return;
        selected = index;
        hard = hardMode;
        Play(entries);
    }

    private void Play(List<LevelEntry> entries)
    {
        selected = Mathf.Clamp(selected, 0, entries.Count - 1);
        var e = entries[selected];
        if (!e.IsValid) return;
        Hide();
        launcher.Launch(e, hard);
    }

    // ---------------- drawing ----------------

    public void OnGUI()
    {
        float s = Screen.height / 1080f;
        DrawFade();
        if (!Open)
        {
            if (GameBridge.InMenu() && !launcher.Busy && !CustomSession.Active) DrawHint(s);
            DrawLauncherStatus(s);
            return;
        }

        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.72f));
        float w = 1240 * s, h = 760 * s;
        var panel = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        Fill(panel, Ink);
        Fill(new Rect(panel.x, panel.y, panel.width, 6 * s), Pink);

        Label(new Rect(panel.x + 40 * s, panel.y + 30 * s, 800 * s, 60 * s), "CUSTOM LEVELS", Style("title", 46, Text, FontStyle.Bold));
        Label(new Rect(panel.x + 42 * s, panel.y + 88 * s, 900 * s, 30 * s), $"Sonokinesis loader {Plugin.Version}  ·  levels folder: {Settings.LevelsFolder}", Style("small", 18, Dim));

        var entries = LevelLibrary.Entries;
        var listRect = new Rect(panel.x + 40 * s, panel.y + 140 * s, 640 * s, h - 220 * s);
        var detailRect = new Rect(listRect.xMax + 30 * s, listRect.y, panel.xMax - listRect.xMax - 70 * s, listRect.height);
        Fill(listRect, Panel);
        Fill(detailRect, Panel);

        if (entries == null)
        {
            Label(Inset(listRect, 24 * s), "Looking for levels…", Style("body", 24, Dim));
        }
        else if (entries.Count == 0)
        {
            Label(Inset(listRect, 24 * s), "No levels yet.\n\nPut level folders (or .zip files) made with the Sonokinesis editor into the levels folder shown above, then press F5.", Style("bodywrap", 22, Dim, wrap: true));
        }
        else
        {
            selected = Mathf.Clamp(selected, 0, entries.Count - 1);
            DrawList(entries, listRect, s);
            DrawDetails(entries[selected], detailRect, s);
        }

        Label(new Rect(panel.x + 40 * s, panel.yMax - 62 * s, w - 80 * s, 30 * s),
            "↑ ↓  choose     ENTER  play     H  normal / hard     F5  rescan     ESC  close", Style("hint", 18, Dim));
    }

    private void DrawList(List<LevelEntry> entries, Rect area, float s)
    {
        float rowH = 92 * s, gap = 8 * s;
        float contentH = entries.Count * (rowH + gap);
        float selTop = selected * (rowH + gap);
        // Keep the selected row visible.
        if (selTop < scroll) scroll = selTop;
        if (selTop + rowH > scroll + area.height - 16 * s) scroll = selTop + rowH - area.height + 16 * s;
        scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, contentH - area.height + 16 * s));

        GUI.BeginGroup(area);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var r = new Rect(8 * s, 8 * s + i * (rowH + gap) - scroll, area.width - 16 * s, rowH);
            if (r.yMax < 0 || r.y > area.height) continue;
            bool sel = i == selected;
            Fill(r, sel ? new Color(0.16f, 0.18f, 0.23f, 1) : Row);
            if (sel) Fill(new Rect(r.x, r.y, 6 * s, r.height), hard ? Pink : Cyan);

            Label(new Rect(r.x + 24 * s, r.y + 12 * s, r.width - 160 * s, 36 * s), e.Meta.Title, Style("rowtitle", 26, e.IsValid ? Text : Dim, FontStyle.Bold));
            var sub = e.IsValid
                ? $"{e.Meta.Artist}{(string.IsNullOrEmpty(e.Meta.Author) ? "" : "  ·  level by " + e.Meta.Author)}"
                : "⚠ " + e.Error;
            Label(new Rect(r.x + 24 * s, r.y + 50 * s, r.width - 48 * s, 28 * s), sub, Style(e.IsValid ? "rowsub" : "rowbad", 18, e.IsValid ? Dim : Bad));
            if (e.IsValid) DrawPips(new Rect(r.xMax - 128 * s, r.y + 22 * s, 110 * s, 14 * s), e.Meta.Difficulty, s);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                if (selected == i && Event.current.clickCount >= 2) Play(entries);
                selected = i;
                Event.current.Use();
            }
        }
        GUI.EndGroup();
    }

    private void DrawDetails(LevelEntry e, Rect r, float s)
    {
        var inner = Inset(r, 28 * s);
        Label(new Rect(inner.x, inner.y, inner.width, 90 * s), e.Meta.Title, Style("dtitle", 38, Text, FontStyle.Bold, wrap: true));
        Label(new Rect(inner.x, inner.y + 96 * s, inner.width, 30 * s), e.Meta.Artist, Style("dartist", 24, Cyan));
        float y = inner.y + 140 * s;
        if (!string.IsNullOrEmpty(e.Meta.Author))
        {
            Label(new Rect(inner.x, y, inner.width, 28 * s), "level by " + e.Meta.Author, Style("dsub", 20, Dim));
            y += 36 * s;
        }
        if (e.IsValid)
        {
            int secs = (int)(e.LengthMs / 1000);
            Label(new Rect(inner.x, y, inner.width, 28 * s), $"{secs / 60}:{secs % 60:00}   ·   {e.ObjectCount} objects   ·   difficulty", Style("dsub", 20, Dim));
            DrawPips(new Rect(inner.x + 330 * s, y + 7 * s, 110 * s, 14 * s), e.Meta.Difficulty, s);
            y += 46 * s;
        }
        if (!string.IsNullOrEmpty(e.Meta.Description))
        {
            Label(new Rect(inner.x, y, inner.width, 200 * s), e.Meta.Description, Style("ddesc", 20, Text, wrap: true));
        }
        if (!e.IsValid)
        {
            Label(new Rect(inner.x, y, inner.width, 200 * s), "This level can't be played:\n" + e.Error, Style("dbad", 20, Bad, wrap: true));
            return;
        }

        // Mode toggle and play button.
        var modeRect = new Rect(inner.x, inner.yMax - 140 * s, inner.width, 50 * s);
        var half = new Rect(modeRect.x, modeRect.y, modeRect.width / 2 - 5 * s, modeRect.height);
        var half2 = new Rect(modeRect.x + modeRect.width / 2 + 5 * s, modeRect.y, modeRect.width / 2 - 5 * s, modeRect.height);
        if (ClickRect(half)) hard = false;
        if (ClickRect(half2)) hard = true;
        Fill(half, !hard ? Cyan : Row);
        Fill(half2, hard ? Pink : Row);
        Label(half, "NORMAL", Style("modeOn", 22, !hard ? Ink : Dim, FontStyle.Bold, TextAnchor.MiddleCenter));
        Label(half2, "HARD", Style("modeOn2", 22, hard ? Ink : Dim, FontStyle.Bold, TextAnchor.MiddleCenter));

        var play = new Rect(inner.x, inner.yMax - 76 * s, inner.width, 76 * s);
        Fill(play, Text);
        Label(play, "PLAY", Style("play", 32, Ink, FontStyle.Bold, TextAnchor.MiddleCenter));
        if (ClickRect(play)) Play(LevelLibrary.Entries);
    }

    private void DrawPips(Rect r, int difficulty, float s)
    {
        float w = (r.width - 4 * 6 * s) / 5;
        for (int i = 0; i < 5; i++)
            Fill(new Rect(r.x + i * (w + 6 * s), r.y, w, r.height), i < difficulty ? Pink : new Color(1, 1, 1, 0.12f));
    }

    private void DrawHint(float s)
    {
        var r = new Rect(Screen.width - 380 * s, 24 * s, 356 * s, 44 * s);
        Fill(r, new Color(0, 0, 0, 0.55f));
        Fill(new Rect(r.x, r.y, 5 * s, r.height), Pink);
        Label(new Rect(r.x + 18 * s, r.y, r.width, r.height), $"{Settings.MenuKey}  ·  Custom levels", Style("hintbox", 20, Text, FontStyle.Bold, TextAnchor.MiddleLeft));
    }

    private void DrawLauncherStatus(float s)
    {
        string msg = null;
        Color col = Text;
        if (launcher.Current == Launcher.State.Loading) msg = $"Loading {launcher.LoadingTitle}…";
        else if (Time.unscaledTime < onlineNoticeUntil) { msg = "Custom levels are for solo and local co-op. Leave online play to use them."; col = Bad; }
        else if (launcher.Error != null && launcher.Current == Launcher.State.Idle && Time.unscaledTime - errorShownAt < 8) { msg = "Could not start level: " + launcher.Error; col = Bad; }
        if (launcher.Error != null && launcher.Error != lastError) { lastError = launcher.Error; errorShownAt = Time.unscaledTime; }
        if (msg == null) return;
        var r = new Rect(40 * s, Screen.height - 100 * s, Screen.width - 80 * s, 60 * s);
        Fill(r, new Color(0, 0, 0, 0.75f));
        Label(Inset(r, 16 * s), msg, Style("status", 24, col, wrap: true));
    }

    private float onlineNoticeUntil;
    private string lastError;
    private float errorShownAt = -100;

    private void DrawFade()
    {
        if (launcher.FadeAlpha <= 0) return;
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, launcher.FadeAlpha));
    }

    // ---------------- helpers ----------------

    private static Rect Inset(Rect r, float by) => new(r.x + by, r.y + by, r.width - by * 2, r.height - by * 2);

    private static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private static bool ClickRect(Rect r)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || !r.Contains(e.mousePosition)) return false;
        e.Use();
        return true;
    }

    private static void Label(Rect r, string text, GUIStyle style) => GUI.Label(r, text ?? "", style);

    private GUIStyle Style(string key, int size, Color color, FontStyle font = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
    {
        float s = Screen.height / 1080f;
        int px = Mathf.Max(8, Mathf.RoundToInt(size * s));
        var k = $"{key}:{px}:{color}:{font}:{anchor}:{wrap}";
        if (styles.TryGetValue(k, out var st)) return st;
        st = new GUIStyle(GUI.skin.label) { fontSize = px, fontStyle = font, alignment = anchor, wordWrap = wrap, clipping = TextClipping.Clip };
        st.normal.textColor = color;
        styles[k] = st;
        return st;
    }
}
