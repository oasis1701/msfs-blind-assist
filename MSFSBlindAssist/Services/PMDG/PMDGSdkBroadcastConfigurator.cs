using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Orchestrates the one-shot per-connect check: does the currently loaded PMDG variant's
/// <c>&lt;family&gt;_Options.ini</c> have the <c>[SDK]</c> data-broadcast lines third-party tools
/// (including this app's own PMDG CDU windows) need to see the CDU at all? If not, ask the pilot
/// whether to fix it, back up the existing file, patch it, and warn that a simulator restart is
/// required. Every terminal outcome reaches the pilot, not just success: the configuration file
/// not being found at all, the file existing but not being readable, and a failed write are each
/// their own warning dialog — never silent, so a pilot who never sees a fix offered still knows
/// why.
///
/// <para>
/// This class owns the file IO, the confirmation/warning dialogs, and the session-level "don't
/// nag every reconnect" bookkeeping — everything here is either thin IO glue or WinForms calls,
/// intentionally NOT unit-tested (matches this codebase's convention: sim-facing/IO-facing glue is
/// verified live; the parsing and path logic it calls, <see cref="PMDGOptionsIniFormat"/> and
/// <see cref="PMDGOptionsIniLocator"/>, carry the characterization tests).
/// </para>
///
/// <para>
/// Logging here is deliberately verbose — every candidate path tried, the found file's full
/// contents before any change, the per-key found-vs-required comparison, the pilot's answer to
/// the prompt, the backup path, and the full patched contents before the write. This is code
/// that edits a file this app does not own, on the strength of a guess about which installed
/// package is currently loaded; if that guess is ever wrong the debug.log trail has to be enough
/// to reconstruct exactly what was found and changed without needing to reproduce it live.
/// </para>
/// </summary>
public sealed class PMDGSdkBroadcastConfigurator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Options.ini paths already found fully configured OR already declined by the pilot this
    /// session — skipped on a later reconnect/aircraft-swap so the prompt doesn't repeat every
    /// time the pilot reconnects to the same variant. Cleared per app run (not persisted): a
    /// genuinely fresh check after a restart costs nothing and catches PMDG's own file resets.
    /// </summary>
    private readonly HashSet<string> _resolvedThisSession = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks the given PMDG aircraft's options.ini and, if the SDK broadcast lines are missing,
    /// prompts the pilot to add them. Safe to call on every connect — cheap when already
    /// configured or already declined. Never throws; every step logs what it found and did (see
    /// the class remarks), and any failure this method's own narrower handling didn't already
    /// explain is logged at Warn with the full exception.
    /// </summary>
    /// <param name="aircraftCode">"PMDG_737" or "PMDG_777" — selects the family prefix and
    /// whether a center CDU (777 only) is required.</param>
    /// <param name="title">The loaded aircraft's TITLE simvar value, used to resolve which
    /// installed package (and so which options.ini) is actually loaded.</param>
    /// <param name="uiContext">A live control (MainForm) used to marshal the confirmation/warning
    /// dialogs onto the UI thread — this method does file IO and may run on a background thread,
    /// and WinForms dialogs can only be shown from the thread that owns the window.</param>
    public async Task CheckAsync(string aircraftCode, string? title, AircraftCfgCatalog catalog, Control uiContext)
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
        {
            // A check is already in flight (e.g. two connect events firing close together) —
            // skip rather than queue; the next connect will check again if this one didn't land.
            Log.Debug("PMDG", $"SDK broadcast check: another check is already in flight for aircraftCode=\"{aircraftCode}\" — skipping this call.");
            return;
        }
        try
        {
            // Task.Run from the outset, not just around the individual file reads/writes: the
            // callers fire this from the UI thread mid-connect/mid-aircraft-switch, and an async
            // method runs synchronously up to its first real await. With the catalog already
            // built, that synchronous prefix reached process enumeration, four File.Exists calls
            // and — on the not-found branch — a MODAL MessageBox shown directly on the UI thread,
            // pumping messages into a half-finished SwitchAircraft. Off-thread, every dialog goes
            // through ShowOnUiThread's Invoke and the caller returns immediately as documented.
            await Task.Run(() => CheckCore(aircraftCode, title, catalog, uiContext)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // CheckCore's own steps each have narrower try/catches that log and degrade
            // gracefully (a bad read, a failed write); reaching here means something outside
            // those — log the full exception, not just its message, since this is the one path
            // with no narrower log line already explaining what happened.
            Log.Warn("PMDG", $"SDK broadcast check: unexpected failure for aircraftCode=\"{aircraftCode}\" title=\"{title}\": {ex}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void CheckCore(string aircraftCode, string? title, AircraftCfgCatalog catalog, Control uiContext)
    {
        // Verbose by design, at every step, not just on failure — this touches the pilot's own
        // PMDG configuration file, and debugging a bad edit to a file this app doesn't own is
        // far harder after the fact than reading a log line up front. See PR discussion: the
        // team has been burned before by a hard-to-debug user-config change (the flight-tablet
        // access work) with too little of a trail to reconstruct what happened.
        Log.Debug("PMDG", $"SDK broadcast check: starting for aircraftCode=\"{aircraftCode}\" title=\"{title}\"");

        (string familyPrefix, bool requireCenterCdu) = aircraftCode switch
        {
            "PMDG_737" => ("737", false),
            "PMDG_777" => ("777", true),
            _ => (string.Empty, false),
        };
        if (familyPrefix.Length == 0)
        {
            Log.Debug("PMDG", $"SDK broadcast check: aircraftCode=\"{aircraftCode}\" is not a PMDG variant this check covers — nothing to do.");
            return;
        }
        Log.Debug("PMDG", $"SDK broadcast check: family=\"{familyPrefix}\" requireCenterCdu={requireCenterCdu} " +
            $"(required [SDK] keys: {string.Join(", ", PMDGOptionsIniFormat.RequiredKeys(requireCenterCdu))})");

        if (!catalog.IsReady)
        {
            // Background scan not finished yet — wait for it (we're already off the UI thread).
            // Bounded, so this can never hang the check indefinitely.
            Log.Debug("PMDG", "SDK broadcast check: aircraft.cfg catalog not ready yet — waiting for its background scan.");
            bool ready = catalog.WaitUntilReady(TimeSpan.FromSeconds(60));
            Log.Debug("PMDG", $"SDK broadcast check: aircraft.cfg catalog scan {(ready ? "complete" : "still not finished after 60s — proceeding with whatever it has")}.");
        }
        if (!catalog.TryGetPackageFolderByTitle(title, out var packageFolder))
        {
            Log.Debug("PMDG", $"SDK broadcast check: no installed Community/Official package folder found for title \"{title}\" — cannot locate an options.ini. Aborting this check; nothing was read or written.");
            return;
        }
        Log.Debug("PMDG", $"SDK broadcast check: resolved installed package folder \"{packageFolder}\" for title \"{title}\"");

        // The title is whatever the SIM has loaded, but aircraftCode is whatever PROFILE this
        // app has selected — the SwitchAircraft hook lets a pilot pick the PMDG 737 profile
        // while a Cessna (or a PMDG 777) is loaded. The options.ini lives only in the PMDG
        // package's own work folder, so a non-PMDG or other-family package can never have it;
        // probing it would raise the "configuration not found" warning for an aircraft this
        // check doesn't apply to. PMDG's package folders are pmdg-aircraft-73x / -77x.
        if (packageFolder.IndexOf("pmdg", StringComparison.OrdinalIgnoreCase) < 0 ||
            packageFolder.IndexOf(familyPrefix.Substring(0, 2), StringComparison.Ordinal) < 0)
        {
            Log.Info("PMDG", $"SDK broadcast check: loaded package \"{packageFolder}\" (title \"{title}\") is not a PMDG {familyPrefix}-family package, so it has no {familyPrefix}_Options.ini to check — the selected profile does not match the loaded aircraft. Nothing was read or written.");
            return;
        }

        string runningSim = SimulatorDetector.DetectRunningSimulator();
        Log.Debug("PMDG", $"SDK broadcast check: detected running simulator = \"{runningSim}\"");

        var candidates = PMDGOptionsIniLocator.BuildCandidatePaths(packageFolder, familyPrefix, runningSim);
        Log.Debug("PMDG", $"SDK broadcast check: {candidates.Count} candidate options.ini path(s) to try, in order:");
        for (int i = 0; i < candidates.Count; i++)
        {
            Log.Debug("PMDG", $"SDK broadcast check:   [{i + 1}/{candidates.Count}] {candidates[i]}");
        }

        string? iniPath = PMDGOptionsIniLocator.FindExisting(candidates);
        if (iniPath == null)
        {
            // Confirmed against PMDG's own forum (2026-09): the work folder — and the options.ini
            // in it — is created only after the variant has been loaded into a flight AND the
            // simulator has been fully exited at least once; it does not appear the moment the
            // aircraft loads. So "not found" here is the NORMAL state for this variant's first
            // flight ever, not necessarily a fault — but the pilot's group asked to be told either
            // way rather than have this fail silently, so warn every time, worded to say so.
            string notFoundKey = $"missing::{familyPrefix}::{packageFolder}";
            Log.Debug("PMDG", $"SDK broadcast check: none of the {candidates.Count} candidate path(s) above exist on disk for package \"{packageFolder}\" ({familyPrefix}_Options.ini).");
            lock (_resolvedThisSession)
            {
                if (_resolvedThisSession.Contains(notFoundKey))
                {
                    Log.Debug("PMDG", $"SDK broadcast check: already warned about the missing options.ini for \"{packageFolder}\" earlier this session — skipping the repeat warning.");
                    return;
                }
                _resolvedThisSession.Add(notFoundKey);
            }
            Log.Warn("PMDG", $"SDK broadcast check: could not find an options.ini for package \"{packageFolder}\" ({familyPrefix}) at any of the {candidates.Count} candidate location(s) — could not check or fix the SDK broadcast setting for this flight.");
            ShowOnUiThread(uiContext, () => MessageBox.Show(
                $"MSFS Blind Assist could not find the configuration file it needs to check PMDG's " +
                $"SDK CDU data-broadcast setting for the PMDG {familyPrefix}.\n\n" +
                "If this is the first time you have flown this specific PMDG variant, this is " +
                "expected: PMDG only creates this file after you load the aircraft into a flight " +
                "and then fully exit Microsoft Flight Simulator (from its Main Menu) once. Fly it, " +
                "exit normally, and this check will run again the next time you load it.\n\n" +
                "If you have flown this variant before and keep seeing this message, the file may " +
                "be missing for another reason. Locations checked:\n" +
                string.Join("\n", candidates),
                "PMDG Configuration Not Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning));
            return;
        }
        Log.Debug("PMDG", $"SDK broadcast check: found an existing options.ini at {iniPath}");

        lock (_resolvedThisSession)
        {
            if (_resolvedThisSession.Contains(iniPath))
            {
                Log.Debug("PMDG", $"SDK broadcast check: {iniPath} was already resolved earlier this session (found correct, or the pilot already answered the prompt) — skipping re-check.");
                return;
            }
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(iniPath);
        }
        catch (Exception ex)
        {
            Log.Warn("PMDG", $"SDK broadcast check: could not read {iniPath}: {ex}. Could not check or fix the SDK broadcast setting for this flight; nothing was written.");
            lock (_resolvedThisSession) { _resolvedThisSession.Add(iniPath); }
            ShowOnUiThread(uiContext, () => MessageBox.Show(
                $"MSFS Blind Assist found the PMDG {familyPrefix} configuration file, but could not " +
                $"read it, so PMDG's SDK CDU data-broadcast setting could not be checked this flight.\n\n" +
                $"File: {iniPath}\n\n{ex.Message}",
                "PMDG Configuration Unreadable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning));
            return;
        }

        Log.Debug("PMDG", $"SDK broadcast check: read {lines.Length} line(s) from {iniPath}. Full contents as found, before any change, follow:{Environment.NewLine}" +
            string.Join(Environment.NewLine, lines));

        var currentValues = PMDGOptionsIniFormat.ReadSdkValues(lines);
        var requiredKeys = PMDGOptionsIniFormat.RequiredKeys(requireCenterCdu);
        foreach (var key in requiredKeys)
        {
            string found = currentValues.TryGetValue(key, out var v) ? $"\"{v}\"" : "(not present)";
            Log.Debug("PMDG", $"SDK broadcast check: [SDK] \"{key}\" currently = {found}");
        }

        var missingOrWrong = PMDGOptionsIniFormat.MissingOrIncorrectKeys(lines, requireCenterCdu);
        if (missingOrWrong.Count == 0)
        {
            Log.Info("PMDG", $"SDK broadcast check: {iniPath} already has every required [SDK] key set correctly " +
                $"({string.Join(", ", requiredKeys)} all = 1) — no changes needed, nothing was written.");
            lock (_resolvedThisSession) { _resolvedThisSession.Add(iniPath); }
            return;
        }

        Log.Info("PMDG", $"SDK broadcast check: {iniPath} is missing or has an incorrect value for: " +
            $"{string.Join(", ", missingOrWrong)}. Prompting the pilot before making any change.");

        bool confirmed = PromptToFix(uiContext, familyPrefix, iniPath);
        Log.Debug("PMDG", $"SDK broadcast check: pilot {(confirmed ? "confirmed" : "declined")} the prompt for {iniPath}");
        if (!confirmed)
        {
            Log.Info("PMDG", $"SDK broadcast check: pilot declined — leaving {iniPath} untouched. Nothing was written.");
            lock (_resolvedThisSession) { _resolvedThisSession.Add(iniPath); }
            return;
        }

        string? backupPath = null;
        try
        {
            backupPath = BackupFile(iniPath);
            Log.Debug("PMDG", $"SDK broadcast check: backed up {iniPath} to {backupPath} before making any change.");

            var patched = PMDGOptionsIniFormat.ApplyBroadcastSettings(lines, requireCenterCdu);
            foreach (var key in missingOrWrong)
            {
                string before = currentValues.TryGetValue(key, out var v) ? $"\"{v}\"" : "(not present)";
                Log.Debug("PMDG", $"SDK broadcast check: setting [SDK] \"{key}\"=1 (was {before})");
            }
            Log.Debug("PMDG", $"SDK broadcast check: patched file will have {patched.Count} line(s) (was {lines.Length}). Full new contents, before writing, follow:{Environment.NewLine}" +
                string.Join(Environment.NewLine, patched));

            WriteAtomically(iniPath, patched);
            Log.Info("PMDG", $"SDK broadcast check: wrote the updated [SDK] block to {iniPath} (backup at {backupPath}). Added/corrected: {string.Join(", ", missingOrWrong)}.");

            lock (_resolvedThisSession) { _resolvedThisSession.Add(iniPath); }
            ShowRestartWarning(uiContext, familyPrefix);
        }
        catch (Exception ex)
        {
            Log.Warn("PMDG", $"SDK broadcast check: failed to update {iniPath} (backup: {backupPath ?? "none was made"}): {ex}");
            string backupNote = backupPath == null
                ? "The file was not changed."
                : $"The original file is unchanged (the write goes to a temporary file first); a backup copy is also at:\n{backupPath}";
            ShowOnUiThread(uiContext, () => MessageBox.Show(
                $"Could not update {Path.GetFileName(iniPath)}.\n\n{ex.Message}\n\n{backupNote}",
                "PMDG SDK Configuration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error));
        }
    }

    /// <summary>
    /// Writes the patched contents to a sibling temporary file and then moves it over the
    /// original, so a failure part-way through a write (disk full, sharing violation) can never
    /// leave the pilot's options.ini truncated — <c>File.WriteAllLines</c> truncates the target
    /// before it writes a byte. UTF-8 without BOM, matching what PMDG's own writer produces.
    /// </summary>
    private static void WriteAtomically(string iniPath, IReadOnlyList<string> lines)
    {
        string tempPath = $"{iniPath}.msfsba-tmp";
        File.WriteAllLines(tempPath, lines);
        try
        {
            File.Move(tempPath, iniPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }

    private static bool PromptToFix(Control uiContext, string familyPrefix, string iniPath)
    {
        var result = ShowOnUiThread(uiContext, () => MessageBox.Show(
            $"The PMDG {familyPrefix} does not have PMDG's SDK data broadcast enabled in its " +
            "configuration file.\n\n" +
            "Without it, this app's PMDG CDU window (and any other third-party CDU tool) will " +
            "not display correctly.\n\n" +
            $"File: {iniPath}\n\n" +
            "Do you want MSFS Blind Assist to add the required lines now? The existing file " +
            "will be backed up first.",
            "PMDG SDK Configuration Needed",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question));
        return result == DialogResult.Yes;
    }

    private static void ShowRestartWarning(Control uiContext, string familyPrefix)
    {
        ShowOnUiThread(uiContext, () => MessageBox.Show(
            $"The PMDG {familyPrefix} configuration file has been updated to enable SDK data " +
            "broadcast.\n\n" +
            "You will most likely need to close and restart Microsoft Flight Simulator for this " +
            "change to take effect.\n\n" +
            "Exit the simulator normally, all the way out to its Main Menu and then out of the " +
            "application - do not use Alt+F4 or otherwise force-close it. PMDG only reliably " +
            "picks up a configuration file change on the next launch when the simulator was " +
            "allowed to shut down normally; force-closing it can discard the change entirely, " +
            "including the change just made here.",
            "PMDG SDK Configuration Updated",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information));
    }

    /// <summary>
    /// Runs <paramref name="showDialog"/> on the thread that owns <paramref name="uiContext"/>'s
    /// window handle, blocking the calling (background) thread until the dialog is dismissed —
    /// WinForms dialogs can only be shown from their owning thread, and this method's callers all
    /// need the dialog's result before deciding what to do next. Falls back to calling directly
    /// when already on the UI thread or the handle isn't available (never throws for that reason
    /// alone; a genuine dialog failure still propagates to the caller's own try/catch).
    /// </summary>
    private static DialogResult ShowOnUiThread(Control uiContext, Func<DialogResult> showDialog)
    {
        if (uiContext.IsDisposed || !uiContext.IsHandleCreated)
        {
            // The form is gone (app closing while the check was still running). InvokeRequired
            // reads false here, so without this guard the dialog would open unowned on the pool
            // thread after the main window has closed. Treat as "no answer" — for the fix
            // prompt that means declined, so nothing is written.
            Log.Debug("PMDG", "SDK broadcast check: UI context is disposed or has no handle — dialog not shown.");
            return DialogResult.None;
        }
        if (uiContext.InvokeRequired)
        {
            return uiContext.Invoke(showDialog);
        }
        return showDialog();
    }

    /// <summary>
    /// Copies the existing options.ini to a timestamped sibling backup before it's touched.
    /// Returns the backup path. Throws on failure — the caller must not proceed to patch/write
    /// the file if the backup could not be made.
    /// </summary>
    private static string BackupFile(string iniPath)
    {
        string backupPath = $"{iniPath}.backup-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Copy(iniPath, backupPath, overwrite: false);
        return backupPath;
    }
}
