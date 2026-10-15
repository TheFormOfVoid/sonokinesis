using System;

namespace Sonokinesis.Core;

/// <summary>Easing functions exactly as defined in spec/level-format.md.</summary>
public static class Ease
{
    private const double C1 = 1.70158, C2 = C1 * 1.525, C3 = C1 + 1, C4 = 2 * Math.PI / 3, C5 = 2 * Math.PI / 4.5;

    public enum Kind
    {
        Linear, Step,
        InSine, OutSine, InOutSine,
        InQuad, OutQuad, InOutQuad,
        InCubic, OutCubic, InOutCubic,
        InQuart, OutQuart, InOutQuart,
        InQuint, OutQuint, InOutQuint,
        InExpo, OutExpo, InOutExpo,
        InCirc, OutCirc, InOutCirc,
        InBack, OutBack, InOutBack,
        InElastic, OutElastic, InOutElastic,
        InBounce, OutBounce, InOutBounce,
    }

    public static Kind Parse(string name)
    {
        if (string.IsNullOrEmpty(name)) return Kind.Linear;
        // Spec names are lowerCamel ("outCubic"); enum names are PascalCase.
        var pascal = char.ToUpperInvariant(name[0]) + name.Substring(1);
        return Enum.TryParse<Kind>(pascal, false, out var k) ? k : Kind.Linear;
    }

    public static string Name(Kind k)
    {
        var s = k.ToString();
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }

    public static double Apply(Kind k, double p)
    {
        if (p <= 0) return k == Kind.Step ? 0 : Eval(k, 0);
        if (p >= 1) return 1;
        return Eval(k, p);
    }

    private static double Eval(Kind k, double p)
    {
        switch (k)
        {
            case Kind.Linear: return p;
            case Kind.Step: return p < 1 ? 0 : 1;
            case Kind.InSine: return 1 - Math.Cos(p * Math.PI / 2);
            case Kind.OutSine: return Math.Sin(p * Math.PI / 2);
            case Kind.InOutSine: return -(Math.Cos(Math.PI * p) - 1) / 2;
            case Kind.InQuad: return p * p;
            case Kind.OutQuad: return 1 - (1 - p) * (1 - p);
            case Kind.InOutQuad: return p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
            case Kind.InCubic: return p * p * p;
            case Kind.OutCubic: return 1 - Math.Pow(1 - p, 3);
            case Kind.InOutCubic: return p < 0.5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2;
            case Kind.InQuart: return Math.Pow(p, 4);
            case Kind.OutQuart: return 1 - Math.Pow(1 - p, 4);
            case Kind.InOutQuart: return p < 0.5 ? 8 * Math.Pow(p, 4) : 1 - Math.Pow(-2 * p + 2, 4) / 2;
            case Kind.InQuint: return Math.Pow(p, 5);
            case Kind.OutQuint: return 1 - Math.Pow(1 - p, 5);
            case Kind.InOutQuint: return p < 0.5 ? 16 * Math.Pow(p, 5) : 1 - Math.Pow(-2 * p + 2, 5) / 2;
            case Kind.InExpo: return p == 0 ? 0 : Math.Pow(2, 10 * p - 10);
            case Kind.OutExpo: return p == 1 ? 1 : 1 - Math.Pow(2, -10 * p);
            case Kind.InOutExpo:
                if (p == 0) return 0;
                if (p == 1) return 1;
                return p < 0.5 ? Math.Pow(2, 20 * p - 10) / 2 : (2 - Math.Pow(2, -20 * p + 10)) / 2;
            case Kind.InCirc: return 1 - Math.Sqrt(1 - p * p);
            case Kind.OutCirc: return Math.Sqrt(1 - (p - 1) * (p - 1));
            case Kind.InOutCirc:
                return p < 0.5 ? (1 - Math.Sqrt(1 - Math.Pow(2 * p, 2))) / 2 : (Math.Sqrt(1 - Math.Pow(-2 * p + 2, 2)) + 1) / 2;
            case Kind.InBack: return C3 * p * p * p - C1 * p * p;
            case Kind.OutBack: return 1 + C3 * Math.Pow(p - 1, 3) + C1 * Math.Pow(p - 1, 2);
            case Kind.InOutBack:
                return p < 0.5
                    ? (Math.Pow(2 * p, 2) * ((C2 + 1) * 2 * p - C2)) / 2
                    : (Math.Pow(2 * p - 2, 2) * ((C2 + 1) * (p * 2 - 2) + C2) + 2) / 2;
            case Kind.InElastic:
                if (p == 0) return 0;
                if (p == 1) return 1;
                return -Math.Pow(2, 10 * p - 10) * Math.Sin((p * 10 - 10.75) * C4);
            case Kind.OutElastic:
                if (p == 0) return 0;
                if (p == 1) return 1;
                return Math.Pow(2, -10 * p) * Math.Sin((p * 10 - 0.75) * C4) + 1;
            case Kind.InOutElastic:
                if (p == 0) return 0;
                if (p == 1) return 1;
                return p < 0.5
                    ? -(Math.Pow(2, 20 * p - 10) * Math.Sin((20 * p - 11.125) * C5)) / 2
                    : (Math.Pow(2, -20 * p + 10) * Math.Sin((20 * p - 11.125) * C5)) / 2 + 1;
            case Kind.OutBounce: return OutBounce(p);
            case Kind.InBounce: return 1 - OutBounce(1 - p);
            case Kind.InOutBounce: return p < 0.5 ? (1 - OutBounce(1 - 2 * p)) / 2 : (1 + OutBounce(2 * p - 1)) / 2;
            default: return p;
        }
    }

    private static double OutBounce(double p)
    {
        const double n1 = 7.5625, d1 = 2.75;
        if (p < 1 / d1) return n1 * p * p;
        if (p < 2 / d1) { p -= 1.5 / d1; return n1 * p * p + 0.75; }
        if (p < 2.5 / d1) { p -= 2.25 / d1; return n1 * p * p + 0.9375; }
        p -= 2.625 / d1;
        return n1 * p * p + 0.984375;
    }
}
