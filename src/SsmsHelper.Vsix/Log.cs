using System;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

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
        // Not Background.Run: that logs its own failures here, which could recurse.
#pragma warning disable VSSDK007
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
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
        });
#pragma warning restore VSSDK007
    }
}
