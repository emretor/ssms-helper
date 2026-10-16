using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using SsmsHelper.Commands;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper;

/// <summary>
/// Entry point loaded by SSMS. Loads on demand when one of its commands is first used.
/// Editor features will be MEF components and load independently of this package.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("SSMS Helper", "Snippet, JOIN and INSERT helpers for SSMS", "0.5.6")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageIds.PackageGuidString)]
public sealed class SsmsHelperPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await AboutCommand.InitializeAsync(this);
        await RefreshSchemaCommand.InitializeAsync(this);
        await SuggestJoinCommand.InitializeAsync(this);
    }
}
