using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Beatform.Loader;

/// <summary>User-facing settings, stored in BepInEx/config/dev.beatform.loader.cfg.</summary>
internal static class Settings
{
    private static ConfigEntry<string> levelsFolder;
    private static ConfigEntry<KeyCode> menuKey;
    private static ConfigEntry<bool> blockAllSaves;
    private static ConfigEntry<bool> devConsole;

    public static string LevelsFolder
    {
        get
        {
            var v = levelsFolder.Value;
            if (string.IsNullOrWhiteSpace(v)) v = Path.Combine(Paths.GameRootPath, "CustomLevels");
            return v;
        }
    }

    public static KeyCode MenuKey => menuKey.Value;
    public static bool BlockAllSaves => blockAllSaves.Value;
    public static bool DevConsole => devConsole.Value;

    public static void Bind(ConfigFile config)
    {
        levelsFolder = config.Bind("General", "LevelsFolder", "",
            "Folder that holds custom levels. Empty means <game folder>/CustomLevels.");
        menuKey = config.Bind("General", "MenuKey", KeyCode.F1,
            "Key that opens the custom level list from the main menu.");
        blockAllSaves = config.Bind("Development", "BlockAllSaves", false,
            "Never write the save file, even outside custom levels. For testing the loader.");
        devConsole = config.Bind("Development", "DevConsole", false,
            "Enable the file-driven development console (BepInEx/beatform/cmd.txt).");
    }
}
