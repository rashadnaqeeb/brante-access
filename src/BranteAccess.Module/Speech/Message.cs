using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BranteAccess.Core;

namespace BranteAccess.Module.Speech
{
    /// <summary>
    /// A composable speech value carrying its text (raw or localized) and resolving it through a
    /// pipeline: localization lookup, {variable} substitution, rich-text strip. Ported from
    /// wotr-access. Template vars are stored as Messages so any Message passed in resolves lazily
    /// at the parent's Resolve time. Compose with the + operator or Message.Join. Unity-free by
    /// design so wording logic stays unit-testable.
    /// </summary>
    public class Message
    {
        /// <summary>table, key → localized string (or null). Wired by LocalizationManager.Initialize.</summary>
        public static System.Func<string, string, string> LocalizationResolver { get; set; }

        private static readonly Regex VariablePattern = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);

        private readonly string _rawText;
        private readonly string _table;
        private readonly string _key;
        private readonly Dictionary<string, Message> _vars;
        private readonly List<Message> _parts;
        private readonly string _separator;

        private Message(string rawText, string table, string key, Dictionary<string, Message> vars)
        {
            _rawText = rawText; _table = table; _key = key; _vars = vars;
        }

        private Message(List<Message> parts, string separator)
        {
            _parts = parts; _separator = separator;
        }

        public static Message Raw(string text) => new Message(text, null, null, null);
        public static Message Raw(string text, object vars) => new Message(text, null, null, ObjectToDict(vars));
        public static Message MaybeRaw(string text) => text == null ? null : new Message(text, null, null, null);

        public static Message Localized(string table, string key) => new Message(null, table, key, null);
        public static Message Localized(string table, string key, object vars) => new Message(null, table, key, ObjectToDict(vars));

        public static readonly Message Empty = new Message(string.Empty, null, null, null);

        /// <summary>Join messages with a separator; null/empty parts skipped.</summary>
        public static Message Join(string separator, params Message[] parts)
        {
            var list = new List<Message>();
            if (parts != null)
                foreach (var p in parts)
                    if (p != null && !p.IsEmpty) list.Add(p);
            return list.Count == 0 ? Empty : new Message(list, separator);
        }

        /// <summary>Compose two messages, space-joined.</summary>
        public static Message operator +(Message left, Message right)
        {
            var parts = new List<Message>();
            Flatten(left, parts);
            Flatten(right, parts);
            return new Message(parts, " ");
        }

        public bool IsEmpty
        {
            get
            {
                if (_parts != null) return _parts.Count == 0;
                if (_rawText != null) return string.IsNullOrEmpty(_rawText);
                return false; // localized - unknown without resolving
            }
        }

        public string Resolve() => Resolve(strip: true);

        /// <summary>Resolve localization + {variable} substitution but DO NOT strip rich-text markup.
        /// For callers that mine the markup (links, styling); speech always uses <see cref="Resolve()"/>.</summary>
        public string ResolveRaw() => Resolve(strip: false);

        private string Resolve(bool strip)
        {
            if (_parts != null) return ResolveComposite(strip);

            string text = _rawText != null
                ? _rawText
                : (LocalizationResolver != null ? LocalizationResolver(_table, _key) : null) ?? _key ?? "";

            if (_vars != null && _vars.Count > 0)
                text = SubstituteVars(text, _vars);

            return strip ? TextUtil.StripRichText(text) : text;
        }

        public override string ToString() => Resolve();

        private string ResolveComposite(bool strip)
        {
            var sb = new StringBuilder();
            foreach (var part in _parts)
            {
                var r = part.Resolve(strip);
                if (string.IsNullOrEmpty(r)) continue;
                if (sb.Length > 0) sb.Append(_separator);
                sb.Append(r);
            }
            return sb.ToString();
        }

        private static void Flatten(Message msg, List<Message> into)
        {
            if (msg._parts != null) { foreach (var p in msg._parts) Flatten(p, into); }
            else into.Add(msg);
        }

        private static string SubstituteVars(string text, Dictionary<string, Message> vars)
        {
            return VariablePattern.Replace(text, m =>
                vars.TryGetValue(m.Groups[1].Value, out var v) ? v.Resolve() : m.Value);
        }

        private static Dictionary<string, Message> ObjectToDict(object obj)
        {
            var dict = new Dictionary<string, Message>();
            foreach (var prop in obj.GetType().GetProperties())
                dict[prop.Name] = WrapValue(prop.GetValue(obj));
            return dict;
        }

        private static Message WrapValue(object val)
        {
            if (val == null) return Empty;
            if (val is Message m) return m;
            return Raw(val.ToString());
        }
    }
}
