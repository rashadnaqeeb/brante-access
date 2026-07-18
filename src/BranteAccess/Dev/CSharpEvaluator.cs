#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Mono.CSharp;

namespace BranteAccess.Dev
{
    /// <summary>
    /// Wraps Mono.CSharp's REPL so the dev driver can POST arbitrary C# and run it against the live
    /// game. State persists across calls (usings and variables defined in one eval are visible to
    /// the next). <see cref="Reset"/> drops the session after a module reload so evaluated code
    /// recompiles against the freshest module generation. Ported from wotr-access, with the
    /// predicate compiler for /wait added from Non-Visual Calculus. DEBUG-only.
    ///
    /// MUST be used from the Unity main thread: evaluated code routinely touches Unity objects.
    /// The dev server enqueues code and pumps it from the per-frame tick.
    /// </summary>
    internal sealed class CSharpEvaluator
    {
        private Evaluator _evaluator;
        private StringWriter _report; // compiler diagnostics land here

        private void Initialize()
        {
            _report = new StringWriter();
            var ctx = new CompilerContext(new CompilerSettings(), new StreamReportPrinter(_report));
            _evaluator = new Evaluator(ctx);

            // Make the game + mod assemblies visible to evaluated code. Do NOT re-reference the
            // core BCL assemblies the evaluator already imports (mscorlib/System/System.Core/...):
            // adding them again imports every type twice and makes even int ambiguous. The seed set
            // doubles as the dedupe set. Same-name assemblies (module generations) keep the LAST
            // loaded, so /eval always sees the newest module.
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mscorlib", "System", "System.Core", "System.Xml", "System.Xml.Linq",
                "System.Configuration", "System.Data", "Mono.CSharp", "Mono.Security",
                "netstandard",
            };
            var byName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;
                if (string.IsNullOrEmpty(name) || asm.IsDynamic || skip.Contains(name)) continue;
                // Module generations carry unique names (BranteAccess.Module.g<stamp>, see the
                // module csproj); collapse them under one key so only the NEWEST is referenced -
                // referencing two would make every module type ambiguous in evaluated code.
                if (name.StartsWith("BranteAccess.Module", StringComparison.OrdinalIgnoreCase))
                    name = "BranteAccess.Module";
                byName[name] = asm; // later load (a newer module generation) wins
            }
            foreach (Assembly asm in byName.Values)
            {
                try { _evaluator.ReferenceAssembly(asm); }
                catch (Exception ex) { HostLog.Warning("[dev] eval could not reference " + asm.GetName().Name + ": " + ex.Message); }
            }

            try
            {
                _evaluator.Run(
                    "using System; using System.Linq; using System.Reflection; "
                    + "using System.Collections.Generic; using UnityEngine;");
            }
            catch (Exception ex)
            {
                HostLog.Warning("[dev] eval default usings failed: " + ex.Message);
            }
        }

        /// <summary>Drop the session (next Eval reinitializes and re-references assemblies), so a
        /// freshly reloaded module generation becomes visible.</summary>
        public void Reset()
        {
            _evaluator = null;
            _report = null;
        }

        /// <summary>Compile and run <paramref name="code"/>; return output + result/errors.</summary>
        public string Eval(string code)
        {
            if (_evaluator == null) Initialize();

            var output = new StringWriter();
            TextWriter origOut = Console.Out;
            TextWriter origErr = Console.Error;
            int reportStart = _report.GetStringBuilder().Length;

            object value = null;
            bool hasValue = false;
            Exception thrown = null;

            Console.SetOut(output);
            Console.SetError(output);
            try
            {
                // Evaluate consumes one statement/expression at a time and returns the unconsumed
                // remainder; loop until it's all gone (or stalls / errors).
                string input = code;
                int guard = 0;
                while (!string.IsNullOrWhiteSpace(input) && guard++ < 10000)
                {
                    string remainder;
                    object res;
                    bool resSet;
                    try { remainder = _evaluator.Evaluate(input, out res, out resSet); }
                    catch (Exception e) { thrown = e; break; }
                    if (resSet) { value = res; hasValue = true; }
                    if (_report.GetStringBuilder().Length > reportStart) break; // compile diagnostic emitted
                    if (remainder == input) break; // no progress (incomplete input) - stop to avoid a spin
                    input = remainder;
                }
            }
            finally
            {
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }

            var sb = new StringBuilder();
            string captured = output.ToString();
            if (captured.Length > 0)
            {
                sb.Append(captured);
                if (!captured.EndsWith("\n")) sb.Append('\n');
            }
            string diagnostics = _report.ToString().Substring(reportStart).Trim();
            if (diagnostics.Length > 0) sb.Append("[compile] ").Append(diagnostics).Append('\n');
            if (thrown != null) sb.Append("[exception] ").Append(thrown).Append('\n');
            if (hasValue) sb.Append("=> ").Append(value == null ? "null" : value.ToString()).Append('\n');
            if (sb.Length == 0) sb.Append("(ok)\n");
            return sb.ToString();
        }

        /// <summary>Compile a bool expression in the REPL session (it can use variables prior evals
        /// defined) for /wait's per-frame sampling. Returns null and sets the predicate on success,
        /// else an error string.</summary>
        public string CompilePredicate(string expression, out Func<bool> predicate)
        {
            predicate = null;
            if (_evaluator == null) Initialize();

            int reportStart = _report.GetStringBuilder().Length;
            CompiledMethod compiled;
            try
            {
                compiled = _evaluator.Compile(expression.Trim().TrimEnd(';') + ";");
            }
            catch (Exception e)
            {
                return "[compile exception] " + e.Message + "\n";
            }
            string diagnostics = _report.ToString().Substring(reportStart).Trim();
            if (compiled == null || diagnostics.Length > 0)
                return "[compile] " + (diagnostics.Length > 0 ? diagnostics : "predicate did not compile") + "\n";

            predicate = () =>
            {
                object result = null;
                compiled(ref result);
                return result is bool b && b;
            };
            return null;
        }
    }
}
#endif
