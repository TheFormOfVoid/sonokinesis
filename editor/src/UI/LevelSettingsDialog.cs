using System;
using System.IO;
using Beatform.Editor.Audio;
using Beatform.Editor.Model;
using Godot;

namespace Beatform.Editor.UI;

/// <summary>Level-wide settings: song info, tempo, timing, colors, and editor preferences.</summary>
public static class LevelSettingsDialog
{
    private static EditorState S => EditorState.I;

    public static void Show(App app)
    {
        var d = S.Doc;
        var dlg = new AcceptDialog { Title = "Level settings", OkButtonText = "Done", MinSize = new Vector2I(560, 0) };
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(540, 470) };
        dlg.AddChild(tabs);

        // ----- song -----
        var song = Page(tabs, "Song");
        Field(song, "Title", Text(d.Title, v => Edit("Title", () => d.Title = v)));
        Field(song, "Artist", Text(d.Artist, v => Edit("Artist", () => d.Artist = v)));
        Field(song, "Level by", Text(d.Author, v => { Edit("Author", () => d.Author = v); EditorSettings.AuthorName = v; }));
        var desc = new TextEdit { Text = d.Description, CustomMinimumSize = new Vector2(0, 70), WrapMode = TextEdit.LineWrappingMode.Boundary };
        desc.TextChanged += () => Edit("Description", () => d.Description = desc.Text, false);
        Field(song, "Description", desc);
        Field(song, "Difficulty", Num(d.Difficulty, 1, 5, 1, v => Edit("Difficulty", () => d.Difficulty = (int)v)));
        var songRow = new HBoxContainer();
        var songName = new Label { Text = string.IsNullOrEmpty(d.Audio) ? "(none)" : d.Audio, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
        songRow.AddChild(songName);
        var change = new Button { Text = "Change song…" };
        change.Pressed += () => ChangeSong(app, songName);
        songRow.AddChild(change);
        Field(song, "Song file", songRow);

        // ----- timing -----
        var timing = Page(tabs, "Timing");
        var bpm = Num(d.Bpm, 20, 400, 0.01, v => { Edit("BPM", () => d.Bpm = v); app.Timeline.QueueRedraw(); });
        var bpmRow = new HBoxContainer();
        bpm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bpmRow.AddChild(bpm);
        var detect = new Button { Text = "Detect", TooltipText = "Guess the tempo from the song" };
        var detectInfo = UiTheme.Small("");
        detect.Pressed += () =>
        {
            var w = app.Song.Waveform;
            if (w == null) { detectInfo.Text = "The song is still being read. Try again in a moment."; return; }
            var guess = TempoGuess.Estimate(w);
            if (guess <= 0) { detectInfo.Text = "Could not find a steady beat."; return; }
            bpm.Value = Math.Round(guess, 2);
            detectInfo.Text = $"Guessed {guess:0.##} BPM. Check it against the grid; halve or double it if needed.";
        };
        bpmRow.AddChild(detect);
        Field(timing, "BPM", bpmRow);
        timing.AddChild(detectInfo);
        Field(timing, "Beats per bar", Num(d.BeatsPerBar, 1, 16, 1, v => { Edit("Beats per bar", () => d.BeatsPerBar = (int)v); app.Timeline.QueueRedraw(); }));
        Field(timing, "Song offset", Num(d.OffsetMs, -10000, 600000, 1, v => { Edit("Offset", () => d.OffsetMs = v); app.SongSettingsChanged(); }, " ms"),
            "Song time 0 is this far into the audio file. Use it to skip silence so bar 1 lands on the first beat.");
        var lengthRow = new HBoxContainer();
        var auto = new CheckBox { Text = "use the song's length", ButtonPressed = d.LengthMs == null };
        var length = Num(d.LengthMs ?? Math.Max(0, app.Song.LengthMs - d.OffsetMs), 1000, 3600000, 1, v => { if (!auto.ButtonPressed) Edit("Length", () => d.LengthMs = v); }, " ms");
        length.Editable = d.LengthMs != null;
        auto.Toggled += on => { length.Editable = !on; Edit("Length", () => d.LengthMs = on ? null : length.Value); };
        lengthRow.AddChild(auto);
        length.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lengthRow.AddChild(length);
        Field(timing, "Level ends", lengthRow, "The level finishes here. The game shows its finish once players get past it.");
        Field(timing, "Preview from", Num(d.PreviewStartMs, 0, 3600000, 1, v => Edit("Preview start", () => d.PreviewStartMs = v), " ms"));

        // ----- look -----
        var look = Page(tabs, "Look");
        Field(look, "Background", ColorField(d.Background, v => Edit("Background color", () => d.Background = v)));
        Field(look, "Hazards", ColorField(d.Hazard, v => Edit("Hazard color", () => d.Hazard = v)), "Default color of objects that use the theme color.");
        Field(look, "Activation flash", ColorField(d.Flash, v => Edit("Flash color", () => d.Flash = v)), "Objects flash this color for a moment when they become dangerous.");
        Field(look, "Hitbox scale", Num(d.HitboxScale, 0.1, 2, 0.01, v => Edit("Hitbox scale", () => d.HitboxScale = v), " ×"),
            "Shrinks every hitbox a little so near misses feel fair. 0.9 is a good start.");

        // ----- editor -----
        var ed = Page(tabs, "Editor");
        Field(ed, "Game folder", Text(EditorSettings.GameDir ?? GameBridge.FindGame() ?? "", v => EditorSettings.GameDir = v),
            "The Just Shapes & Beats install folder (the one with JSB.exe). Found automatically through Steam when possible.");
        Field(ed, "New levels in", Text(EditorSettings.LevelsRoot, v => EditorSettings.LevelsRoot = v));
        var game = GameBridge.FindGame();
        ed.AddChild(UiTheme.Small(game == null ? "Game not found." : GameBridge.LoaderInstalled(game) ? "Beatform loader is installed in the game." : "The game was found, but the Beatform loader is not installed yet.",
            game != null && GameBridge.LoaderInstalled(game) ? UiTheme.Good : UiTheme.Bad));
        ed.AddChild(UiTheme.Small($"Level id: {d.Id}. Folder: {d.FolderPath ?? "(not saved yet)"}"));

        dlg.Confirmed += () => dlg.QueueFree();
        dlg.Canceled += () => dlg.QueueFree();
        app.AddChild(dlg);
        dlg.PopupCentered();
    }

