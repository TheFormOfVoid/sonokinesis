using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Sonokinesis.Loader.Dev;

/// <summary>
/// Development console driven by files: write a command to BepInEx/sonokinesis/cmd.txt and the
/// result appears in BepInEx/sonokinesis/out.txt. Used to explore live game state while building
/// the loader. Runs on Unity's main thread.
/// </summary>
public class DevConsole : MonoBehaviour
{
    public DevConsole(IntPtr ptr) : base(ptr) { }

    private string cmdPath, outPath;
    private float nextPoll;
    private static Assembly[] gameAssemblies;
    public static readonly Dictionary<string, Func<string[], string>> Commands = new();

    private void Awake()
    {
        cmdPath = Path.Combine(Plugin.DataDir, "cmd.txt");
        outPath = Path.Combine(Plugin.DataDir, "out.txt");
        gameAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name is "Assembly-CSharp" or "Assembly-CSharp-firstpass"
                or "UnityEngine.CoreModule" or "Il2Cppmscorlib" or "Il2CppSystem" or "UnityEngine.AudioModule")
            .ToArray();
        RegisterBuiltins();
        GameCommands.Register();
    }

    private void Update()
    {
        GameCommands.Tick();
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.25f;
        if (!File.Exists(cmdPath)) return;
        string text;
        try { text = File.ReadAllText(cmdPath); File.Delete(cmdPath); }
        catch { return; }

        var sb = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            sb.Append("> ").Append(line).Append('\n');
            try { sb.Append(Run(line)).Append('\n'); }
            catch (Exception e) { sb.Append("!! ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n').Append(e.StackTrace).Append('\n'); }
        }
        File.WriteAllText(outPath, sb.ToString());
    }

    public static bool GuiTest;
    private static string guiError;

    private void OnGUI()
    {
        if (!GuiTest) return;
        try
        {
            GUI.color = Color.white;
            GUI.Box(new Rect(40, 40, 420, 120), "Sonokinesis IMGUI test");
            GUI.Label(new Rect(60, 80, 380, 30), "Label works. Time " + Time.unscaledTime.ToString("0.0"));
            var style = new GUIStyle(GUI.skin.label) { fontSize = 28 };
            style.normal.textColor = new Color(1f, 0.16f, 0.43f);
            GUI.Label(new Rect(60, 110, 380, 40), "Styled label", style);
            GUI.DrawTexture(new Rect(480, 40, 60, 60), Texture2D.whiteTexture);
        }
        catch (Exception e) { guiError = e.GetType().Name + ": " + e.Message; GuiTest = false; }
    }

    private static string Run(string line)
    {
        var sp = line.IndexOf(' ');
        var name = sp < 0 ? line : line[..sp];
        var rest = sp < 0 ? "" : line[(sp + 1)..].Trim();
        if (!Commands.TryGetValue(name, out var fn)) return $"unknown command '{name}'. known: {string.Join(", ", Commands.Keys)}";
        return fn(rest.Length == 0 ? Array.Empty<string>() : SplitArgs(rest));
    }

    private static string[] SplitArgs(string s)
    {
        var list = new List<string>(); var cur = new StringBuilder(); bool q = false;
        foreach (var c in s)
        {
            if (c == '"') { q = !q; continue; }
            if (c == ' ' && !q) { if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); } continue; }
            cur.Append(c);
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list.ToArray();
    }

    private static void RegisterBuiltins()
    {
        Commands["ping"] = _ => "pong " + Time.unscaledTime;
        Commands["guitest"] = a => { GuiTest = a.Length == 0 || a[0] != "off"; return "gui test " + GuiTest + (guiError != null ? " last error: " + guiError : ""); };
        Commands["get"] = a => Fmt(Eval(a[0]), 0);
        Commands["props"] = a => Props(Eval(a[0]));
        Commands["methods"] = a => Methods(a[0]);
        Commands["list"] = a =>
        {
            var max = a.Length > 1 ? int.Parse(a[1]) : 50;
            var sub = a.Length > 2 ? a[2] : null;
            var sb = new StringBuilder(); int i = 0;
            foreach (var item in Enumerate(Eval(a[0])))
            {
                if (i >= max) { sb.Append("...\n"); break; }
                object shown = item;
                if (sub != null) { try { shown = EvalOn(item, sub); } catch (Exception e) { shown = "!" + e.Message; } }
                sb.Append(i).Append(": ").Append(Fmt(shown, 0)).Append('\n');
                i++;
            }
            return sb.Append($"({i} items)").ToString();
        };
        Commands["json"] = a => Il2CppJson.ToJson(Eval(a[0]) as Il2CppObjectBase, a.Length > 1 ? int.Parse(a[1]) : 6);
        Commands["save"] = a =>
        {
            // save <relative file> <command...>: run a command and write its full output to a file.
            var file = Path.Combine(Plugin.DataDir, a[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var res = Run(string.Join(" ", a.Skip(1).Select(x => x.Contains(' ') ? $"\"{x}\"" : x)));
            File.WriteAllText(file, res);
            return $"wrote {res.Length} chars to {file}";
        };
    }

    // ---------- tiny reflection evaluator: Type.Member.Member[3].Method() ----------

    public static object Eval(string expr)
    {
        var parts = SplitPath(expr);
        // Find the longest prefix that names a type.
        for (int n = parts.Count; n >= 1; n--)
        {
            var typeName = string.Join(".", parts.Take(n));
            var t = FindType(typeName);
            if (t == null) continue;
            object cur = null; Type curType = t;
            foreach (var p in parts.Skip(n)) { cur = Step(cur, curType, p); curType = cur?.GetType(); if (cur == null) break; }
            return n == parts.Count ? t : cur;
        }
        throw new Exception($"no type found in '{expr}'");
    }

    public static object EvalOn(object root, string path)
    {
        object cur = root;
        foreach (var p in SplitPath(path)) { if (cur == null) return null; cur = Step(cur, cur.GetType(), p); }
        return cur;
    }

    private static List<string> SplitPath(string expr)
    {
        var res = new List<string>(); var cur = new StringBuilder(); int depth = 0;
        foreach (var c in expr)
        {
            if (c == '(' || c == '[') depth++;
            if (c == ')' || c == ']') depth--;
            if (c == '.' && depth == 0) { res.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(c);
        }
        if (cur.Length > 0) res.Add(cur.ToString());
        return res;
    }

    public static Type FindType(string name)
    {
        foreach (var a in gameAssemblies)
        {
            var t = a.GetType(name, false);
            if (t != null) return t;
        }
        foreach (var a in gameAssemblies)
        {
            try
            {
                var t = a.GetTypes().FirstOrDefault(x => x.Name == name || x.FullName == name);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    private static object Step(object target, Type type, string seg)
    {
        if (seg.StartsWith("@"))
        {
            // @TypeName casts an Il2Cpp object to a more derived interop type.
            var castTo = FindType(seg[1..]) ?? throw new Exception($"no type {seg[1..]}");
            var tryCast = typeof(Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(castTo);
            return tryCast.Invoke(target, null);
        }
        int? index = null;
        var br = seg.IndexOf('[');
        if (br >= 0) { index = int.Parse(seg[(br + 1)..seg.IndexOf(']')]); seg = seg[..br]; }
        object val;
        if (seg.EndsWith(")"))
        {
            var name = seg[..seg.IndexOf('(')];
            var argStr = seg[(seg.IndexOf('(') + 1)..^1];
            var args = argStr.Length == 0 ? Array.Empty<string>() : argStr.Split(',').Select(s => s.Trim()).ToArray();
            var m = type.GetMethods(Any).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length)
                ?? throw new Exception($"no method {type.Name}.{name}/{args.Length}");
            var conv = m.GetParameters().Select((p, i) => ConvertArg(args[i], p.ParameterType)).ToArray();
            val = m.Invoke(m.IsStatic ? null : target, conv);
        }
        else
        {
            var prop = type.GetProperty(seg, Any);
            if (prop != null && prop.GetIndexParameters().Length == 0) val = prop.GetValue(prop.GetMethod.IsStatic ? null : target);
            else
            {
                var f = type.GetField(seg, Any) ?? throw new Exception($"no member {type.Name}.{seg}");
                val = f.GetValue(f.IsStatic ? null : target);
            }
        }
        if (index.HasValue) val = Enumerate(val).ElementAt(index.Value);
        return val;
    }

    private static object ConvertArg(string s, Type t)
    {
        if (t == typeof(string)) return s;
        if (t == typeof(int)) return int.Parse(s);
        if (t == typeof(float)) return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        if (t == typeof(bool)) return bool.Parse(s);
        if (t == typeof(ushort)) return ushort.Parse(s);
        return Eval(s);
    }

    public static IEnumerable<object> Enumerate(object coll)
    {
        if (coll == null) yield break;
        var t = coll.GetType();
        var countProp = t.GetProperty("length", Any) ?? t.GetProperty("Count", Any) ?? t.GetProperty("Length", Any);
        var item = t.GetProperties(Any).FirstOrDefault(p => p.Name == "Item" && p.GetIndexParameters().Length == 1 && p.GetIndexParameters()[0].ParameterType == typeof(int));
        if (countProp == null || item == null) throw new Exception($"{t.Name} is not indexable");
        int n = Convert.ToInt32(countProp.GetValue(coll));
        for (int i = 0; i < n; i++) yield return item.GetValue(coll, new object[] { i });
    }

    // ---------- formatting ----------

    public static string Fmt(object v, int depth)
    {
        if (v == null) return "null";
        if (v is Type tt) return "type " + tt.FullName;
        if (v is string s) return "\"" + s + "\"";
        if (v.GetType().IsPrimitive || v is decimal) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        if (v is Vector2 v2) return $"({v2.x:0.###}, {v2.y:0.###})";
        if (v is Vector3 v3) return $"({v3.x:0.###}, {v3.y:0.###}, {v3.z:0.###})";
        if (v is Matrix4x4 m4) return $"[{m4.m00:0.###} {m4.m01:0.###} {m4.m02:0.###} {m4.m03:0.###} | {m4.m10:0.###} {m4.m11:0.###} {m4.m12:0.###} {m4.m13:0.###} | {m4.m20:0.###} {m4.m21:0.###} {m4.m22:0.###} {m4.m23:0.###}]";
        if (v is Rect rc) return $"Rect(x={rc.x:0.##}, y={rc.y:0.##}, w={rc.width:0.##}, h={rc.height:0.##})";
        if (v is Color col) return $"Color({col.r:0.###}, {col.g:0.###}, {col.b:0.###}, {col.a:0.###})";
        if (v is Il2CppSystem.String ils) return "\"" + ils.ToString() + "\"";
        var t = v.GetType();
        var label = t.Name;
        try
        {
            if (v is Il2CppObjectBase ob)
            {
                var real = ob.TryCast<Il2CppSystem.Object>()?.GetIl2CppType()?.Name;
                if (real != null && real != t.Name) label += "<" + real + ">";
            }
        }
        catch { }
        foreach (var key in new[] { "id", "name", "songName", "className" })
        {
            var p = t.GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || p.GetIndexParameters().Length > 0) continue;
            try { var pv = p.GetValue(v); if (pv != null) { label += $" {key}={pv}"; break; } } catch { }
        }
        if (t.GetProperty("length", Any) != null || t.GetProperty("Count", Any) != null)
        {
            try { label += $" (n={Enumerate(v).Count()})"; } catch { }
        }
        return label;
    }

    private static string Props(object v)
    {
        if (v == null) return "null";
        var isType = v is Type;
        var t = isType ? (Type)v : v.GetType();
        var sb = new StringBuilder(Fmt(v, 0)).Append('\n');
        foreach (var p in t.GetProperties(Any).OrderBy(p => p.Name))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var getter = p.GetMethod; if (getter == null) continue;
            if (isType != getter.IsStatic) continue;
            if (p.Name is "ObjectClass" or "Pointer" or "WasCollected" or "Il2CppType" or "isWrapped" or "pooledPtr") continue;
            string s; try { s = Fmt(p.GetValue(isType ? null : v), 1); } catch (Exception e) { s = "!" + (e.InnerException?.Message ?? e.Message); }
            sb.Append("  ").Append(p.Name).Append(" = ").Append(s).Append('\n');
        }
        return sb.ToString();
    }

    private static string Methods(string expr)
    {
        var v = Eval(expr);
        var t = v as Type ?? v.GetType();
        var sb = new StringBuilder();
        foreach (var m in t.GetMethods(Any).Where(m => !m.IsSpecialName).OrderBy(m => m.Name))
            sb.Append($"  {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})\n");
        return sb.ToString();
    }
}
