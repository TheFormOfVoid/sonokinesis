using System;
using System.IO;
using Godot;
using Sonokinesis.Editor.UI;

namespace Sonokinesis.Editor.Hub;

/// <summary>Settings: folders, the game window, updates, removing Sonokinesis, and about.</summary>
public partial class SettingsPage : HubPage
{
    private Label gamePath, levelsPath, updateStatus;
    private CheckButton console, mods, autoUpdate;
    private Button removeButton;

    public SettingsPage()
    {
        Body.AddChild(HubUi.PageTitle("Settings"));

        // Folders
        Body.AddChild(HubUi.Card(out var folders));
        folders.AddChild(HubUi.Heading("Folders"));
        gamePath = HubUi.Dim("", 14);
        folders.AddChild(Setting("Game folder", gamePath,
            HubUi.Secondary("Change…", () => Hub.I.PickFolder("Choose the Just Shapes & Beats folder", dir =>
            {
                if (!File.Exists(Path.Combine(dir, "JSB.exe"))) { Hub.I.Toast("That folder doesn't have JSB.exe in it.", false); return; }
                EditorSettings.GameDir = dir;
                Refresh();
            })),
            HubUi.Secondary("Find it", () =>
            {
                EditorSettings.GameDir = null;
                Hub.I.Toast(GameInstall.Game != null ? "Found the game through Steam." : "The game was not found through Steam.", GameInstall.Game != null);
                Refresh();
            }, "Look for the game in your Steam libraries again")));
        levelsPath = HubUi.Dim("", 14);
        folders.AddChild(Setting("Levels folder", levelsPath,
            HubUi.Secondary("Change…", () => WithGame(game => Hub.I.PickFolder("Choose the folder the game reads levels from", dir =>
            {
                GameInstall.SetLevelsFolder(game, dir);
                Hub.I.Toast("The game reads levels from " + dir + " now (from its next start).");
                Refresh();
            }))),
            HubUi.Secondary("Default", () => WithGame(game =>
            {
                GameInstall.SetLevelsFolder(game, "");
                Hub.I.Toast("Back to the game's CustomLevels folder.");
                Refresh();
            }), "Use the CustomLevels folder inside the game folder")));

        // Game
        Body.AddChild(HubUi.Card(out var gameCard));
        gameCard.AddChild(HubUi.Heading("Game"));
        mods = Switch("Load Sonokinesis when the game starts", on => WithGame(game =>
        {
            GameInstall.SetModsEnabled(game, on);
            Hub.I.Toast(on ? "Sonokinesis is on from the game's next start." : "Sonokinesis is off: the game starts without mods. Nothing was uninstalled.");
        }));
        gameCard.AddChild(SwitchRow(mods, "Off, the game starts exactly as it does without mods (your custom levels wait for you here)."));
        console = Switch("Show the mod loader's console window", on =>
        {
            EditorSettings.ShowConsole = on;
            WithGame(game => GameInstall.SetConsoleShown(game, on), quiet: true);
        });
        gameCard.AddChild(SwitchRow(console, "The black text window that opens next to the game. It is only useful for finding problems."));
        removeButton = HubUi.Secondary("Remove Sonokinesis from the game…", RemoveFromGame, "Takes the mod out of the game folder. Your levels are kept.");
        gameCard.AddChild(HubUi.Row(10, removeButton));

        // Updates
        Body.AddChild(HubUi.Card(out var updates));
        updates.AddChild(HubUi.Heading("Updates"));
        autoUpdate = Switch("Check for updates when Sonokinesis starts", on => EditorSettings.CheckUpdates = on);
        updates.AddChild(autoUpdate);
        updateStatus = HubUi.Dim("", 14);
        updates.AddChild(HubUi.Row(12, HubUi.Secondary("Check now", () => Hub.I.CheckForUpdates(quiet: false)), updateStatus));

        // About
        Body.AddChild(HubUi.Card(out var about));
        about.AddChild(HubUi.Heading("About"));
        about.AddChild(HubUi.Text("Sonokinesis " + Core.BuildInfo.Version, 15, UiTheme.Text, HubUi.Semi));
        about.AddChild(HubUi.Dim("Free and open-source software under the GPL-3.0 license. An unofficial fan project, not affiliated with or endorsed by Berzerk Studio; it contains no code, art, sound, music or levels from Just Shapes & Beats. Just Shapes & Beats is a trademark of Berzerk Studio.", 13));
        about.AddChild(HubUi.Row(16,
            HubUi.Link("Website and source code ↗", () => OS.ShellOpen($"https://github.com/{Core.BuildInfo.Repo}")),
            HubUi.Link("All releases ↗", () => OS.ShellOpen(Updater.ReleasesPage)),
            HubUi.Link("Report a problem ↗", () => OS.ShellOpen($"https://github.com/{Core.BuildInfo.Repo}/issues"))));
    }

    public override void _Ready()
    {
        Hub.I.UpdateStateChanged += () => updateStatus.Text = Hub.I.UpdateCheckResult;
    }

    private static Control Setting(string name, Label value, params Control[] buttons)
    {
        var text = HubUi.Column(2);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(HubUi.Text(name, 15, UiTheme.Text, HubUi.Semi));
        text.AddChild(value);
        var row = HubUi.Row(10, text);
        foreach (var b in buttons) { b.SizeFlagsVertical = SizeFlags.ShrinkCenter; row.AddChild(b); }
        return row;
    }

    private static CheckButton Switch(string text, Action<bool> changed)
    {
        var s = new CheckButton { Text = text, FocusMode = FocusModeEnum.None };
        s.AddThemeFontOverride("font", HubUi.Semi);
        s.Toggled += on => changed(on);
        return s;
    }

    private static Control SwitchRow(CheckButton s, string help)
    {
        var col = HubUi.Column(0);
        col.AddChild(s);
        var d = HubUi.Dim(help, 13);
        col.AddChild(d);
        return col;
    }

    private static void WithGame(Action<string> act, bool quiet = false)
    {
        var game = GameInstall.Game;
        if (game == null) { if (!quiet) Hub.I.Toast("Just Shapes & Beats was not found. Choose its folder first.", false); return; }
        try { act(game); } catch (Exception e) { Hub.I.Toast("Could not change it: " + e.Message, false); }
    }

    public override void Refresh()
    {
        var state = GameInstall.State(out var game);
        gamePath.Text = game ?? "Not found";
        levelsPath.Text = GameInstall.LevelsFolder(game) ?? "The game has to be found first";
        bool installed = state is LoaderState.Installed or LoaderState.Outdated;
        mods.Disabled = !installed;
        mods.SetPressedNoSignal(installed && GameInstall.ModsEnabled(game));
        console.SetPressedNoSignal(installed ? GameInstall.ConsoleShown(game) : EditorSettings.ShowConsole);
        autoUpdate.SetPressedNoSignal(EditorSettings.CheckUpdates);
        removeButton.Disabled = !installed;
        updateStatus.Text = Hub.I.UpdateCheckResult;
    }

    private void RemoveFromGame()
    {
        var game = GameInstall.Game;
        if (game == null) return;
        if (GameInstall.GameRunning) { Hub.I.Toast("Close the game first.", false); return; }
        Hub.I.Confirm("Remove from the game", "Take Sonokinesis out of the game folder? Your levels are kept, and you can put it back any time with Play.\n\n(To only turn it off for a while, use the switch above instead.)", "Remove", () =>
            Hub.I.Run(() => GameInstall.Uninstall(game), log => { Hub.I.Toast(string.Join("\n", log)); Refresh(); }, e => Hub.I.Toast("Could not remove it: " + e.Message, false)));
    }
}
