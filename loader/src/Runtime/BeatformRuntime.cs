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
    private readonly SongClock clock = new();
    private readonly PlayerList players = new();
    private readonly List<(LevelEvent e, double started)> activeShakes = new();
    private double nextShakeKick;
    private readonly System.Random shakeRandom = new(1234);

    internal static BeatformRuntime Instance { get; private set; }
    /// <summary>The song time drawn in the last frame (for the dev console).</summary>
    internal double LastSongTime => lastSongTime;
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
        bool frozen = GameBridge.IsPausedOrRewinding(logic);
        double t = clock.Next(level.SongTime(audioMs), Time.unscaledTimeAsDouble * 1000, Time.unscaledDeltaTime * 1000, frozen);

        // A new roll of the level's random parts for every attempt: at the start and whenever playback
        // has gone back (a checkpoint restart), once it runs again.
        if (!frozen)
        {
            if (double.IsNaN(furthestSongTime) || t < furthestSongTime - 1000)
            {
                level.Evaluator.Seed = seeds.Next(1, int.MaxValue);
                furthestSongTime = t;
            }
            else furthestSongTime = Math.Max(furthestSongTime, t);
        }

        GameBridge.HideGameBackground();
        if (!renderer.Visible) renderer.SetVisible(true);
        long t0 = Settings.DevConsole ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        GameBridge.Heroes(heroes);
        UpdatePlayers(level.Evaluator);
        var heroZ = GameBridge.HeroZRange();
        long t1 = Settings.DevConsole ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        // Hazards just behind the players (players stay visible on top), flashes in front of everything.
        var items = renderer.Draw(level.Evaluator, t, GameBridge.GameplayLayerMatrix(), heroZ.Back + 0.004f, heroZ.Front - 0.004f);
        // The game draws the players over our flashes; a flash that covers the screen hides them.
        bool covered = HideCoveredHeroes && renderer.FlashCover >= 0.99f;
        if (covered != heroesHidden) { GameBridge.ShowHeroes(!covered); heroesHidden = covered; }
        long t2 = Settings.DevConsole ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        if (!frozen)
        {
            foreach (var h in heroes)
            {
                if (h.Dead || !level.Evaluator.FirstHit(items, new Vec2(h.X, h.Y), h.Radius, out var hit)) continue;
                var c = (hit.HasHitTransform ? hit.HitTransform : hit.Transform).Apply(0, 0);
                GameBridge.HitHero(h, (float)c.X, (float)c.Y);
            }
            RunShakes(level.Data, t);
            RunSounds(level, t);
        }
        ApplyCamera(level.Evaluator.CameraAt(t));
        ApplyPlayerSpeed(frozen ? 1 : PlayerSpeedOverride ?? (float)level.Evaluator.PlayerSpeedAt(t),
            frozen ? 1 : PlayerTurnOverride ?? (float)level.Evaluator.PlayerTurnAt(t));
        if (Settings.DevConsole)
        {
            double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Profile.Add((t1 - t0) * ms, (t2 - t1) * ms, (System.Diagnostics.Stopwatch.GetTimestamp() - t2) * ms);
        }
        lastSongTime = t;
    }

    /// <summary>Hands the players' positions to the level: imposters chase them, anchored objects follow them.</summary>
    [HideFromIl2Cpp]
    private void UpdatePlayers(LevelEvaluator evaluator)
    {
        players.Players.Clear();
        for (int i = 0; i < heroes.Count; i++)
        {
            var h = heroes[i];
            uint c = h.Color;
            players.Players.Add(new PlayerInfo
            {
                Number = h.Number > 0 ? h.Number : i + 1,
                Position = new Vec2(h.X, h.Y),
                Color = c != 0 ? new Rgba(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f) : LevelData.DefaultPlayerColor,
                Alive = !h.Dead,
            });
        }
        evaluator.Players = players;
    }

    /// <summary>Sound events play when playback crosses them, like shakes; jumps skip them.</summary>
    [HideFromIl2Cpp]
    private void RunSounds(LoadedLevel level, double t)
    {
        if (double.IsNaN(lastSongTime) || t <= lastSongTime || t - lastSongTime >= 250) return;
        foreach (var e in level.Data.Events)
        {
            if (e.T > t) break;
            if (e.Type == Core.EventType.Sound && e.T > lastSongTime) GameBridge.PlayGameSound(e.Sound, (float)e.Volume);
        }
        // What scripts did since the last frame: sounds, hits, teleports.
        level.Evaluator.ScriptSignals(lastSongTime, t, signals);
        foreach (var s in signals)
        {
            switch (s.Kind)
            {
                case "sound": GameBridge.PlayGameSound(s.Name, (float)s.Volume); break;
                case "hit":
                    foreach (var h in heroes) if (h.Number == s.Player && !h.Dead) GameBridge.HitHero(h, h.X, h.Y);
                    break;
                case "move": GameBridge.MoveHero(s.Player, (float)s.X, (float)s.Y); break;
            }
        }
        var errors = level.Evaluator.Scripts?.Errors;
        if (errors != null) while (reportedErrors < errors.Count) Plugin.Logger.LogWarning("script error: " + errors[reportedErrors++]);
    }

    private readonly List<ScriptSignal> signals = new();
    private int reportedErrors;

    private double furthestSongTime = double.NaN;
    private bool heroesHidden;
    internal static bool HideCoveredHeroes = true;
    private readonly System.Random seeds = new();
    private float playerSpeed = 1;
    /// <summary>Dev console: forces the players' speed and turning instead of the level's events.</summary>
    internal static float? PlayerSpeedOverride, PlayerTurnOverride;

    /// <summary>
    /// playerSpeed events: the part of each step's movement the players keep and how quickly they turn
    /// (see HeroMovementPatches), set every frame. Not while paused or rewinding.
    /// </summary>
    private void ApplyPlayerSpeed(float speed, float turn = 1)
    {
        Patches.HeroMovementPatches.Turn = turn;
        if (speed == 1 && playerSpeed == 1) return;
        Patches.HeroMovementPatches.Scale = speed;
        GameBridge.HeroPhysics(Patches.HeroMovementPatches.HeroPhysics);
        playerSpeed = speed;
    }

    private float swayRot, swayX, swayY, camScale = 1;

    /// <summary>
    /// Turns, moves and zooms the game's gameplay container by the level's sway and zoom. Turning and
    /// zoom apply only the change since the last frame; the position is shifted every frame from where
    /// the game puts it, so the game's own camera moves (kicks, shakes) keep working.
    /// </summary>
    private void ApplyCamera(CameraState cam)
    {
        float s = (float)cam.Scale, r = (float)cam.Rotation, x = (float)cam.X, y = (float)cam.Y;
        GameBridge.ShiftCamera(x, y);
        swayX = x; swayY = y;
        if (r == swayRot && s == camScale) return;
        if (GameBridge.OffsetCamera(r - swayRot, s / camScale)) { swayRot = r; camScale = s; }
    }

    private void ResetSway()
    {
        if (swayRot != 0 || camScale != 1) GameBridge.OffsetCamera(-swayRot, 1 / camScale);
        if (swayX != 0 || swayY != 0) GameBridge.ShiftCamera(0, 0);
        swayRot = swayX = swayY = 0;
        camScale = 1;
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
            // A restart or a jump back: start over, including the kick timer (which would otherwise
            // hold back every shake until playback passes where the last attempt got to).
            activeShakes.Clear();
            nextShakeKick = 0;
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

    /// <summary>Rolling cost of the loader's per-frame work, for the dev console.</summary>
    internal static class Profile
    {
        private static double hero, draw, hits;
        private static int n;
        public static void Add(double h, double d, double c) { hero += h; draw += d; hits += c; n++; }
        public static string Report()
        {
            if (n == 0) return "no frames";
            var r = $"frames={n} hero={hero / n:0.000}ms draw={draw / n:0.000}ms hits+events={hits / n:0.000}ms";
            hero = draw = hits = 0; n = 0;
            return r;
        }
    }

    private void EndSession()
    {
        renderer.SetVisible(false);
        activeShakes.Clear();
        nextShakeKick = 0;
        lastSongTime = double.NaN;
        clock.Reset();
        ResetSway();
        ApplyPlayerSpeed(1);
        furthestSongTime = double.NaN;
        reportedErrors = 0;
        if (heroesHidden) { GameBridge.ShowHeroes(true); heroesHidden = false; }
        sessionStartedAt = 0;
        sessionSeenInGame = false;
        CustomSession.End();
    }
}
