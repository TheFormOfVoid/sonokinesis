using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sonokinesis.Editor.UI;

namespace Sonokinesis.Editor.Hub;

/// <summary>Play: is the game there, is Sonokinesis in it, and a big button to start it.</summary>
public partial class PlayPage : HubPage
{
    private StatusDot gameDot, loaderDot;
    private Label gameDetail, loaderDetail, playHint;
    private Button loaderButton, playButton;
    private CheckButton modsSwitch;
    private Control modsRow;
    private bool busy;

    public PlayPage()
    {
        Body.AddChild(HubUi.PageTitle("Play"));
        Body.AddChild(HubUi.Dim("Custom levels for Just Shapes & Beats. Sonokinesis puts a small mod into the game that adds a custom level list, and never changes your save or achievements."));

        Body.AddChild(HubUi.Card(out var status));
        gameDot = new StatusDot();
        gameDetail = HubUi.Dim("");
        var gameText = HubUi.Column(2);
        gameText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        gameText.AddChild(HubUi.Heading("Just Shapes & Beats"));
        gameText.AddChild(gameDetail);
        status.AddChild(HubUi.Row(14, gameDot, gameText, HubUi.Secondary("Choose folder…", ChooseGame, "Pick the game folder yourself (the one with JSB.exe)")));
        status.AddChild(new HSeparator());

        loaderDot = new StatusDot();
        loaderDetail = HubUi.Dim("");
        var loaderText = HubUi.Column(2);
        loaderText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        loaderText.AddChild(HubUi.Heading("Sonokinesis in the game"));
        loaderText.AddChild(loaderDetail);
        loaderButton = HubUi.Secondary("Install", InstallLoader);
        status.AddChild(HubUi.Row(14, loaderDot, loaderText, loaderButton));

        modsSwitch = new CheckButton { Text = "Load Sonokinesis when the game starts", FocusMode = FocusModeEnum.None };
        modsSwitch.AddThemeFontOverride("font", HubUi.Semi);
        modsSwitch.Toggled += on => SetMods(on);
        var modsText = HubUi.Column(2);
        modsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        modsText.AddChild(modsSwitch);
        modsText.AddChild(HubUi.Dim("Turn this off to play the normal game without any mods. Nothing is uninstalled: turn it back on whenever you like.", 13));
        modsRow = HubUi.Row(14, new Control { CustomMinimumSize = new Vector2(14, 0) }, modsText);
        status.AddChild(modsRow);

        Body.AddChild(HubUi.Gap(4));
        playButton = HubUi.Primary("▶   Play Just Shapes & Beats", Play, UiTheme.Pink, 22);
        playButton.CustomMinimumSize = new Vector2(420, 72);
        playButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        Body.AddChild(playButton);
        playHint = HubUi.Dim("");
        Body.AddChild(playHint);

        Body.AddChild(HubUi.Gap(6));
        Body.AddChild(HubUi.Heading("How it works"));
        var steps = HubUi.Row(14);
        foreach (var (n, title, text) in new[]
        {
            ("1", "Add levels", "Drop level .zip files on this window, or get them in the Levels tab."),
            ("2", "Start the game", "Press Play above. Sonokinesis is set up for you the first time."),
            ("3", "Press F1", "On the game's main menu, F1 opens your custom levels. Pick one and press Enter."),
        })
        {
            var card = HubUi.Card(out var b, 18);
            card.SizeFlagsStretchRatio = 1;
            var num = HubUi.Text(n, 26, UiTheme.Cyan, HubUi.Bold);
            b.AddChild(num);
            b.AddChild(HubUi.Text(title, 17, UiTheme.Text, HubUi.Semi));
            b.AddChild(HubUi.Dim(text, 14));
            steps.AddChild(card);
        }
        Body.AddChild(steps);
    }

