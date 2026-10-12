using System;
using System.Collections.Generic;
using UnityEngine;

namespace Beatform.Loader.Runtime;

/// <summary>Read-mostly access to live game state, with every call guarded against missing objects.</summary>
internal static class GameBridge
{
    public struct HeroCircle
    {
        public float X, Y, Radius;
        public CollisionCircleComponent Component;
        /// <summary>1 to 4, from the player's model (0 if unknown).</summary>
        public int Number;
        /// <summary>The player's colour as 0xRRGGBB.</summary>
        public uint Color;
        public bool Dead;
    }

    public static ActorNormalLevelLogic LevelLogic()
    {
        try
        {
            var scene = MainGame.instance?.gameSceneManager?.gameScene;
            if (scene == null) return null;
            return ActorNormalLevelLogic.getNormalLevelLogic();
        }
        catch { return null; }
    }

    /// <summary>The music position of the running level in ms, or null when no level music runs.</summary>
    public static double? MusicPositionMs(ActorNormalLevelLogic logic)
    {
        try
        {
            var music = logic?.logicEnemyCreator?.sfxView;
            return music == null ? null : music.position;
        }
        catch { return null; }
    }

    public static bool IsPausedOrRewinding(ActorNormalLevelLogic logic)
    {
        try
        {
            if (logic.isPaused()) return true;
            var rw = logic.logicRewindMusic;
            return rw != null && rw.isRewinding();
        }
        catch { return true; }
    }

    public static void Heroes(List<HeroCircle> output)
    {
        output.Clear();
        try
        {
            var cols = Hero.allCollisionEnemy;
            if (cols == null) return;
            for (int i = 0; i < cols.length; i++)
            {
                var c = cols[i];
                var actor = c?.actor;
                if (actor == null || actor.destroyed) continue;
                var circle = c.circleCol;
                var info = new HeroCircle
                {
                    X = actor.px + (circle?.x ?? 0),
                    Y = actor.py + (circle?.y ?? 0),
                    Radius = circle?.radius ?? 8,
                    Component = c,
                };
                try
                {
                    var hero = actor.TryCast<Hero>();
                    var model = hero?.modelPlayer;
                    if (model != null) { info.Number = model.getPlayerNumber(); info.Color = model.color; }
                    info.Dead = hero?.lifeComponent?.isDead ?? false;
                }
                catch { }
                output.Add(info);
            }
        }
        catch { }
    }

    /// <summary>The physics components that move the players (for playerSpeed events).</summary>
    public static void HeroPhysics(HashSet<IntPtr> output)
    {
        output.Clear();
        try
        {
            var cols = Hero.allCollisionEnemy;
            for (int i = 0; cols != null && i < cols.length; i++)
            {
                var physics = cols[i]?.actor?.physicComponent;
                if (physics != null) output.Add(physics.Pointer);
            }
        }
        catch { }
    }

    private static Actor attacker;

    /// <summary>
    /// Hands a hit to the game: an attack the size of a dot at the player's center, sent by an actor
    /// standing where the hazard is. The game applies damage, knockback and invincibility rules.
    /// </summary>
    public static void HitHero(HeroCircle hero, float fromX, float fromY)
    {
        try
        {
            attacker ??= new Actor();
            attacker.setPos(fromX, fromY);
            CollisionCircleComponent.attackTargets(attacker, Hero.allCollisionEnemy, hero.X, hero.Y, 0.5f, 0, 0);
        }
        catch (Exception e) { Plugin.Logger.LogWarning("hit failed: " + e.Message); }
    }

    /// <summary>Concatenated matrix of the gameplay hazard layer: follows camera moves and kicks.</summary>
    public static Matrix4x4 GameplayLayerMatrix()
    {
        try
        {
            var layers = CameraFlashContainers.instance;
            return layers?.enemyVisual?.transform?.concatenedMatrix ?? Matrix4x4.identity;
        }
        catch { return Matrix4x4.identity; }
    }

    /// <summary>
    /// Adds to the gameplay camera container's rotation (degrees) and position (game units). The
    /// container holds players, game effects and the layer our hazards follow.
    /// </summary>
    public static bool OffsetCamera(float rotation, float x, float y)
    {
        try
        {
            var c = CameraFlashContainers.instance?.container;
            if (c == null) return false;
            c.rotation += rotation;
            c.x += x;
            c.y += y;
            return true;
        }
        catch { return false; }
    }

