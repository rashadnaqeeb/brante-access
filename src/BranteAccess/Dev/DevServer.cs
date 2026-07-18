#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using BranteAccess.Core.Modularity;
using UnityEngine;

namespace BranteAccess.Dev
{
    /// <summary>
    /// The dev driver: a loopback HTTP server that lets an out-of-process agent drive the live game
    /// and read back what the mod SPOKE - the mod's only real output. Endpoint set ported from
    /// Non-Visual Calculus, implemented on the wotr-access Mono stack (Mono.CSharp REPL, raw TCP
    /// server). DEBUG builds only.
    ///
    /// Threading: the HTTP thread never touches Unity. Game-touching endpoints enqueue a job that
    /// <see cref="MainThreadTick"/> (called from HostPump.Update) executes on the Unity main thread;
    /// the HTTP thread blocks on the job's event. /speech and /log read their ring buffers directly
    /// off-thread. /wait registers a predicate the tick samples once per frame.
    ///
    /// All endpoint text is dev tooling - exempt from the no-inline-strings rule.
    /// </summary>
    internal sealed class DevServer
    {
        private const int DefaultPort = 8772;
        private const int JobTimeoutMs = 30000;      // main-thread job wait (eval can be slow)
        private const int SettleQuietMs = 350;       // /eval: speech is "settled" after this quiet gap
        private const int SettleMaxMs = 3000;        // /eval: hard cap on waiting for speech
        private const int DefaultWaitTimeoutMs = 10000;

        private readonly Plugin _plugin;
        private readonly string _modDir;
        private readonly int _port;

        private readonly LineLog _speechLog = new LineLog();
        private readonly LineLog _logLog = new LineLog();
        private readonly CSharpEvaluator _evaluator = new CSharpEvaluator();

        private DevHttpServer _http;
        private DevLogListener _logListener;
        private int _lastEvalGeneration = -1;
        private long _frame;

        // Main-thread job queue: HTTP thread enqueues, MainThreadTick drains.
        private sealed class Job
        {
            public Func<string> Run;
            public string Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }
        private readonly Queue<Job> _jobs = new Queue<Job>();

        // Predicates /wait registered; sampled once per frame on the main thread.
        private sealed class PendingWait
        {
            public Func<bool> Predicate;
            public volatile bool Satisfied;
            public volatile string Error;
            public long StartFrame;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }
        private readonly List<PendingWait> _waits = new List<PendingWait>();

        public DevServer(Plugin plugin, string modDir)
        {
            _plugin = plugin;
            _modDir = modDir;
            string portVar = Environment.GetEnvironmentVariable("BRANTE_DEV_PORT");
            _port = int.TryParse(portVar, out int p) ? p : DefaultPort;
        }

        public void Start()
        {
            // The driver polls and evals with the game window unfocused; without this Unity pauses
            // the player loop on focus loss and every main-thread job would hang.
            Application.runInBackground = true;

            _plugin.Speech.Observer = (text, interrupt) =>
                _speechLog.Add(interrupt ? "[interrupt] " + text : text);

            _logListener = new DevLogListener(_logLog);
            BepInEx.Logging.Logger.Listeners.Add(_logListener);

            _http = new DevHttpServer(_port, Handle);
            _http.Start();
            HostLog.Info("[dev] server listening on http://127.0.0.1:" + _port);
        }

        public void Stop()
        {
            _http?.Stop();
            _http = null;
            if (_logListener != null)
            {
                BepInEx.Logging.Logger.Listeners.Remove(_logListener);
                _logListener = null;
            }
            if (_plugin.Speech != null) _plugin.Speech.Observer = null;
        }

        /// <summary>Called once per frame from HostPump on the Unity main thread.</summary>
        public void MainThreadTick()
        {
            _frame++;

            while (true)
            {
                Job job;
                lock (_jobs)
                {
                    if (_jobs.Count == 0) break;
                    job = _jobs.Dequeue();
                }
                try { job.Result = job.Run(); }
                catch (Exception e) { job.Result = "[error] " + e + "\n"; }
                job.Done.Set();
            }

            lock (_waits)
            {
                for (int i = _waits.Count - 1; i >= 0; i--)
                {
                    PendingWait w = _waits[i];
                    try
                    {
                        if (!w.Predicate()) continue;
                        w.Satisfied = true;
                    }
                    catch (Exception e)
                    {
                        w.Error = e.Message;
                    }
                    _waits.RemoveAt(i);
                    w.Done.Set();
                }
            }
        }

