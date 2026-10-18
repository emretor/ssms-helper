using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SsmsHelper.Core.Settings;

namespace SsmsHelper.Options;

/// <summary>Tools > Options > SSMS Helper > General. Reads and writes through <see cref="SettingsStore"/>.</summary>
[Guid("6F1D0B57-3C1E-4C59-9A61-2E7B7C5D9A43")]
public sealed class GeneralOptionsPage : DialogPage
{
    private const string Helpers = "Helpers";
    private const string Safety = "Safety";
    private const string Schema = "Schema";

    [Category(Helpers)]
    [DisplayName("Snippets")]
    [Description("Tab expands snippet shortcuts such as ssf, ii, upd, df and bt.")]
    [DefaultValue(true)]
    public bool Snippets { get; set; } = true;

    [Category(Helpers)]
    [DisplayName("JOIN list opens automatically")]
    [Description("Open the list of ON conditions after \"JOIN table alias \" and after \"ON \". The Suggest Join command works either way.")]
    [DefaultValue(true)]
    public bool JoinAutoOpen { get; set; } = true;

    [Category(Helpers)]
    [DisplayName("INSERT helper")]
    [Description("Tab or Shift+Enter (or an IntelliSense pick) after \"INSERT INTO table\" writes the columns and VALUES.")]
    [DefaultValue(true)]
    public bool InsertHelper { get; set; } = true;

    [Category(Helpers)]
    [DisplayName("Expand SELECT *")]
    [Description("Tab or Shift+Enter right after the * in \"SELECT * FROM ...\" writes the column list.")]
    [DefaultValue(true)]
    public bool StarExpansion { get; set; } = true;

    [Category(Helpers)]
    [DisplayName("EXEC helper")]
    [Description("Tab or Shift+Enter (or an IntelliSense pick) after \"EXEC procedure\" writes its parameters.")]
    [DefaultValue(true)]
    public bool ExecHelper { get; set; } = true;

    [Category(Safety)]
    [DisplayName("Ask before UPDATE or DELETE without WHERE")]
    [Description("Execute (F5, Ctrl+E) asks for confirmation when the script has an UPDATE or DELETE without WHERE.")]
    [DefaultValue(true)]
    public bool WhereSafetyCheck { get; set; } = true;

    [Category(Schema)]
    [DisplayName("Schema cache minutes")]
    [Description("How long a loaded schema is used before it is refreshed in the background (1 to 1440). Refresh Schema reloads at once.")]
    [DefaultValue(HelperSettings.DefaultSchemaCacheMinutes)]
    public int SchemaCacheMinutes { get; set; } = HelperSettings.DefaultSchemaCacheMinutes;

    public override void LoadSettingsFromStorage()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var settings = SettingsStore.Current;
        Snippets = settings.Snippets;
        JoinAutoOpen = settings.JoinAutoOpen;
        InsertHelper = settings.InsertHelper;
        StarExpansion = settings.StarExpansion;
        ExecHelper = settings.ExecHelper;
        WhereSafetyCheck = settings.WhereSafetyCheck;
        SchemaCacheMinutes = settings.SchemaCacheMinutes;
    }

    public override void SaveSettingsToStorage()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SettingsStore.Save(new HelperSettings
        {
            Snippets = Snippets,
            JoinAutoOpen = JoinAutoOpen,
            InsertHelper = InsertHelper,
            StarExpansion = StarExpansion,
            ExecHelper = ExecHelper,
            WhereSafetyCheck = WhereSafetyCheck,
            SchemaCacheMinutes = SchemaCacheMinutes,
        });

        // Show the stored (range-checked) value next time the page opens.
        SchemaCacheMinutes = SettingsStore.Current.SchemaCacheMinutes;
    }
}