    // Level sound names and the game sounds they play (the game's own audio, loaded by the game).
    private static readonly Dictionary<string, string> soundNames = new()
    {
        // What the game itself plays when a player dies, is hit, or comes back (checked in game).
        ["playerDeath"] = "ID_SFX_EXPLOSION_5",
        ["playerHit"] = "ID_SFX_HIT_1",
        ["playerRespawn"] = "ID_SFX_POWERUP_2",
        ["explosion"] = "SFX_PORTAL_EXPLOSION_V01",
    };

    /// <summary>Plays one of the game's sounds by level sound name (or the game's own sound id).</summary>
    public static void PlayGameSound(string name, float volume)
    {
        try
        {
            if (name == null) return;
            var field = soundNames.TryGetValue(name, out var id) ? id : name;
            var prop = typeof(ModelSoundEnum).GetProperty(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop?.GetValue(null) is ModelSound sound) JukeBox.playSound(sound, volume);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"sound {name}: {e.Message}"); }
    }

    /// <summary>Hides the game's background art layers so the level's own background shows.</summary>
    public static void HideGameBackground()
    {
        try
        {
            var l = CameraFlashContainers.instance;
            if (l == null) return;
            if (l.mapBackVisual != null) l.mapBackVisual.visible = false;
            if (l.backgroundVisual != null) l.backgroundVisual.visible = false;
            if (l.fake3dVisual != null) l.fake3dVisual.visible = false;
        }
        catch { }
    }

    // Until a player sprite has been seen, sit just in front of the background.
    private static float lastHeroZ = -0.015f;

    /// <summary>
    /// Depth of the front-most player sprite. The game assigns sprite depth in draw order, and its own
    /// hazards come right after the players, so Beatform draws just in front of this value.
    /// </summary>
    public static float FrontHeroZ() => HeroZRange().Front;

    private static float lastHeroBackZ = -0.015f;

    /// <summary>
    /// Depth of the nearest and the farthest drawn part of any player (smaller z is nearer). Hazards
    /// go just behind the farthest part so players stay visible on top of them, as in the game.
    /// </summary>
    public static (float Front, float Back) HeroZRange()
    {
        float front = float.MaxValue, back = float.MinValue;
        try
        {
            var cols = Hero.allCollisionEnemy;
            for (int i = 0; cols != null && i < cols.length; i++)
            {
                var hero = cols[i]?.actor?.TryCast<Hero>();
                var root = hero?.heroRenderer?.heroContainerMc;
                if (root != null) ShapeZRange(root, ref front, ref back, 0);
            }
        }
        catch { }
        if (front != float.MaxValue) { lastHeroZ = front; lastHeroBackZ = back; }
        return (lastHeroZ, lastHeroBackZ);
    }

    private static void ShapeZRange(DisplayObject d, ref float front, ref float back, int depth)
    {
        if (d == null || depth > 6) return;
        if (d.hasMetaGo)
        {
            var go = d.metaSpriteGo;
            if (go != null && go.transform != null)
            {
                float z = go.transform.position.z;
                if (z < front) front = z;
                if (z > back) back = z;
            }
        }
        var c = d.TryCast<DisplayObjectContainer>();
        if (c == null) return;
        for (var ch = c.firstChild; ch != null; ch = ch.nextSibling) ShapeZRange(ch, ref front, ref back, depth + 1);
    }

    public static void CameraKick(float angleDeg, int force)
    {
        try { CameraFlash.mainCamera?.kickback(angleDeg, force); } catch { }
    }

    /// <summary>The live UI actor of the given game UI type, or null.</summary>
    public static T FindUi<T>() where T : UIBase
    {
        try
        {
            var list = UIBase.manager.actorList;
            var name = typeof(T).Name;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null || a.destroyed) continue;
                if (a.TryCast<Il2CppSystem.Object>().GetIl2CppType().Name != name) continue;
                return a.TryCast<T>();
            }
        }
        catch { }
        return null;
    }

    public static bool InMainMenu() => FindUi<UI_MainMenu02>() != null;

    /// <summary>True while the game is in an online mode; custom levels are solo and local co-op only.</summary>
    public static bool InOnlineMode()
    {
        try { return MetaGameProgress.instance?.modelTypeOfGame?.isOnline() == true; }
        catch { return false; }
    }

    /// <summary>True on the main menu or the playlist screen: places where a level may be started.</summary>
    public static bool InMenu() => InMainMenu() || FindUi<UI_Practice>() != null;

    /// <summary>Redraws the playlist screen so it shows restored numbers (beat points).</summary>
    public static void RefreshMenus()
    {
        try { FindUi<UI_Practice>()?.refresh(); } catch { }
    }
}
