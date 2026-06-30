using NarcoNet.UI;
using NarcoNet.Utilities;

namespace NarcoNet.Services;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

/// <summary>
///     Service interface for managing client UI windows and user interactions
/// </summary>
public interface IClientUIService
{
    /// <summary>
    ///     Gets whether any UI window is currently active
    /// </summary>
    bool IsAnyWindowActive { get; }

    /// <summary>
    ///     Shows the update confirmation window with the list of changes
    /// </summary>
    void ShowUpdateWindow(List<string> optional, List<string> required, Action<bool> onAccept, Action? onSkip, bool hasRemovedFiles);

    /// <summary>
    ///     Shows the download progress window
    /// </summary>
    void ShowProgressWindow();

    /// <summary>
    ///     Updates the download progress
    /// </summary>
    void UpdateProgress(int current, int total, Action? onCancel);

    /// <summary>
    ///     Updates the aggregate byte-level download progress
    /// </summary>
    void UpdateByteProgress(long current, long total);

    /// <summary>
    ///     Hides the progress window
    /// </summary>
    void HideProgressWindow();

    /// <summary>
    ///     Shows the restart required window
    /// </summary>
    void ShowRestartWindow(Action onRestart);

    /// <summary>
    ///     Shows the download error window
    /// </summary>
    void ShowErrorWindow(Action onQuit);

    /// <summary>
    ///     Hides all windows
    /// </summary>
    void HideAllWindows();

    /// <summary>
    ///     Draws all active UI windows (called from OnGUI)
    /// </summary>
    void DrawWindows();

    /// <summary>
    ///     Handles game UI visibility when update windows are shown
    /// </summary>
    void HandleGameUIVisibility(bool updateWindowsActive);

    /// <summary>
    ///     Shows the sync diagnostic overlay window
    /// </summary>
    void ShowDiagnosticWindow();

    /// <summary>
    ///     Updates a diagnostic step's status and state
    /// </summary>
    void UpdateDiagnosticStep(string stepKey, string status, DiagnosticState state);

    /// <summary>
    ///     Hides the sync diagnostic overlay window
    /// </summary>
    void HideDiagnosticWindow();

    /// <summary>
    ///     Shows the file comparison window with merged local and remote file data
    /// </summary>
    void ShowFileComparisonWindow(SyncPathModFiles localModFiles, SyncPathModFiles remoteModFiles);

    /// <summary>
    ///     Hides the file comparison window
    /// </summary>
    void HideFileComparisonWindow();

    /// <summary>
    ///     Sets the callback for the diagnostic window's "Show Files" button
    /// </summary>
    void SetDiagnosticShowFilesAction(Action? onShowFiles);
}
