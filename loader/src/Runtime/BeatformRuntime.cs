using System;
using System.Collections.Generic;
using Beatform.Core;
using Beatform.Loader.UI;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace Beatform.Loader.Runtime;

/// <summary>
/// The loader's per-frame driver: runs the level list and launcher, follows the active custom
/// session, and while the custom level plays draws it and hands hits to the game.
/// </summary>
public class BeatformRuntime : MonoBehaviour
{
    public BeatformRuntime(IntPtr ptr) : base(ptr) { }

    private HazardRenderer renderer;
    private Launcher launcher;
    private LevelMenu menu;
    private readonly List<GameBridge.HeroCircle> heroes = new();

    private bool sessionSeenInGame;
    private float sessionStartedAt;
    private double lastSongTime = double.NaN;
    private readonly List<(LevelEvent e, double started)> activeShakes = new();
    private double nextShakeKick;
    private readonly System.Random shakeRandom = new(1234);

    internal static BeatformRuntime Instance { get; private set; }
    [HideFromIl2Cpp] internal Launcher Launcher => launcher;
    [HideFromIl2Cpp] internal LevelMenu Menu => menu;

    private void Awake()
    {
        Instance = this;
        renderer = new HazardRenderer(transform);
        launcher = new Launcher();
        menu = new LevelMenu(launcher);
        LevelLibrary.Rescan();
    }

    private void Update()
    {
        try
        {
            launcher.Update();
            menu.Update();
            EditorBridge.Update(launcher);
        }
        catch (Exception e) { Plugin.Logger.LogError(e); }
    }

    private void LateUpdate()
    {
        try { TickSession(); }
        catch (Exception e) { Plugin.Logger.LogError(e); }
    }

    private void OnGUI()
    {
        try { menu.OnGUI(); }
        catch (Exception e) { Plugin.Logger.LogError(e); }
    }

    private void TickSession()
    {
        if (!CustomSession.Active)
        {
            if (renderer.Visible) renderer.SetVisible(false);
            sessionSeenInGame = false;
            return;
        }
        if (sessionStartedAt <= 0) sessionStartedAt = Time.unscaledTime;

        var logic = GameBridge.LevelLogic();
        bool ours = logic != null && logic.modelSong != null && logic.modelSong.id == CustomSession.Model.id;
        if (!ours)
        {
            // Left the level (back to a menu), or it never started.
            if (sessionSeenInGame || Time.unscaledTime - sessionStartedAt > 30) EndSession();
            else if (renderer.Visible) renderer.SetVisible(false);
            return;
        }
        sessionSeenInGame = true;

        var level = CustomSession.Level;
        var music = GameBridge.MusicPositionMs(logic);
        if (music is not double audioMs)
        {
            // The song is over (finish triangle, results): keep only the background.
            renderer.DrawBackgroundOnly(level.Evaluator, lastSongTime);
            return;
        }
        double t = level.SongTime(audioMs);

        GameBridge.HideGameBackground();
        if (!renderer.Visible) renderer.SetVisible(true);
        float heroZ = GameBridge.FrontHeroZ();
        var items = renderer.Draw(level.Evaluator, t, GameBridge.GameplayLayerMatrix(), heroZ - 0.004f);

        bool frozen = GameBridge.IsPausedOrRewinding(logic);
        if (!frozen)
        {
            GameBridge.Heroes(heroes);
            foreach (var h in heroes)
            {
                if (!level.Evaluator.Hits(items, new Vec2(h.X, h.Y), h.Radius, out var by)) continue;
                var c = LevelEvaluator.TransformAt(by, t).Apply(0, 0);
                GameBridge.HitHero(h, (float)c.X, (float)c.Y);
            }
            RunShakes(level.Data, t);
        }
        lastSongTime = t;
    }

    /// <summary>Shake events fire when playback crosses their start; jumps (rewinds, seeks) skip them.</summary>
    [HideFromIl2Cpp]
    private void RunShakes(LevelData data, double t)
    {
        if (!double.IsNaN(lastSongTime) && t > lastSongTime && t - lastSongTime < 250)
        {
            foreach (var e in data.Events)
                if (e.Type == Core.EventType.Shake && e.T > lastSongTime && e.T <= t) activeShakes.Add((e, e.T));
        }
        else if (double.IsNaN(lastSongTime) || t < lastSongTime)
        {
            activeShakes.Clear();
        }
        if (activeShakes.Count == 0 || t < nextShakeKick) return;
        nextShakeKick = t + 50;
        for (int i = activeShakes.Count - 1; i >= 0; i--)
        {
            var (e, started) = activeShakes[i];
            double p = e.Duration > 0 ? (t - started) / e.Duration : 1;
            if (p >= 1) { activeShakes.RemoveAt(i); continue; }
            int force = (int)Math.Round(e.Intensity * (1 - p));
            if (force > 0) GameBridge.CameraKick((float)(shakeRandom.NextDouble() * 360), force);
        }
    }

    private void EndSession()
    {
        renderer.SetVisible(false);
        activeShakes.Clear();
        lastSongTime = double.NaN;
        sessionStartedAt = 0;
        sessionSeenInGame = false;
        CustomSession.End();
    }
}
