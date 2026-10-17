# Sonokinesis (player download)

Play custom levels in Just Shapes & Beats. This download has only what you need to play; to make
levels, get the full Sonokinesis download with the editor.

You need Just Shapes & Beats on Steam (Windows).

## Install

1. Unzip this folder anywhere.
2. Close the game and double-click **Install-Sonokinesis.bat**. It finds the game through Steam and
   adds the BepInEx mod loader and the Sonokinesis plugin next to the game files (nothing of the game is
   replaced or changed). It also creates a `CustomLevels` folder with a demo level.
3. Start the game. The first start after installing takes a minute or two while BepInEx sets itself
   up (a console window may show its progress).

## Add levels

Drag level `.zip` files (or level folders) onto **Add-Level.bat**. That's it: they are copied into
the game's `CustomLevels` folder. Double-clicking **Add-Level.bat** opens that folder, so you can also
drop levels in there yourself.

## Play

On the game's main menu (or the playlist screen) press **F1**: the custom level list opens. Choose a
level and press **Enter**. **H** switches between normal and hard, **F5** reloads the list after you
add levels, **Esc** closes it.

## Good to know

- Your save file and Steam achievements are never changed by custom levels: saving is paused while a
  custom level runs and your progress is put back exactly as it was afterwards.
- Custom levels work in solo and local co-op. They are not available in online lobbies.
- Only play levels from people you trust: levels can contain small scripts. They run in a sandbox
  that cannot touch your files or the internet, but a broken one can still spoil a level.

## Remove

Close the game and double-click **Uninstall-Sonokinesis.bat**. Your levels are kept (delete the
`CustomLevels` folder yourself if you want them gone).

## About

Sonokinesis is free, open-source software (GPL-3.0, see LICENSE). It is an unofficial fan project, not
affiliated with or endorsed by Berzerk Studio, and contains no code, art, sound, music or levels from
Just Shapes & Beats. Just Shapes & Beats is a trademark of Berzerk Studio.
