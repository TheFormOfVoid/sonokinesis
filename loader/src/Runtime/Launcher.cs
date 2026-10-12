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
        CustomSession.StartSongMs = startAtSongMs;
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
        FadeMenuSources();
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
        SilenceMenuAudio();
        CloseMenus();
        var list = new List<ModelStoryCheckpoint>();
        list.Add(model);
        MainGame.instance.StartPractice(list, hard ? ModelGameDifficultyEnum.HARD : ModelGameDifficultyEnum.NORMAL);
    }

    /// <summary>
    /// Fades out the menu music and any song preview, as the playlist screen does before it starts a
    /// song. Only what plays now is touched; the level's own music starts afterwards.
    /// </summary>
    private static void SilenceMenuAudio()
    {
        try
        {
            var musics = JukeBox.allDynamicMusics;
            for (int i = (musics?.length ?? 0) - 1; i >= 0; i--) musics[i]?.fadeToDestroy(20);
        }
        catch (Exception e) { Plugin.Logger.LogWarning("fading the menu music: " + e.Message); }
        try
        {
            var list = UIBase.manager.actorList;
            for (int i = 0; i < list.Count; i++)
            {
                var practice = list[i]?.TryCast<UI_Practice>();
                if (practice != null && !practice.destroyed) practice.StopPreview();
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning("stopping the preview: " + e.Message); }
        try
        {
            var sounds = JukeBox.allSounds;
            for (int i = (sounds?.length ?? 0) - 1; i >= 0; i--)
            {
                var sfx = sounds[i];
                var view = sfx?.sfxView;
                if (view != null && view.isPlaying && (view.looping || view.bindToMusicVolume)) JukeBox.fadeOutPreview(sfx);
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning("fading menu sounds: " + e.Message); }
        try
        {
            // Menu loops belong to the menu screens' own actors, so also fade every music-like sound
            // that is playing now (looping, or longer than a few seconds). Short UI sounds keep playing.
            fadingSources.Clear();
            foreach (var src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
            {
                if (src == null || !src.isPlaying) continue;
                var clip = src.clip;
                if (src.loop || (clip != null && clip.length > 8f)) fadingSources.Add((src, src.volume));
            }
            fadeStarted = Time.unscaledTime;
        }
        catch (Exception e) { Plugin.Logger.LogWarning("fading menu audio sources: " + e.Message); }
    }

    private static readonly System.Collections.Generic.List<(AudioSource src, float volume)> fadingSources = new();
    private static float fadeStarted;
    private const float MenuFadeSeconds = 0.4f;

    /// <summary>Fades the menu sounds picked by <see cref="SilenceMenuAudio"/>, then stops them.</summary>
    private static void FadeMenuSources()
    {
        if (fadingSources.Count == 0) return;
        float p = Mathf.Clamp01((Time.unscaledTime - fadeStarted) / MenuFadeSeconds);
        for (int i = fadingSources.Count - 1; i >= 0; i--)
        {
            var (src, vol) = fadingSources[i];
            try
            {
                if (src == null) { fadingSources.RemoveAt(i); continue; }
                src.volume = vol * (1 - p);
                if (p >= 1) { src.Stop(); src.volume = vol; fadingSources.RemoveAt(i); }
            }
            catch { fadingSources.RemoveAt(i); }
        }
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
