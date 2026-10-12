using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Beatform.Loader.Patches;

/// <summary>
/// playerSpeed events: the players keep their normal velocity (so the game still stretches, turns and
/// trails them as they move and dash) but only this part of each physics step's movement is kept.
/// Done in the step itself, before the game draws the player, so the shape stays centred on it.
/// </summary>
[HarmonyPatch]
internal static class HeroMovementPatches
{
    /// <summary>Part of each movement step the players keep; 1 is normal.</summary>
    public static float Scale = 1;
    /// <summary>The players' physics components, refreshed by the runtime while Scale is below 1.</summary>
    public static readonly HashSet<IntPtr> HeroPhysics = new();

    /// <summary>The part of the way to their heading the players turn each frame; 1 is at once.</summary>
    public static float Turn = 1;

    private static float fromX, fromY;
    private static Actor moving;
    private static readonly Dictionary<IntPtr, float> turned = new();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PhysicComponent), nameof(PhysicComponent.update))]
    private static void BeforeStep(PhysicComponent __instance)
    {
        moving = null;
        if (Scale >= 1 || __instance == null || !HeroPhysics.Contains(__instance.Pointer)) return;
        var actor = __instance.actor;
        if (actor == null) return;
        fromX = actor.px;
        fromY = actor.py;
        moving = actor;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PhysicComponent), nameof(PhysicComponent.update))]
    private static void AfterStep()
    {
        var actor = moving;
        if (actor == null) return;
        moving = null;
        float dx = actor.px - fromX, dy = actor.py - fromY;
        // Jumps (respawns) are not slowed.
        if (dx * dx + dy * dy < 80 * 80) actor.setPos(fromX + dx * Scale, fromY + dy * Scale);
    }

    /// <summary>
    /// The game points a player (and its stretch) along its velocity each frame; while turning is
    /// slowed, the drawn angle only moves part of the way there.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HeroRenderer), nameof(HeroRenderer.update))]
    private static void AfterRender(HeroRenderer __instance)
    {
        if (Turn >= 1)
        {
            if (turned.Count > 0) turned.Clear();
            return;
        }
        var view = __instance?.animView;
        if (view == null) return;
        float target = view.rotation;
        if (!turned.TryGetValue(__instance.Pointer, out float r)) r = target;
        float d = ((target - r) % 360 + 540) % 360 - 180;
        r += d * Turn;
        turned[__instance.Pointer] = r;
        view.rotation = r;
        var shadow = __instance.animShadowView;
        if (shadow != null) shadow.rotation = r;
    }
}
