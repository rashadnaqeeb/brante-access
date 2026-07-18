#if DEBUG
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using WrathAccess.Input;
using WrathAccess.Speech;
using WrathAccess.UI;

namespace WrathAccess.Dev
{
    /// <summary>
    /// Dev-only in-process driver, gated behind the WRATHACCESS_DEV env var. Exposes a loopback HTTP
    /// server so an external driver (Claude, curl) can introspect and drive the live mod/game:
    ///   POST /eval           body = C# source, run against the live game (REPL state persists across
    ///                        calls); returns captured output + result/errors.
    ///   GET  /speech?since=N lines the mod has spoken since cursor N (we can't hear the TTS, so this is
    ///                        how we observe it). Tapped at the SpeechManager chokepoint.
    ///   GET  /health         liveness.
    ///
    /// Eval runs on the Unity main thread: HTTP requests enqueue a job and block until <see cref="Pump"/>
    /// (called once per frame from Main.OnFrame) executes it. /speech reads a thread-safe buffer directly
    /// off the HTTP thread.
    ///
    /// This whole subsystem is compiled only in DEBUG (#if DEBUG) — a Release build has none of it, so it
    /// cannot be toggled on by anything. Even in Debug it stays inert unless WRATHACCESS_DEV=1.
    /// </summary>
    internal sealed class DevServer
    {
        public static readonly DevServer Instance = new DevServer();

        public const string EnableEnv = "WRATHACCESS_DEV";
        public const string PortEnv = "WRATHACCESS_DEV_PORT";
        public const string MarkerFile = "devserver.enable"; // under persistentDataPath/WrathAccess/
        private const int DefaultPort = 8771; // Tangledeep's dev server uses 8770; keep ours distinct.

        // Enabled by the env var OR a marker file the dev launcher drops. The marker is immune to HOW the
        // game is launched: a Steam relaunch spawns a fresh process that doesn't inherit our $env: var
        // (observed — the server returned early at the env gate while the mod itself loaded fine), whereas
        // the file is read from persistentDataPath regardless. Still DEBUG-only, so neither exists in Release.
        private static bool DevEnabled(out string how)
        {
            how = null;
            if (Environment.GetEnvironmentVariable(EnableEnv) == "1") { how = "env"; return true; }
            try
            {
                string marker = System.IO.Path.Combine(
                    UnityEngine.Application.persistentDataPath, "WrathAccess", MarkerFile);
                if (System.IO.File.Exists(marker)) { how = "marker"; return true; }
            }
            catch { }
            return false;
        }

        private sealed class Job
        {
            public Func<string> Work;
            public string Result = "";
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private readonly SpeechLog _speech = new SpeechLog();
        private readonly CSharpEvaluator _evaluator = new CSharpEvaluator();
        private readonly ConcurrentQueue<Job> _jobs = new ConcurrentQueue<Job>();
        private DevHttpServer _http;
        private bool _enabled;

        /// <summary>Stand up the server if WRATHACCESS_DEV=1; otherwise stay inert.</summary>
        public void Start()
        {
            string how;
            if (!DevEnabled(out how)) return;

            // Keep the Unity player loop (and thus our main-thread Pump, and thus /eval) running while the
            // game is unfocused — otherwise the loop freezes the moment our terminal takes focus and eval
            // jobs never execute. WotR's AutoPauseController pauses game LOGIC on focus loss but not the
            // loop, and nothing in the game writes runInBackground, so setting it true here (during focused
            // boot, before any focus loss) and re-asserting each Pump holds. DEBUG/dev-only behavior.
            UnityEngine.Application.runInBackground = true;

            int port = DefaultPort;
            string p = Environment.GetEnvironmentVariable(PortEnv);
            if (!string.IsNullOrEmpty(p)) int.TryParse(p, out port);

            // Tap every string the mod speaks through the SpeechManager chokepoint into the ring buffer.
            SpeechManager.Observer = _speech.Add;

            try
            {
                _http = new DevHttpServer(port, HandleRequest);
                _http.Start();
                _enabled = true;
                Main.Log?.Log("Dev server on http://127.0.0.1:" + port + " (gate: " + how + "; POST /eval, GET /speech)");
            }
            catch (Exception e)
            {
                Main.Log?.Error("Dev server failed to start: " + e);
            }
        }

