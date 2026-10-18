using System;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using SsmsHelper.Connections;
using SsmsHelper.Core.Settings;

namespace SsmsHelper.Options;

/// <summary>
/// Keeps <see cref="HelperSettings"/> in the SSMS user settings store (collection "SsmsHelper"). The editor reads
/// <see cref="Current"/> on every key, so it is a plain field read; the store is only touched on load and save.
/// Lives outside the package because the package loads on demand, after the editor helpers are already in use.
/// </summary>
internal static class SettingsStore
{
    private const string Collection = "SsmsHelper";

    private static HelperSettings? s_current;

    /// <summary>The settings, loaded on first use from the UI thread; defaults until then.</summary>
    public static HelperSettings Current
    {
        get
        {
            if (s_current is not null)
            {
                return s_current;
            }

            if (!ThreadHelper.CheckAccess())
            {
                return HelperSettings.Default;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            return Load();
        }
    }

    /// <summary>Saves and applies the settings at once; open editors pick them up on their next key.</summary>
    public static void Save(HelperSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        settings = settings.Normalized();
        try
        {
            var store = new ShellSettingsManager(ServiceProvider.GlobalProvider).GetWritableSettingsStore(SettingsScope.UserSettings);
            if (!store.CollectionExists(Collection))
            {
                store.CreateCollection(Collection);
            }

            foreach (var value in settings.ToValues())
            {
                store.SetString(Collection, value.Key, value.Value);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed.", ex);
        }

        Apply(settings);
    }

    private static HelperSettings Load()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        HelperSettings settings;
        try
        {
            var store = new ShellSettingsManager(ServiceProvider.GlobalProvider).GetReadOnlySettingsStore(SettingsScope.UserSettings);
            settings = HelperSettings.FromValues(name =>
                store.CollectionExists(Collection) && store.PropertyExists(Collection, name) ? store.GetString(Collection, name) : null);
        }
        catch (Exception ex)
        {
            Log.Error("Reading settings failed; using defaults.", ex);
            settings = HelperSettings.Default;
        }

        Apply(settings);
        return settings;
    }

    private static void Apply(HelperSettings settings)
    {
        s_current = settings;
        SchemaService.Cache.TimeToLive = TimeSpan.FromMinutes(settings.SchemaCacheMinutes);
        SchemaService.Databases.TimeToLive = TimeSpan.FromMinutes(settings.SchemaCacheMinutes);
    }
}