        /// <summary>Run <paramref name="fn"/> on the main thread; block the HTTP thread for the result.</summary>
        private string OnMainThread(Func<string> fn)
        {
            var job = new Job { Run = fn };
            lock (_jobs) _jobs.Enqueue(job);
            if (!job.Done.Wait(JobTimeoutMs))
                return "[timeout] main thread did not run the job in " + JobTimeoutMs
                    + "ms (game paused or frozen?)\n";
            return job.Result;
        }

        // ---- routing ----

        private string Handle(string method, string rawPath, string body)
        {
            int q = rawPath.IndexOf('?');
            string path = q >= 0 ? rawPath.Substring(0, q) : rawPath;
            Dictionary<string, string> query = ParseQuery(q >= 0 ? rawPath.Substring(q + 1) : "");

            switch (path)
            {
                case "/health": return Health();
                case "/eval": return EvalWithSpeechSettle(body);
                case "/input": return OnMainThread(() => Driver(d => d.DispatchUi(body.Trim()),
                    r => r ?? "[unknown verb] " + body.Trim() + "\n"));
                case "/nav": return OnMainThread(() => Driver(d => d.DescribeNav(), r => r));
                case "/type": return OnMainThread(() => Driver(d => d.TypeText(body),
                    r => r ?? "[no text entry active]\n"));
                case "/wait": return Wait(body, query);
                case "/speech": return ReadRing(_speechLog, query);
                case "/log": return ReadLog(query);
                case "/gui": return OnMainThread(GuiInspector.Describe);
                case "/focus": return OnMainThread(FocusInspector.Describe);
                case "/typeinfo": return TypeFinder.Describe(query.TryGetValue("name", out string n) ? n : "");
                case "/screenshot": return OnMainThread(Screenshot);
                case "/reload": return Reload();
                case "/module": return OnMainThread(() => ModuleInspector.Describe(_plugin.Loader));
                case "/mute": return Mute(method, body);
                default: return "[404] no route " + path + "; routes: /health /eval /input /nav /type"
                    + " /wait /speech /log /gui /focus /typeinfo /screenshot /reload /module /mute\n";
            }
        }

        // ---- endpoints ----

        private string Health()
        {
            var module = _plugin.Loader.Module;
            return "ok " + Plugin.Name + " " + Plugin.Version
                + " module=" + (module == null ? "NOT LOADED" : "generation " + _plugin.Loader.Generation)
                + " enabled=" + _plugin.Enabled
                + " frame=" + Interlocked.Read(ref _frame)
                + " muted=" + _plugin.Speech.Muted
                + " speechCursor=" + _speechLog.End
                + " logCursor=" + _logLog.End + "\n";
        }

        /// <summary>POST /mute: body "on" stops speech from reaching the user's screen reader
        /// while the dev tap keeps capturing (so /speech still verifies); "off" restores it.
        /// GET reports the state. For driving the game while the user works elsewhere.</summary>
        private string Mute(string method, string body)
        {
            if (method != "POST") return "muted=" + _plugin.Speech.Muted + "\n";
            string want = (body ?? "").Trim().ToLowerInvariant();
            if (want != "on" && want != "off") return "[bad body] POST 'on' or 'off'\n";
            bool mute = want == "on";
            return OnMainThread(() =>
            {
                // Cut off anything mid-utterance before muting (Silence no-ops once Muted is set).
                if (mute && !_plugin.Speech.Muted) _plugin.Speech.Silence();
                _plugin.Speech.Muted = mute;
                HostLog.Info("[dev] speech backends " + (mute ? "muted" : "unmuted"));
                return "muted=" + mute + "\n";
            });
        }

        /// <summary>POST /eval: run C# on the main thread, then wait (off-thread) until speech the
        /// action triggered has settled, and append it. Act-then-listen in one round trip.</summary>
        private string EvalWithSpeechSettle(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "[empty] POST the C# code as the body\n";

            long speechBefore = _speechLog.End;
            string result = OnMainThread(() =>
            {
                // A module reload (F6 or /reload) invalidates types compiled into the REPL session;
                // drop it so evaluated code resolves against the newest generation.
                int generation = _plugin.Loader.Generation;
                if (generation != _lastEvalGeneration)
                {
                    _evaluator.Reset();
                    _lastEvalGeneration = generation;
                }
                return _evaluator.Eval(code);
            });

            string speech = CaptureSettledSpeech(speechBefore);
            if (speech.Length > 0)
                result += "[speech]\n" + speech;
            return result;
        }

