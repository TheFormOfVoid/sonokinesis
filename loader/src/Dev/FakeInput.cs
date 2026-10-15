using HarmonyLib;

namespace Sonokinesis.Loader.Dev;

/// <summary>
/// Development only: overrides the local players' stick and dash button, to test how the players
/// move without a person at the controls. Commands: fakeinput X Y [dash] · fakeinput off.
/// </summary>
internal static class FakeInput
{
    private static bool active, dash;
    private static float x, y;

    public static void Apply(Harmony harmony)
    {
        try
        {
            harmony.Patch(AccessTools.Method(typeof(HeroControl), nameof(HeroControl.applyMoveInputToHero)),
                prefix: new HarmonyMethod(typeof(FakeInput), nameof(BeforeMove)));
            harmony.Patch(AccessTools.Method(typeof(HeroDashComponent), nameof(HeroDashComponent.inputForDash)),
                postfix: new HarmonyMethod(typeof(FakeInput), nameof(Dash)));
        }
        catch (System.Exception e) { Plugin.Logger.LogWarning("fake input: " + e.Message); }

        DevConsole.Commands["fakeinput"] = a =>
        {
            if (a.Length == 0 || a[0] == "off") { active = false; return "fake input off"; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            x = float.Parse(a[0], inv);
            y = a.Length > 1 ? float.Parse(a[1], inv) : 0;
            dash = a.Length > 2 && a[2] == "dash";
            active = true;
            return $"fake input ({x}, {y}) dash={dash}";
        };
    }

    private static void BeforeMove(HeroControl __instance)
    {
        if (!active || __instance?.moveInput == null) return;
        __instance.moveInput.x = x;
        __instance.moveInput.y = y;
    }

    private static void Dash(ref bool __result) { if (active && dash) __result = true; }
}
