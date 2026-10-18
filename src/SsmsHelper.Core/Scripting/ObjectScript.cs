using System.Collections.Generic;
using System.Text;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Scripting;

/// <summary>Builds the text F12 opens in a new query window, laid out like SSMS's own "Script as".</summary>
public static class ObjectScript
{
    /// <summary>
    /// ALTER script for a procedure, view, function or trigger from its stored definition: USE, the SET options the
    /// module was created with, then the definition with its leading CREATE (or CREATE OR ALTER) turned into ALTER.
    /// Comments before CREATE are kept.
    /// </summary>
    public static string Alter(string database, string definition, bool ansiNulls, bool quotedIdentifier, string newLine)
    {
        var text = new StringBuilder();
        AppendUse(text, database, newLine);
        AppendBatch(text, "SET ANSI_NULLS " + (ansiNulls ? "ON" : "OFF"), newLine);
        AppendBatch(text, "SET QUOTED_IDENTIFIER " + (quotedIdentifier ? "ON" : "OFF"), newLine);
        AppendBatch(text, ToAlter(definition).Trim(), newLine);
        return text.ToString();
    }

    /// <summary>CREATE script for a table from SMO's scripted statements, each in its own batch.</summary>
    public static string Create(string database, IEnumerable<string> statements, string newLine)
    {
        var text = new StringBuilder();
        AppendUse(text, database, newLine);
        foreach (var statement in statements)
        {
            AppendBatch(text, statement.Trim(), newLine);
        }

        return text.ToString();
    }

    /// <summary>Replaces the first CREATE (and a following OR ALTER) with ALTER; anything else is returned unchanged.</summary>
    public static string ToAlter(string definition)
    {
        var tokens = SqlTokenizer.Tokenize(definition);
        if (tokens.Count == 0 || !tokens[0].IsWord("CREATE"))
        {
            return definition;
        }

        var end = tokens[0].End;
        if (tokens.Count > 2 && tokens[1].IsWord("OR") && tokens[2].IsWord("ALTER"))
        {
            end = tokens[2].End;
        }

        return definition.Substring(0, tokens[0].Start) + "ALTER" + definition.Substring(end);
    }

    private static void AppendUse(StringBuilder text, string database, string newLine)
    {
        if (database.Length > 0)
        {
            AppendBatch(text, "USE [" + database.Replace("]", "]]") + "]", newLine);
        }
    }

    private static void AppendBatch(StringBuilder text, string statement, string newLine) =>
        text.Append(statement).Append(newLine).Append("GO").Append(newLine);
}