        /// <summary>Run queued main-thread jobs. Call once per frame from the tick.</summary>
        public void Pump()
        {
            if (!_enabled) return;
            UnityEngine.Application.runInBackground = true; // re-assert each frame (cheap insurance vs any reset)
            Job job;
            while (_jobs.TryDequeue(out job))
            {
                try { job.Result = job.Work() ?? ""; }
                catch (Exception e) { job.Result = "[host error] " + e + "\n"; }
                job.Done.Set();
            }
        }

        /// <summary>Run <paramref name="work"/> on the main thread (next Pump) and block for its result.</summary>
        private string OnMainThread(Func<string> work, int timeoutSeconds = 30)
        {
            var job = new Job { Work = work };
            _jobs.Enqueue(job);
            if (!job.Done.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
                return "[timeout] main thread did not run the job within " + timeoutSeconds + "s (frozen / not pumping?)\n";
            return job.Result;
        }

        // Runs on the HTTP thread.
        private string HandleRequest(string method, string path, string body)
        {
            string route = path;
            string query = "";
            int q = path.IndexOf('?');
            if (q >= 0) { route = path.Substring(0, q); query = path.Substring(q + 1); }

            if (route == "/eval" && method == "POST")
            {
                if (string.IsNullOrWhiteSpace(body)) return "[empty] POST C# source as the request body\n";
                return OnMainThread(() => _evaluator.Eval(body));
            }

            if (route == "/gui" && method == "GET")
                return OnMainThread(() => GuiInspector.Dump());

            if (route == "/input" && method == "POST")
            {
                string verb = (body ?? "").Trim();
                return OnMainThread(() => Inject(verb));
            }

            if (route == "/screenshot" && method == "GET")
                return Screenshot();

            if (route == "/loadsave" && method == "POST")
                return LoadSave(body);

            if (route == "/speech" && method == "GET")
            {
                long since = 0;
                foreach (string kv in query.Split('&'))
                    if (kv.StartsWith("since=", StringComparison.Ordinal))
                        long.TryParse(kv.Substring("since=".Length), out since);
                long next;
                string lines = _speech.Render(since, out next);
                return "cursor: " + next + "\n" + lines;
            }

            if (route == "/health" || route == "/") return "ok\n";

            return "[404] " + method + " " + route + "\n";
        }

        // Fire one of our InputActions by key, exactly as InputManager.Tick routes a real press: a UI action
        // goes to the navigator; anything else fires its handler. Lets the dev driver drive nav (ui.down,
        // ui.activate, ui.next…) and global hotkeys. Unknown key → list what's available. Main-thread only.
        private static string Inject(string key)
        {
            foreach (var a in InputManager.Actions)
            {
                if (a.Key != key) continue;
                bool consumed = a.Category == InputCategory.UI && Navigation.DispatchJustPressed(a);
                if (!consumed) a.InvokePerformed();
                return "fired " + key + (consumed ? " (navigator)" : " (handler)") + "\n";
            }
            var sb = new StringBuilder("[unknown action] " + key + "\navailable:\n");
            foreach (var a in InputManager.Actions) sb.Append("  ").Append(a.Key).Append('\n');
            return sb.ToString();
        }

        // Load a save from the main menu and BLOCK until the gameplay scene is interactive, so the driver
        // can script "drop me in-game" in one call. body = "latest" (default) | "quick" | an index into the
        // save list. Drives Game.LoadGameFromMainMenu (the CONTINUE-button path), then polls for loading to
        // finish + a play context to be active. We drive nav via /input + /eval (our InputManager), so we
        // don't need the game's keyboard focus — no focus fix required (the dev server already keeps the loop
        // running unfocused). Save metadata loads async at the title screen, so a too-early call returns a
        // retryable "[not ready]"/"[no save]".
        private string LoadSave(string body)
        {
            string sel = (body ?? "").Trim();
            if (sel.Length == 0) sel = "latest";

            string kick = OnMainThread(() =>
            {
                var game = Kingmaker.Game.Instance;
                if (game == null || game.SaveManager == null) return "[not ready] no SaveManager yet; retry\n";
                // Must be idle at the title screen. The server answers /health at the GameStarter entry point
                // (before the menu exists), and loading mid-boot half-initializes the game.
                var lp = Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance;
                if (lp == null || lp.IsLoadingScreenActive) return "[not ready] still on a loading screen; retry\n";
                var mm = game.UI?.MainMenu;
                if (mm == null) return "[not ready] not at the main menu (load only from the title screen); retry\n";
                var save = ResolveSave(game.SaveManager, sel);
                if (save == null) return "[no save] '" + sel + "' not found (saves still loading? retry)\n";
                // Drive the real Continue-button path: MainMenu.LoadGame wraps the load in EnterGame, which
                // shows the loading screen, tears down the menu (stopping its music) + EscManager, loads the
                // obligatory scenes, THEN runs LoadGameFromMainMenu. Calling LoadGameFromMainMenu directly
                // skips that transition and leaves a broken half-load (menu music + no party).
                mm.LoadGame(save);
                return "ok\n";
            });
            if (kick != "ok\n") return kick;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 90)
            {
                string status = OnMainThread(() =>
                {
                    var lp = Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance;
                    if (lp != null && lp.IsLoadingScreenActive) return "";
                    // A play context becoming active is our "interactive" signal; at the menu it's
                    // ctx.mainmenu (not in-play), so we can't falsely return before the load even starts.
                    string key = WrathAccess.Screens.ScreenManager.Current?.Key;
                    bool inPlay = key == "ctx.ingame" || key == "ctx.tacticalcombat" || key == "ctx.globalmap";
                    return inPlay ? "loaded '" + sel + "': screen=" + key + "\n" : "";
                });
                if (status.Length > 0) return status;
                Thread.Sleep(150);
            }
            return "[timeout] load '" + sel + "' did not become interactive within 90s\n";
        }