        /// <summary>Collect speech lines from <paramref name="since"/> until a quiet gap of
        /// <see cref="SettleQuietMs"/> (capped at <see cref="SettleMaxMs"/> total). Runs on the HTTP
        /// thread; announcements arrive from game frames, not from us.</summary>
        private string CaptureSettledSpeech(long since)
        {
            var sb = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(SettleMaxMs);
            long cursor = since;
            while (true)
            {
                double left = (deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) break;
                if (!_speechLog.WaitForNew(cursor, (int)Math.Min(SettleQuietMs, left)))
                    break; // quiet gap: settled
                sb.Append(_speechLog.Render(cursor, out cursor));
            }
            return sb.ToString();
        }

        /// <summary>POST /wait: body is a bool C# expression (REPL session scope), sampled once per
        /// frame on the main thread until true or ?timeout= ms pass.</summary>
        private string Wait(string body, Dictionary<string, string> query)
        {
            if (string.IsNullOrWhiteSpace(body)) return "[empty] POST the bool expression as the body\n";
            int timeoutMs = query.TryGetValue("timeout", out string t) && int.TryParse(t, out int tv)
                ? tv : DefaultWaitTimeoutMs;

            var wait = new PendingWait { StartFrame = -1 };
            string compileError = OnMainThread(() =>
            {
                string error = _evaluator.CompilePredicate(body, out Func<bool> predicate);
                if (error != null) return error;
                wait.Predicate = predicate;
                wait.StartFrame = _frame;
                // Sample immediately: an already-true predicate returns without waiting a frame.
                try
                {
                    if (predicate()) { wait.Satisfied = true; wait.Done.Set(); return null; }
                }
                catch (Exception e)
                {
                    wait.Error = e.Message; wait.Done.Set(); return null;
                }
                lock (_waits) _waits.Add(wait);
                return null;
            });
            if (compileError != null) return compileError;

            bool signaled = wait.Done.Wait(timeoutMs);
            if (!signaled)
            {
                lock (_waits) _waits.Remove(wait);
                return "[timeout] still false after " + timeoutMs + "ms\n";
            }
            if (wait.Error != null) return "[predicate threw] " + wait.Error + "\n";
            long frames = Interlocked.Read(ref _frame) - wait.StartFrame;
            return "[true] after " + frames + " frame(s)\n";
        }

        /// <summary>GET /speech (and the core of /log): ?since=N cursor, ?wait=MS long-poll.</summary>
        private static string ReadRing(LineLog log, Dictionary<string, string> query)
        {
            long since = query.TryGetValue("since", out string s) && long.TryParse(s, out long sv) ? sv : 0;
            if (query.TryGetValue("wait", out string w) && int.TryParse(w, out int waitMs))
                log.WaitForNew(since, waitMs);
            string rendered = log.Render(since, out long next);
            return rendered + "next: " + next + "\n";
        }

        private string ReadLog(Dictionary<string, string> query)
        {
            string rendered = ReadRing(_logLog, query);
            if (!query.TryGetValue("grep", out string grep) || string.IsNullOrEmpty(grep))
                return rendered;
            var sb = new StringBuilder();
            string[] lines = rendered.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                bool isCursor = i == lines.Length - 2 && lines[i].StartsWith("next: ");
                if (isCursor || lines[i].IndexOf(grep, StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.Append(lines[i]).Append('\n');
            }
            return sb.ToString();
        }

        private string Screenshot()
        {
            // The raw-TCP server only speaks text, so write the PNG to disk and return its path.
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                string file = Path.Combine(_modDir, "screenshot.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                return file + "\n";
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        private string Reload()
        {
            return OnMainThread(() =>
            {
                // A failed reload leaves the old module live, so "is a module loaded" cannot
                // signal failure - only a generation bump proves the swap happened.
                int before = _plugin.Loader.Generation;
                _plugin.ReloadModule();
                if (_plugin.Loader.Generation == before)
                    return "[failed] reload did not swap, still generation " + before
                        + "; GET /log?grep=ModuleLoader\n";
                return "reloaded: " + _plugin.Loader.Module.GetType().FullName
                    + " generation " + _plugin.Loader.Generation + "\n";
            });
        }

        /// <summary>Route a call to the live module's IDevDriver, probed by cast per request so it
        /// always hits the newest generation.</summary>
        private string Driver(Func<IDevDriver, string> call, Func<string, string> render)
        {
            if (_plugin.Loader.Module is IDevDriver driver)
                return render(call(driver));
            return "[no driver] module " + (_plugin.Loader.Module == null
                ? "is not loaded" : "does not implement IDevDriver") + "\n";
        }

        // ---- helpers ----

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string key = eq >= 0 ? pair.Substring(0, eq) : pair;
                string value = eq >= 0 ? pair.Substring(eq + 1) : "";
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            return result;
        }
    }
}
#endif
