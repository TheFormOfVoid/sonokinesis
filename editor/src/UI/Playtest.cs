using System;
using System.Collections.Generic;
using System.Linq;
using Beatform.Core;
using Beatform.Editor.Audio;
using Godot;

namespace Beatform.Editor.UI;

/// <summary>
/// Play the level inside the editor with a stand-in player: move with WASD or arrows, dash with Space
/// or Shift. Hits use the same collision as the game. Dying rewinds to the last checkpoint, like the
/// game. Movement numbers are close to the game's feel but are not taken from it.
/// </summary>
public sealed class Playtest
{
    private static EditorState S => EditorState.I;

    private const float Speed = 420;          // units per second
    private const float DashSpeed = 1350;
    private const float DashTime = 0.13f;
    private const float DashCooldown = 0.38f;
    private const float HitInvincibility = 1.4f;
    private const float Radius = 8;           // same circle the game uses for players
    private const int Lives = 3;

    private readonly SongPlayer song;
    public bool Active { get; private set; }
    private Vector2 pos;
    private Vector2 dashDir;
    private float dashLeft, dashCooldown, invincible, hitFlash;
    private int hp;
    public int Hits { get; private set; }
    public int Deaths { get; private set; }
    private double lastCheckpoint;
    private readonly List<Vector2> trail = new();

    public Playtest(SongPlayer song) { this.song = song; }

    public void Start(double fromMs)
    {
        Active = true;
        pos = new Vector2(-380, 0);
        hp = Lives;
        Hits = 0;
        Deaths = 0;
        invincible = 1;
        lastCheckpoint = S.Doc.Checkpoints.Where(c => c <= fromMs).DefaultIfEmpty(0).Max();
        trail.Clear();
        App.Instance.StartPlayback(fromMs);
    }

    public void Stop()
    {
        if (!Active) return;
        Active = false;
        App.Instance.StopPlayback();
    }

    public void Step(double delta, List<DrawItem> items)
    {
        float dt = (float)delta;
        double t = S.Playhead;
        foreach (var c in S.Doc.Checkpoints) if (c <= t && c > lastCheckpoint) lastCheckpoint = c;

        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (dir != Vector2.Zero) dir = dir.Normalized();

        dashCooldown -= dt;
        bool dashPressed = Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.Shift);
        if (dashPressed && dashCooldown <= 0 && dashLeft <= 0)
        {
            dashDir = dir == Vector2.Zero ? Vector2.Right : dir;
            dashLeft = DashTime;
            dashCooldown = DashCooldown;
        }
        Vector2 vel;
        if (dashLeft > 0) { dashLeft -= dt; vel = dashDir * DashSpeed; }
        else vel = dir * Speed;
        pos += vel * dt;
        pos.X = Mathf.Clamp(pos.X, -640 + Radius, 640 - Radius);
        pos.Y = Mathf.Clamp(pos.Y, -360 + Radius, 360 - Radius);
        trail.Add(pos);
        if (trail.Count > 8) trail.RemoveAt(0);

        invincible -= dt;
        hitFlash -= dt;
        bool dashing = dashLeft > 0;
        if (invincible <= 0 && !dashing && S.Evaluator.Hits(items, new Vec2(pos.X, pos.Y), Radius, out _))
        {
            Hits++;
            hp--;
            invincible = HitInvincibility;
            hitFlash = 0.25f;
            if (hp <= 0)
            {
                Deaths++;
                hp = Lives;
                invincible = 1.5f;
                pos = new Vector2(-380, 0);
                App.Instance.StartPlayback(lastCheckpoint);
            }
        }
        if (!S.Playing) Stop();
    }

    public void Draw(CanvasView view)
    {
        if (hitFlash > 0)
        {
            var screen = new Rect2(view.GameToView(-640, -360), new Vector2(1280, 720) * (view.GameToView(1, 0).X - view.GameToView(0, 0).X));
            view.DrawRect(screen, new Color(1, 0.16f, 0.43f, hitFlash * 1.6f));
        }
        float s = view.GameToView(1, 0).X - view.GameToView(0, 0).X;
        for (int i = 0; i < trail.Count; i++)
        {
            float a = (i + 1f) / trail.Count * 0.25f;
            var tp = view.GameToView(trail[i].X, trail[i].Y);
            view.DrawRect(new Rect2(tp - new Vector2(10, 10) * s, new Vector2(20, 20) * s), new Color(UiTheme.Cyan, a));
        }
        bool blink = invincible > 0 && (int)(invincible * 12) % 2 == 0;
        var p = view.GameToView(pos.X, pos.Y);
        if (!blink) view.DrawRect(new Rect2(p - new Vector2(11, 11) * s, new Vector2(22, 22) * s), UiTheme.Cyan);
        view.DrawCircle(p, Radius * s, new Color(1, 1, 1, 0.5f), false, 1);

        var font = view.GetThemeDefaultFont();
        string hud = $"PLAYTEST   lives {new string('■', hp)}{new string('□', Lives - hp)}   hits {Hits}   deaths {Deaths}   WASD move · SPACE dash · ESC stop";
        view.DrawString(font, new Vector2(14, 24), hud, HorizontalAlignment.Left, -1, 15, UiTheme.Cyan);
    }
}
