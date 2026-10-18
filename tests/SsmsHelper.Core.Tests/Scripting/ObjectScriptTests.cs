using SsmsHelper.Core.Scripting;

namespace SsmsHelper.Core.Tests.Scripting;

public class ObjectScriptTests
{
    [Theory]
    [InlineData("CREATE PROCEDURE dbo.P AS SELECT 1", "ALTER PROCEDURE dbo.P AS SELECT 1")]
    [InlineData("create   proc dbo.P AS SELECT 1", "ALTER   proc dbo.P AS SELECT 1")]
    [InlineData("CREATE OR ALTER VIEW dbo.V AS SELECT 1 AS x", "ALTER VIEW dbo.V AS SELECT 1 AS x")]
    [InlineData("-- Orders by customer\r\n/* v2 */\r\nCREATE PROCEDURE dbo.P AS SELECT 'CREATE'", "-- Orders by customer\r\n/* v2 */\r\nALTER PROCEDURE dbo.P AS SELECT 'CREATE'")]
    [InlineData("ALTER PROCEDURE dbo.P AS SELECT 1", "ALTER PROCEDURE dbo.P AS SELECT 1")]
    public void ToAlter_ReplacesOnlyTheLeadingCreate(string definition, string expected)
    {
        Assert.Equal(expected, ObjectScript.ToAlter(definition));
    }

    [Fact]
    public void Alter_HasUse_SetOptions_AndTheDefinition_InBatches()
    {
        var script = ObjectScript.Alter("Shop", "\r\nCREATE PROCEDURE dbo.P AS SELECT 1\r\n", ansiNulls: true, quotedIdentifier: false, "\n");
        Assert.Equal(
            "USE [Shop]\nGO\nSET ANSI_NULLS ON\nGO\nSET QUOTED_IDENTIFIER OFF\nGO\nALTER PROCEDURE dbo.P AS SELECT 1\nGO\n",
            script);
    }

    [Fact]
    public void Create_PutsEachStatementInItsOwnBatch_AndEscapesTheDatabaseName()
    {
        var script = ObjectScript.Create("My]Db", new[] { "SET ANSI_NULLS ON", "CREATE TABLE dbo.T (a int)\r\n" }, "\n");
        Assert.Equal("USE [My]]Db]\nGO\nSET ANSI_NULLS ON\nGO\nCREATE TABLE dbo.T (a int)\nGO\n", script);
    }

    [Fact]
    public void NoDatabase_NoUse()
    {
        Assert.StartsWith("SET ANSI_NULLS", ObjectScript.Alter("", "CREATE VIEW v AS SELECT 1 AS x", true, true, "\n"));
    }
}