    public override void Refresh()
    {
        if (busy) return;
        var state = GameInstall.State(out var game);
        bool running = game != null && GameInstall.GameRunning;

        gameDot.Set(game != null ? UiTheme.Good : UiTheme.Bad);
        gameDetail.Text = game ?? "Not found. Is it installed through Steam? If it is somewhere else, choose its folder.";

        bool mods = game != null && GameInstall.ModsEnabled(game);
        modsRow.Visible = state is LoaderState.Installed or LoaderState.Outdated;
        modsSwitch.SetPressedNoSignal(mods);

        loaderButton.Visible = game != null;
        switch (state)
        {
            case LoaderState.NoGame:
                loaderDot.Set(UiTheme.Dim);
                loaderDetail.Text = "Waiting for the game to be found.";
                break;
            case LoaderState.NotInstalled:
                loaderDot.Set(UiTheme.Gold);
                loaderDetail.Text = "Not installed yet. Press Play and it is installed for you (it takes a few seconds).";
                loaderButton.Text = "Install";
                break;
            case LoaderState.Outdated:
                loaderDot.Set(UiTheme.Gold);
                var v = GameInstall.InstalledVersion(game);
                loaderDetail.Text = $"An older version{(v.Length > 0 ? " (" + v + ")" : "")} is installed. Press Play and it is updated to {Core.BuildInfo.Version}.";
                loaderButton.Text = "Update";
                break;
            default:
                loaderDot.Set(mods ? UiTheme.Good : UiTheme.Dim);
                var iv = GameInstall.InstalledVersion(game);
                loaderDetail.Text = mods ? $"Installed and ready{(iv.Length > 0 ? " (version " + iv + ")" : "")}." : "Installed, but turned off: the game starts without mods.";
                loaderButton.Text = "Reinstall";
                break;
        }
        loaderButton.Disabled = running;
        loaderButton.TooltipText = running ? "Close the game first." : "";

        playButton.Disabled = game == null || running;
        playButton.Text = running ? "The game is running" : "▶   Play Just Shapes & Beats";
        playHint.Text = game == null ? "The game has to be found first."
            : running ? "On the game's main menu, press F1 to open your custom levels."
            : !mods && state == LoaderState.Installed ? "Sonokinesis is turned off, so the game starts without custom levels."
            : "Starts the game through Steam. On its main menu, press F1 to open your custom levels.";
    }

    private void ChooseGame()
    {
        Hub.I.PickFolder("Choose the Just Shapes & Beats folder", dir =>
        {
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "JSB.exe"))) { Hub.I.Toast("That folder doesn't have JSB.exe in it. Choose the Just Shapes & Beats folder.", false); return; }
            EditorSettings.GameDir = dir;
            Refresh();
        });
    }

    private void SetMods(bool on)
    {
        var game = GameInstall.Game;
        if (game == null) return;
        try
        {
            GameInstall.SetModsEnabled(game, on);
            Hub.I.Toast(on ? "Sonokinesis is on: custom levels are back next time the game starts." : "Sonokinesis is off: the game starts without mods. Nothing was uninstalled.");
        }
        catch (Exception e) { Hub.I.Toast("Could not change it: " + e.Message, false); }
        Refresh();
    }

    private void InstallLoader() => Install(null);

    /// <summary>Installs or updates the loader in the background, then runs <paramref name="then"/>.</summary>
    public void Install(Action<List<string>> then)
    {
        var game = GameInstall.Game;
        if (game == null || busy) return;
        busy = true;
        loaderButton.Disabled = playButton.Disabled = true;
        loaderDetail.Text = "Installing…";
        Hub.I.Run(() => GameInstall.Install(game), log =>
        {
            busy = false;
            Refresh();
            if (then != null) then(log);
            else Hub.I.Toast(string.Join("\n", log));
        }, e =>
        {
            busy = false;
            Refresh();
            Hub.I.Toast("Could not install: " + e.Message, false);
        });
    }

    /// <summary>Makes sure Sonokinesis is installed, current and on, then runs <paramref name="then"/> (with whether BepInEx is brand new).</summary>
    public void EnsureReady(Action<bool> then, bool turnOn = true)
    {
        var state = GameInstall.State(out var game);
        if (game == null) { Hub.I.Toast("Just Shapes & Beats was not found. Choose its folder on the Play tab.", false); Hub.I.ShowPage("play"); return; }
        if (state is LoaderState.NotInstalled or LoaderState.Outdated)
        {
            if (GameInstall.GameRunning) { Hub.I.Toast("Close the game first: Sonokinesis has to be " + (state == LoaderState.Outdated ? "updated." : "installed."), false); return; }
            Install(log => then(log.Any(l => l.StartsWith("Installed BepInEx"))));
            return;
        }
        if (turnOn && !GameInstall.ModsEnabled(game))
        {
            GameInstall.SetModsEnabled(game, true);
            Hub.I.Toast("Turned Sonokinesis back on.");
        }
        then(false);
    }

    private void Play()
    {
        EnsureReady(fresh =>
        {
            GameInstall.LaunchGame();
            Hub.I.Toast(fresh
                ? "Starting the game. The first start after installing takes a minute or two while the mod loader sets itself up."
                : "Starting the game through Steam…");
        }, turnOn: false);   // the switch decides: off means the player wants the normal game
    }
}
