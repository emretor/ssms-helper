using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace SsmsHelper;

/// <summary>
/// Writes to the SSMS activity log: %AppData%\Microsoft\SSMS\22.0_*\ActivityLog.xml when SSMS is
/// started with Ssms.exe /log. Safe to call from any thread.
/// </summary>
internal static class Log
{
    public const string Source = "SsmsHelper";

    public static void Info(string message) => Write(message, error: false);

    public static void Error(string message, Exception? exception = null) =>
        Write(exception is null ? message : $"{message}{Environment.NewLine}{exception}", error: true);

    private static void Write(string message, bool error)
    {
        // Fire-and-forget is intended: logging must never block the caller, and the Try* methods do not throw.
#pragma warning disable VSSDK007
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (error)
            {
                ActivityLog.TryLogError(Source, message);
            }
            else
            {
                ActivityLog.TryLogInformation(Source, message);
            }
        }).Task.Forget();
#pragma warning restore VSSDK007
    }
}