        private static Kingmaker.EntitySystem.Persistence.SaveInfo ResolveSave(
            Kingmaker.EntitySystem.Persistence.SaveManager mgr, string sel)
        {
            if (sel == "latest") return mgr.GetLatestSave();
            if (sel == "quick") return mgr.GetNewestQuickslot();
            // "area:<BlueprintName>" = the newest save made IN that area — the survey workflow's way
            // to load an area in its story-correct etude state (teleporting a later save in shows the
            // area as the LATER story left it, e.g. the festival square already torn by the attack).
            if (sel.StartsWith("area:", StringComparison.OrdinalIgnoreCase))
            {
                string area = sel.Substring("area:".Length).Trim();
                Kingmaker.EntitySystem.Persistence.SaveInfo best = null;
                foreach (var s in mgr)
                    if (s != null && s.Area != null && s.Area.name == area
                        && (best == null || s.SystemSaveTime > best.SystemSaveTime))
                        best = s;
                return best;
            }
            if (int.TryParse(sel, out int idx))
            {
                int i = 0;
                foreach (var s in mgr) if (i++ == idx) return s;
                return null;
            }
            return mgr.GetLatestSave();
        }

        // Capture the game framebuffer to a PNG (works unfocused) and return its path for the driver to Read.
        // ScreenCapture writes asynchronously over the next frame(s): trigger on the main thread, then wait
        // here (HTTP thread) for the file to appear and its size to settle.
        private string Screenshot()
        {
            string path = Path.Combine(Path.GetTempPath(), "wa_shot.png");
            OnMainThread(() =>
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                UnityEngine.ScreenCapture.CaptureScreenshot(path);
                return "requested";
            });

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 8)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        long size = new FileInfo(path).Length;
                        if (size > 0)
                        {
                            Thread.Sleep(60); // let the write settle, then confirm the size is stable
                            if (new FileInfo(path).Length == size) return path + "\n";
                        }
                    }
                }
                catch { }
                Thread.Sleep(50);
            }
            return "[timeout] screenshot not written within 8s\n";
        }
    }
}
#endif
