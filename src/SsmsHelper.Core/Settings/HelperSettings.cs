using System;
using System.Collections.Generic;
using System.Globalization;

namespace SsmsHelper.Core.Settings;

/// <summary>
/// User settings (Tools > Options > SSMS Helper). Stored as string values so the Vsix can keep them in the VS
/// settings store; missing or unreadable values fall back to the defaults, so an older or hand-edited store never
/// breaks a helper.
/// </summary>
public sealed record HelperSettings
{
    public const int DefaultSchemaCacheMinutes = 30;
    public const int MinSchemaCacheMinutes = 1;
    public const int MaxSchemaCacheMinutes = 24 * 60;

    public static HelperSettings Default { get; } = new();

    /// <summary>Our completion list: tables after FROM, columns after SET / WHERE, keywords after SELECT *.</summary>
    public bool CompletionList { get; init; } = true;

    /// <summary>Tab expands snippet shortcuts such as ssf.</summary>
    public bool Snippets { get; init; } = true;

    /// <summary>The JOIN list opens by itself after "JOIN table alias " and "ON ". The Suggest Join command always works.</summary>
    public bool JoinAutoOpen { get; init; } = true;

    /// <summary>Tab / Shift+Enter / IntelliSense pick after "INSERT INTO table".</summary>
    public bool InsertHelper { get; init; } = true;

    /// <summary>Tab / Shift+Enter right after the * in "SELECT * FROM".</summary>
    public bool StarExpansion { get; init; } = true;

    /// <summary>Tab / Shift+Enter / IntelliSense pick after "EXEC procedure".</summary>
    public bool ExecHelper { get; init; } = true;

    /// <summary>F12 scripts the object under the caret into a new query window.</summary>
    public bool ScriptObject { get; init; } = true;

    /// <summary>Results grid right-click: Copy as INSERT (with and without identity).</summary>
    public bool CopyAsInsert { get; init; } = true;

    /// <summary>Execute asks before running UPDATE or DELETE without WHERE.</summary>
    public bool WhereSafetyCheck { get; init; } = true;

    /// <summary>SSMS's Refresh Local Cache (Ctrl+Shift+R) also reloads the SSMS Helper schema.</summary>
    public bool RefreshWithIntelliSense { get; init; } = true;

    /// <summary>Minutes before a loaded schema is refreshed in the background, kept within 1 minute and 1 day.</summary>
    public int SchemaCacheMinutes { get; init; } = DefaultSchemaCacheMinutes;

    /// <summary>Copy with every value inside its allowed range.</summary>
    public HelperSettings Normalized() => this with
    {
        SchemaCacheMinutes = Math.Min(MaxSchemaCacheMinutes, Math.Max(MinSchemaCacheMinutes, SchemaCacheMinutes)),
    };

    public IReadOnlyDictionary<string, string> ToValues() => new Dictionary<string, string>
    {
        [nameof(CompletionList)] = Format(CompletionList),
        [nameof(Snippets)] = Format(Snippets),
        [nameof(JoinAutoOpen)] = Format(JoinAutoOpen),
        [nameof(InsertHelper)] = Format(InsertHelper),
        [nameof(StarExpansion)] = Format(StarExpansion),
        [nameof(ExecHelper)] = Format(ExecHelper),
        [nameof(ScriptObject)] = Format(ScriptObject),
        [nameof(CopyAsInsert)] = Format(CopyAsInsert),
        [nameof(WhereSafetyCheck)] = Format(WhereSafetyCheck),
        [nameof(RefreshWithIntelliSense)] = Format(RefreshWithIntelliSense),
        [nameof(SchemaCacheMinutes)] = SchemaCacheMinutes.ToString(CultureInfo.InvariantCulture),
    };

    /// <param name="get">Stored value for a name, or null when it was never saved.</param>
    public static HelperSettings FromValues(Func<string, string?> get) => new HelperSettings
    {
        CompletionList = ReadBool(get, nameof(CompletionList), Default.CompletionList),
        Snippets = ReadBool(get, nameof(Snippets), Default.Snippets),
        JoinAutoOpen = ReadBool(get, nameof(JoinAutoOpen), Default.JoinAutoOpen),
        InsertHelper = ReadBool(get, nameof(InsertHelper), Default.InsertHelper),
        StarExpansion = ReadBool(get, nameof(StarExpansion), Default.StarExpansion),
        ExecHelper = ReadBool(get, nameof(ExecHelper), Default.ExecHelper),
        ScriptObject = ReadBool(get, nameof(ScriptObject), Default.ScriptObject),
        CopyAsInsert = ReadBool(get, nameof(CopyAsInsert), Default.CopyAsInsert),
        WhereSafetyCheck = ReadBool(get, nameof(WhereSafetyCheck), Default.WhereSafetyCheck),
        RefreshWithIntelliSense = ReadBool(get, nameof(RefreshWithIntelliSense), Default.RefreshWithIntelliSense),
        SchemaCacheMinutes = int.TryParse(get(nameof(SchemaCacheMinutes)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            ? minutes
            : Default.SchemaCacheMinutes,
    }.Normalized();

    private static string Format(bool value) => value ? "true" : "false";

    private static bool ReadBool(Func<string, string?> get, string name, bool fallback) =>
        bool.TryParse(get(name), out var value) ? value : fallback;
}
