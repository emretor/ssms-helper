using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper;

/// <summary>Runs async work started from synchronous editor callbacks without blocking them.</summary>
internal static class Background
{
    /// <summary>
    /// Starts <paramref name="work"/> on the JoinableTaskFactory and returns immediately.
    /// Exceptions are logged, never rethrown, so editor callbacks stay safe.
    /// </summary>
    public static void Run(Func<Task> work, string description)
    {
        // Fire-and-forget is intended here; this is the single place that does it.
#pragma warning disable VSSDK007
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await work();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error($"{description} failed.", ex);
            }
        }).Task.Forget();
#pragma warning restore VSSDK007
    }
}
