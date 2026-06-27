// GsxMenuAutomation — thin async driver over GsxService.
//
// Responsibilities:
//   • OpenAsync   — register the WaitForNextMenuAsync BEFORE triggering OpenMenu
//                   (CRITICAL ordering: avoids a race where MenuChanged fires
//                    between the trigger and the wait registration).
//   • ChooseAsync — same ordering: register wait, then Choose, then await.
//   • CloseMenu   — best-effort hide so we never leave the menu open on abort.
//
// No gate / DFS knowledge lives here — this is pure menu mechanics, reusable
// by any future consumer (service dispatch, etc.).

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Async open/choose/close driver over <see cref="GsxService"/>.
/// All async methods are UI-thread-aware: they resume on whichever
/// synchronisation context was current when awaited, consistent with
/// the <see cref="GsxService.WaitForNextMenuAsync"/> contract.
/// </summary>
public sealed class GsxMenuAutomation
{
    private readonly GsxService _gsx;

    /// <summary>Default per-step timeout if callers don't supply one.</summary>
    public static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(8);

    public GsxMenuAutomation(GsxService gsx)
    {
        _gsx = gsx ?? throw new ArgumentNullException(nameof(gsx));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Requests the GSX top-level menu and waits for it to appear.
    /// </summary>
    /// <param name="timeout">
    /// How long to wait for the menu to populate.  Defaults to
    /// <see cref="DefaultStepTimeout"/>.
    /// </param>
    /// <returns>The list of <see cref="GsxService.MenuOption"/>s on the new menu.</returns>
    /// <exception cref="TimeoutException">
    /// Thrown when the menu does not appear within <paramref name="timeout"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when GSX hides the menu or signals a timeout before a new menu arrives.
    /// </exception>
    /// <remarks>
    /// CRITICAL: the <see cref="GsxService.WaitForNextMenuAsync"/> registration
    /// is made BEFORE calling <see cref="GsxService.OpenMenu"/> to avoid a race
    /// where <c>MenuChanged</c> fires between the trigger and the wait registration.
    /// </remarks>
    public Task<IReadOnlyList<GsxService.MenuOption>> OpenAsync(
        TimeSpan timeout = default)
    {
        if (timeout == default) timeout = DefaultStepTimeout;

        // CRITICAL: register BEFORE triggering.
        var waitTask = _gsx.WaitForNextMenuAsync(timeout);
        _gsx.OpenMenu();
        return waitTask;
    }

    /// <summary>
    /// Chooses a menu option and waits for the resulting menu to appear.
    /// </summary>
    /// <param name="choice">
    /// The choice index to pass to <see cref="GsxService.Choose"/>.
    /// </param>
    /// <param name="timeout">
    /// How long to wait for the resulting menu.  Defaults to
    /// <see cref="DefaultStepTimeout"/>.
    /// </param>
    /// <returns>The list of <see cref="GsxService.MenuOption"/>s on the resulting menu.</returns>
    /// <exception cref="TimeoutException">Thrown when no follow-on menu arrives within <paramref name="timeout"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when GSX hides or times out the menu.</exception>
    /// <remarks>
    /// Same CRITICAL ordering as <see cref="OpenAsync"/>: register wait BEFORE choose.
    /// </remarks>
    public Task<IReadOnlyList<GsxService.MenuOption>> ChooseAsync(
        int choice,
        TimeSpan timeout = default)
    {
        if (timeout == default) timeout = DefaultStepTimeout;

        // CRITICAL: register BEFORE triggering.
        var waitTask = _gsx.WaitForNextMenuAsync(timeout);
        _gsx.Choose(choice);
        return waitTask;
    }

    /// <summary>
    /// Best-effort hides the menu so we never leave it open on an abort path.
    /// Does not throw.
    /// </summary>
    /// <remarks>
    /// GSX's <c>MENU_CHOICE = -1</c> signals "user abandoned" and causes GSX to
    /// dismiss the menu — this is the same signal used by AccessGSX on timeout.
    /// We call <see cref="GsxService.Choose"/> with -1 if the menu is currently
    /// active; otherwise this is a no-op.
    /// </remarks>
    public void CloseMenu()
    {
        try
        {
            if (_gsx.IsMenuActive)
            {
                // Sending choice -1 signals "abandon" to GSX (same as the
                // HandleToggleEvent case 3 path in GsxService).
                _gsx.Choose(-1);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[GsxMenuAutomation] CloseMenu best-effort failed (ignored): {ex.Message}");
        }
    }
}
