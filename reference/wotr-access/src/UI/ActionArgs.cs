using System;

namespace WrathAccess.UI
{
    /// <summary>
    /// Reads named values out of an action's anonymous-object args bag
    /// (e.g. <c>new { value = 50 }</c>), with a graceful default + log on miss.
    /// </summary>
    public static class ActionArgs
    {
        public static T Get<T>(object args, string name, T defaultValue = default)
        {
            if (args == null) return defaultValue;
            var prop = args.GetType().GetProperty(name);
            if (prop == null)
            {
                Main.Log?.Log("ActionArgs: missing arg '" + name + "'");
                return defaultValue;
            }
            var value = prop.GetValue(args);
            if (value is T typed) return typed;
            try { return (T)Convert.ChangeType(value, typeof(T)); }
            catch { return defaultValue; }
        }
    }
}
