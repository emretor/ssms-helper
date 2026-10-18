using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Data.SqlTypes;
using System.Linq;
using System.Reflection;

namespace SsmsHelper.Results;

/// <summary>One column of the selection, with what the result set says about it.</summary>
/// <param name="StorageIndex">Index for QEResultSet.GetCellData (data index + 1 in grid mode).</param>
/// <param name="SchemaRow">The column's row of the reader's schema table, when available.</param>
internal sealed record GridColumn(string Name, string TypeName, int StorageIndex, DataRow? SchemaRow);

/// <summary>The selected cells of the focused results grid.</summary>
/// <param name="ResultIndex">Which grid of the window (0 = the first result set).</param>
/// <param name="ResultCount">How many grids the window shows.</param>
/// <param name="Rows">Values per selected row, one per column, as GetCellData returns them.</param>
internal sealed record GridSelection(int ResultIndex, int ResultCount, IReadOnlyList<GridColumn> Columns, IReadOnlyList<object?[]> Rows);

/// <summary>Reading the grid failed; the message is for the status bar.</summary>
internal sealed class GridAccessException : Exception
{
    public GridAccessException(string message) : base(message)
    {
    }
}

/// <summary>
/// Reads the selection of the focused SQL results grid. SSMS has no API for the grid, so this walks SSMS's
/// internal objects by reflection (SQLEditors.dll and Microsoft.SqlServer.GridControl.dll, checked against SSMS 22):
/// SqlScriptEditorControl.m_sqlResultsControl → DisplaySQLResultsControl.m_gridResultsPage → GridResultsTabPage
/// .FocusedGrid (GridControl) and its QEResultSet. Every missing member ends in a <see cref="GridAccessException"/>,
/// never in SSMS. UI thread only.
/// </summary>
internal static class ResultsGridReader
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>More rows than this are refused: the script would be too large to paste.</summary>
    public const int MaxRows = 50_000;

    private static bool s_logged;

    /// <param name="docView">The query window's doc view (SqlScriptEditorControl).</param>
    public static GridSelection Read(object docView)
    {
        var results = Field(docView, "m_sqlResultsControl") ?? throw Missing("the results pane");
        var page = Field(results, "m_gridResultsPage") ?? throw new GridAccessException("no results grid in this window; run the query with Results to Grid.");
        var grid = Property(page, "FocusedGrid") ?? throw new GridAccessException("click into a results grid first.");

        var gridCount = Property(page, "NumberOfGrids") is int n ? n : 0;
        var resultIndex = -1;
        for (var i = 0; i < gridCount; i++)
        {
            if (ReferenceEquals(Invoke(page, "GetGridControl", i), grid))
            {
                resultIndex = i;
                break;
            }
        }

        var resultSet = (resultIndex >= 0 ? Invoke(page, "GetResultSet", resultIndex) : null) ??
                        Invoke(page, "GetGridResultSet", grid) ??
                        throw Missing("the result set");

        var gridMode = Property(resultSet, "GridMode") as bool? ?? true;
        var names = Property(resultSet, "ColumnNames") as StringCollection ?? throw Missing("the column names");
        var selected = Property(grid, "SelectedCells") as IEnumerable ?? throw Missing("the selection");

        // Blocks are in UI columns (the user may have dragged columns around); keep the UI order.
        var uiColumns = new SortedSet<int>();
        var rowSet = new SortedSet<long>();
        foreach (var block in selected)
        {
            var x = Convert.ToInt32(Property(block, "X"));
            var right = Convert.ToInt32(Property(block, "Right"));
            var y = Convert.ToInt64(Property(block, "Y"));
            var bottom = Convert.ToInt64(Property(block, "Bottom"));
            if (x < 0 || y < 0)
            {
                continue;
            }

            for (var c = x; c <= right; c++)
            {
                uiColumns.Add(c);
            }

            if (rowSet.Count + (bottom - y + 1) > MaxRows)
            {
                throw new GridAccessException($"more than {MaxRows:N0} rows selected; select fewer rows.");
            }

            for (var r = y; r <= bottom; r++)
            {
                rowSet.Add(r);
            }
        }

        var columns = new List<GridColumn>();
        foreach (var ui in uiColumns)
        {
            var storage = Invoke(grid, "GetStorageColumnIndexByUIIndex", ui) is int s ? s : ui;
            var data = gridMode ? storage - 1 : storage;
            if (data < 0 || data >= names.Count)
            {
                continue; // the row number column
            }

            var typeName = TryInvoke(resultSet, "GetServerDataTypeName", data) as string ?? string.Empty;
            columns.Add(new GridColumn(names[data]!, typeName, storage, TryInvoke(resultSet, "GetSchemaRow", data) as DataRow));
        }

        var getCellData = Method(resultSet, "GetCellData", typeof(long), typeof(int));
        var getCellText = Method(resultSet, "GetCellDataAsString", typeof(long), typeof(int));
        var rows = new List<object?[]>(rowSet.Count);
        foreach (var row in rowSet)
        {
            var values = new object?[columns.Count];
            for (var k = 0; k < columns.Count; k++)
            {
                var value = getCellData.Invoke(resultSet, new object[] { row, columns[k].StorageIndex });

                // Numbers come as SqlInt32, SqlDecimal, ...; other stored values are used as the grid shows them.
                values[k] = IsUsable(value) ? value : getCellText.Invoke(resultSet, new object[] { row, columns[k].StorageIndex });
            }

            rows.Add(values);
        }

        LogOnce(columns, rows);
        return new GridSelection(resultIndex, gridCount, columns, rows);
    }

    private static bool IsUsable(object? value) =>
        value is null or DBNull or INullable or string or bool or byte or short or int or long or decimal or double or float
            or DateTime or DateTimeOffset or TimeSpan or Guid or byte[] or char[];

    /// <summary>The first read per session goes to the activity log, so a changed SSMS shows what it returns.</summary>
    private static void LogOnce(IReadOnlyList<GridColumn> columns, IReadOnlyList<object?[]> rows)
    {
        if (s_logged)
        {
            return;
        }

        s_logged = true;
        var first = rows.Count > 0 ? rows[0] : null;
        var described = columns.Select((c, i) =>
            $"{c.Name} {c.TypeName} storage={c.StorageIndex} schemaRow={(c.SchemaRow is null ? "no" : "yes")} value={first?[i]?.GetType().Name ?? "null"}");
        var schemaColumns = columns.FirstOrDefault(c => c.SchemaRow is not null)?.SchemaRow!.Table.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
        Log.Info($"Copy as INSERT: {rows.Count} row(s); {string.Join("; ", described)}; schema table: {string.Join(",", schemaColumns ?? Array.Empty<string>())}");
    }

    private static GridAccessException Missing(string what) =>
        new($"cannot read {what} from this SSMS version. Please report it with your SSMS version.");

    private static object? Field(object target, string name)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, Members | BindingFlags.DeclaredOnly) is { } field)
            {
                return field.GetValue(target);
            }
        }

        return null;
    }

    private static object? Property(object target, string name)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetProperty(name, Members | BindingFlags.DeclaredOnly) is { } property && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(target);
            }
        }

        return null;
    }

    private static MethodInfo Method(object target, string name, params Type[] parameters)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetMethod(name, Members | BindingFlags.DeclaredOnly, null, parameters, null) is { } method)
            {
                return method;
            }
        }

        throw Missing(name);
    }

    private static object? Invoke(object target, string name, object argument)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var method = type.GetMethods(Members | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType.IsInstanceOfType(argument));
            if (method is not null)
            {
                return method.Invoke(target, new[] { argument });
            }
        }

        return null;
    }

    private static object? TryInvoke(object target, string name, object argument)
    {
        try
        {
            return Invoke(target, name, argument);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }
}
