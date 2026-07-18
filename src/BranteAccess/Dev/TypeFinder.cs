#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BranteAccess.Dev
{
    /// <summary>
    /// The /typeinfo lookup: find a type by simple name across every loaded assembly and print its
    /// full name, assembly, and public surface. Kills namespace guessing in the REPL - one round
    /// trip instead of one per guess. Ported from Non-Visual Calculus (interop loading dropped;
    /// Mono loads game assemblies eagerly).
    /// </summary>
    internal static class TypeFinder
    {
        private const int MaxMatches = 25;
        private const int MaxMemberLines = 250;

        public static string Describe(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "[empty] pass ?name=TypeName\n";
            name = name.Trim();

            List<Type> matches = Search(name);
            if (matches.Count == 0)
                return "[none] no loaded type named '" + name + "'\n";

            var sb = new StringBuilder();
            foreach (Type t in matches.Take(MaxMatches))
                sb.Append(t.FullName).Append(" (").Append(t.Assembly.GetName().Name).Append(")\n");
            if (matches.Count > MaxMatches)
                sb.Append("... ").Append(matches.Count - MaxMatches).Append(" more\n");

            if (matches.Count == 1)
                AppendMembers(sb, matches[0]);
            return sb.ToString();
        }

        // Exact simple-name match (case-insensitive).
        private static List<Type> Search(string name)
        {
            var found = new List<Type>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch (Exception ex)
                {
                    HostLog.Warning("[dev] typeinfo: could not read types from " + asm.GetName().Name + ": " + ex.Message);
                    continue;
                }
                foreach (Type t in types)
                    if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                        found.Add(t);
            }
            return found;
        }

        // The type's public surface, compact: enum values inline; otherwise declared fields,
        // properties, and methods (accessors skipped), one per line, capped.
        private static void AppendMembers(StringBuilder sb, Type t)
        {
            if (t.IsEnum)
            {
                sb.Append("enum: ");
                string[] names = Enum.GetNames(t);
                Array values = Enum.GetValues(t);
                var parts = new List<string>(names.Length);
                for (int i = 0; i < names.Length; i++)
                    parts.Add(names[i] + "=" + Convert.ToInt64(values.GetValue(i)));
                sb.Append(string.Join(" ", parts)).Append('\n');
                return;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly;
            int lines = 0;

            foreach (FieldInfo f in t.GetFields(flags))
            {
                if (++lines > MaxMemberLines) { sb.Append("...\n"); return; }
                sb.Append("  field ").Append(f.IsStatic ? "static " : "").Append(f.Name)
                  .Append(" : ").Append(Short(f.FieldType)).Append('\n');
            }
            foreach (PropertyInfo p in t.GetProperties(flags))
            {
                if (++lines > MaxMemberLines) { sb.Append("...\n"); return; }
                sb.Append("  prop ").Append(p.Name).Append(" : ").Append(Short(p.PropertyType)).Append('\n');
            }
            foreach (MethodInfo m in t.GetMethods(flags))
            {
                if (m.IsSpecialName) continue; // property/event accessors
                if (++lines > MaxMemberLines) { sb.Append("...\n"); return; }
                sb.Append("  ").Append(m.IsStatic ? "static " : "").Append(m.Name).Append('(')
                  .Append(string.Join(", ", m.GetParameters().Select(pi => Short(pi.ParameterType))))
                  .Append(") : ").Append(Short(m.ReturnType)).Append('\n');
            }
        }

        private static string Short(Type t) => t.IsGenericType
            ? t.Name.Split('`')[0] + "<" + string.Join(", ", t.GetGenericArguments().Select(Short)) + ">"
            : t.Name;
    }
}
#endif
