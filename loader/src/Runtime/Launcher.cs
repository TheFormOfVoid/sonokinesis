using System;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace Beatform.Loader.Runtime;

/// <summary>
/// Takes a level from "chosen in the list" to "playing": loads it in the background, fades to
/// black, swaps the menu out for the game's practice level flow, then fades back in.
/// </summary>
internal sealed class Launcher
{
    public enum State { Idle, Loading, FadeOut, Starting, FadeIn }

    private const float FadeSeconds = 0.35f;

    public State Current { get; private set; } = State.Idle;
    public string Error { get; private set; }
    public string LoadingTitle { get; private set; }
    public float FadeAlpha { get; private set; }

    private LevelLoader loader;
    private LoadedLevel loaded;
    private bool hard;
    private double startAtSongMs;
    private float stateTime;
    /// <summary>Until this time, keep checking that the level really plays from startAtSongMs.</summary>
    private float verifySeekUntil;
    private float nextSeekAt;
    private float inRangeSince;

    public bool Busy => Current != State.Idle;

    public void Launch(LevelEntry entry, bool hardMode, double startAtSongMs = 0)
    {
        if (Busy) return;
        Error = null;
        hard = hardMode;
        this.startAtSongMs = startAtSongMs;
        LoadingTitle = entry.Meta.Title;
        loader = new LevelLoader(entry.Folder);
        Enter(State.Loading);
    }

    private void Enter(State s)
    {
        Current = s;
        stateTime = Time.unscaledTime;
    }

    public void Update()
    {
        VerifySeek();
        float elapsed = Time.unscaledTime - stateTime;
        switch (Current)
        {
            case State.Loading:
                if (!loader.IsDone) return;
                if (loader.Error != null)
                {
                    Error = loader.Error;
                    Plugin.Logger.LogError($"could not load level: {Error}");
                    Enter(State.Idle);
                    return;
                }
                try { loaded = loader.Finish(); }
                catch (Exception e)
                {
                    Error = e.Message;
                    Plugin.Logger.LogError(e);
                    Enter(State.Idle);
                    return;
                }
                Enter(State.FadeOut);
                break;

            case State.FadeOut:
                FadeAlpha = Mathf.Clamp01(elapsed / FadeSeconds);
                if (FadeAlpha < 1) return;
                try { StartLevel(); }
                catch (Exception e)
                {
                    Error = e.Message;
                    Plugin.Logger.LogError(e);
                    CustomSession.End();
                    FadeAlpha = 0;
                    Enter(State.Idle);
                    return;
                }
                Enter(State.Starting);
                break;

            case State.Starting:
                FadeAlpha = 1;
                var logic = GameBridge.LevelLogic();
                bool ours = logic != null && CustomSession.Model != null && logic.modelSong?.id == CustomSession.Model.id;
                if (ours && GameBridge.MusicPositionMs(logic) is double)
                {
                    // The game starts the music itself shortly after this, and a relaunch briefly leaves
                    // the old level in place, so a single seek here can be undone: VerifySeek repeats it
                    // until the level really plays from the start time.
                    if (startAtSongMs > 0) { verifySeekUntil = Time.unscaledTime + 5; inRangeSince = 0; nextSeekAt = 0; }
                    Enter(State.FadeIn);
                }
                else if (elapsed > 20)
                {
                    Error = "the game did not start the level";
                    FadeAlpha = 0;
                    CustomSession.End();
                    Enter(State.Idle);
                }
                break;

            case State.FadeIn:
                FadeAlpha = 1 - Mathf.Clamp01(elapsed / FadeSeconds);
                if (FadeAlpha <= 0) Enter(State.Idle);
                break;
        }
    }

    private void StartLevel()
    {
        var model = CustomSongRegistry.Create(loaded);
        CustomSession.Begin(loaded, model);
        CloseMenus();
        var list = new List<ModelStoryCheckpoint>();
        list.Add(model);
        MainGame.instance.StartPractice(list, hard ? ModelGameDifficultyEnum.HARD : ModelGameDifficultyEnum.NORMAL);
    }

    /// <summary>Removes the menu screens the practice flow would normally close itself.</summary>
    private static void CloseMenus()
    {
        try
        {
            var list = UIBase.manager.actorList;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var a = list[i];
                if (a == null || a.destroyed) continue;
                var name = a.TryCast<Il2CppSystem.Object>().GetIl2CppType().Name;
                if (name.StartsWith("UI_MainMenu") || name == "UI_Practice" || name == "UIEffect")
                    a.destroy();
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning("closing menus: " + e.Message); }
    }

    private void VerifySeek()
    {
        if (verifySeekUntil <= 0) return;
        if (Time.unscaledTime > verifySeekUntil || loaded == null) { verifySeekUntil = 0; return; }
        if (Time.unscaledTime < nextSeekAt) return;
        var logic = GameBridge.LevelLogic();
        if (!CustomSession.Active) { verifySeekUntil = 0; return; }
        if (logic == null || logic.modelSong?.id != CustomSession.Model?.id) return;
        if (GameBridge.IsPausedOrRewinding(logic))
        {
            // The game pauses a level that starts without focus; wait for it to resume.
            verifySeekUntil = Time.unscaledTime + 5;
            return;
        }
        if (GameBridge.MusicPositionMs(logic) is not double audioMs || audioMs <= 0) return;
        double songMs = loaded.SongTime(audioMs);
        if (songMs >= startAtSongMs - 1000 && songMs < startAtSongMs + 6000)
        {
            // Done once it has stayed there for a moment (a relaunch can still restart the level).
            if (inRangeSince <= 0) inRangeSince = Time.unscaledTime;
            else if (Time.unscaledTime - inRangeSince > 1.5f) verifySeekUntil = 0;
            return;
        }
        inRangeSince = 0;
        SeekTo(logic, startAtSongMs);
        nextSeekAt = Time.unscaledTime + 0.25f;
    }

    /// <summary>Jumps the level to a song time (used by the editor's test-from-here).</summary>
    private void SeekTo(ActorNormalLevelLogic logic, double songMs)
    {
        try { logic.logicEnemyCreator.putMusicTo((float)loaded.AudioTime(songMs)); }
        catch (Exception e) { Plugin.Logger.LogWarning("seek failed: " + e.Message); }
    }
}