    private static void Edit(string label, Action change, bool snapshot = true)
    {
        if (snapshot) S.Edit(label, change);
        else { change(); S.Touch(); }
    }

    private static VBoxContainer Page(TabContainer tabs, string name)
    {
        var page = new VBoxContainer { Name = name };
        page.AddThemeConstantOverride("separation", 8);
        tabs.AddChild(page);
        return page;
    }

    private static void Field(VBoxContainer page, string label, Control field, string tooltip = null)
    {
        var row = new HBoxContainer();
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(130, 0), TooltipText = tooltip ?? "", MouseFilter = Control.MouseFilterEnum.Pass };
        l.AddThemeColorOverride("font_color", UiTheme.Dim);
        row.AddChild(l);
        field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (tooltip != null) field.TooltipText = tooltip;
        row.AddChild(field);
        page.AddChild(row);
    }

    private static LineEdit Text(string value, Action<string> set)
    {
        var le = new LineEdit { Text = value ?? "" };
        le.TextSubmitted += v => set(v);
        le.FocusExited += () => { if (le.Text != (value ?? "")) { value = le.Text; set(le.Text); } };
        return le;
    }

    private static SpinBox Num(double value, double min, double max, double step, Action<double> set, string suffix = "")
    {
        var sb = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix, SelectAllOnFocus = true };
        sb.ValueChanged += v => set(v);
        return sb;
    }

    private static ColorPickerButton ColorField(string hex, Action<string> set)
    {
        var cp = new ColorPickerButton { Color = UiTheme.ParseHex(hex, Colors.White), CustomMinimumSize = new Vector2(0, 26), EditAlpha = false };
        cp.PopupClosed += () => set(UiTheme.ToHex(cp.Color));
        return cp;
    }

    private static void ChangeSong(App app, Label songName)
    {
        var fd = new FileDialog
        {
            Title = "Choose the song",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Filters = new[] { "*.ogg, *.mp3, *.wav ; Songs" },
        };
        fd.FileSelected += path =>
        {
            fd.QueueFree();
            if (S.Doc.FolderPath == null) app.Save();
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var name = "song" + ext;
            var dest = Path.Combine(S.Doc.FolderPath, name);
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dest, true);
            S.Edit("Change song", () => S.Doc.Audio = name);
            songName.Text = name;
            app.SongSettingsChanged();
        };
        fd.Canceled += () => fd.QueueFree();
        app.AddChild(fd);
        fd.PopupCentered(new Vector2I(900, 600));
    }
}

/// <summary>A simple tempo estimate from the waveform envelope (onset strength autocorrelation).</summary>
public static class TempoGuess
{
    public static double Estimate(Waveform w)
    {
        int n = w.Min.Length;
        if (n < 2000) return 0;
        var env = new float[n];
        for (int i = 0; i < n; i++) env[i] = Math.Max(-w.Min[i], w.Max[i]);
        var onset = new float[n];
        for (int i = 1; i < n; i++) onset[i] = Math.Max(0, env[i] - env[i - 1]);
        // Look at up to 90 s from the middle of the song.
        int len = Math.Min(n, (int)(90000 / Waveform.BucketMs));
        int from = Math.Max(0, n / 2 - len / 2);
        double best = 0, bestBpm = 0;
        for (double bpm = 70; bpm <= 200; bpm += 0.05)
        {
            double lag = 60000 / bpm / Waveform.BucketMs;
            double sum = 0;
            for (int i = from; i + (int)(lag * 4) < from + len; i++)
            {
                int j = i + (int)Math.Round(lag);
                sum += onset[i] * onset[j];
            }
            if (sum > best) { best = sum; bestBpm = bpm; }
        }
        return bestBpm;
    }
}
