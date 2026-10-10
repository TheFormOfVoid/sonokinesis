using System.Globalization;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppSystem.Collections;

namespace Beatform.Loader.Dev;

/// <summary>Serializes Il2Cpp Hashtable / ArrayList / boxed primitive trees to JSON for inspection.</summary>
public static class Il2CppJson
{
    public static string ToJson(Il2CppObjectBase obj, int maxDepth = 8)
    {
        var sb = new StringBuilder();
        Write(sb, obj, 0, maxDepth);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, Il2CppObjectBase obj, int depth, int maxDepth)
    {
        if (obj == null) { sb.Append("null"); return; }
        var o = obj.TryCast<Il2CppSystem.Object>();
        if (o == null) { sb.Append("\"<?>\""); return; }
        var typeName = o.GetIl2CppType().FullName;
        if (depth > maxDepth) { sb.Append("\"<").Append(typeName).Append(">\""); return; }

        var ht = o.TryCast<Hashtable>();
        if (ht != null)
        {
            sb.Append('{');
            var e = ht.GetEnumerator().Cast<IDictionaryEnumerator>();
            bool first = true;
            while (e.Cast<IEnumerator>().MoveNext())
            {
                if (!first) sb.Append(',');
                first = false;
                Str(sb, e.Key?.ToString() ?? "null");
                sb.Append(':');
                Write(sb, e.Value, depth + 1, maxDepth);
            }
            sb.Append('}');
            return;
        }
        var al = o.TryCast<ArrayList>();
        if (al != null)
        {
            sb.Append('[');
            for (int i = 0; i < al.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Write(sb, al[i], depth + 1, maxDepth);
            }
            sb.Append(']');
            return;
        }
        switch (typeName)
        {
            case "System.String": Str(sb, o.ToString()); return;
            case "System.Boolean": sb.Append(o.ToString() == "True" ? "true" : "false"); return;
            case "System.Int32": case "System.Int64": case "System.Single": case "System.Double":
            case "System.Int16": case "System.UInt16": case "System.UInt32": case "System.Byte":
                sb.Append(o.ToString().Replace(',', '.')); return;
        }
        Str(sb, "<" + typeName + "> " + o.ToString());
    }

    private static void Str(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
