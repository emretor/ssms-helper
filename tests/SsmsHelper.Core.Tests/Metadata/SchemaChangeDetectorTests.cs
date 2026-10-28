using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Core.Tests.Metadata;

public class SchemaChangeDetectorTests
{
    [Theory]
    [InlineData("CREATE TABLE dbo.Orders (Id int)")]
    [InlineData("create table Orders (Id int)")]
    [InlineData("ALTER TABLE dbo.Orders ADD Note nvarchar(100)")]
    [InlineData("DROP TABLE dbo.Orders")]
    [InlineData("DROP TABLE IF EXISTS dbo.Orders")]
    [InlineData("CREATE OR ALTER PROCEDURE dbo.GetOrders AS SELECT 1")]
    [InlineData("CREATE PROC dbo.GetOrders AS SELECT 1")]
    [InlineData("ALTER VIEW dbo.v AS SELECT 1 AS a")]
    [InlineData("CREATE FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END")]
    [InlineData("DROP SYNONYM dbo.s")]
    [InlineData("CREATE SCHEMA Sales")]
    [InlineData("SELECT * INTO dbo.OrdersCopy FROM dbo.Orders")]
    [InlineData("SELECT a, (SELECT MAX(b) FROM dbo.x) AS m INTO dbo.Copy FROM dbo.Orders")]
    [InlineData("EXEC sp_rename 'dbo.Orders.Note', 'Remark', 'COLUMN'")]
    [InlineData("EXEC sys.sp_rename 'dbo.Orders', 'Orders2'")]
    [InlineData("SELECT 1;\nGO\nCREATE TABLE [dbo].[New Table] (Id int)")]
    [InlineData("CREATE TABLE OtherDb.dbo.Orders (Id int)")]
    public void ChangesSchema(string text)
    {
        Assert.True(SchemaChangeDetector.Find(text).ChangesSchema);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders")]
    [InlineData("INSERT INTO dbo.Orders (Id) VALUES (1)")]
    [InlineData("UPDATE dbo.Orders SET Note = 'CREATE TABLE x' WHERE Id = 1")]
    [InlineData("-- DROP TABLE dbo.Orders\nSELECT 1")]
    [InlineData("/* ALTER TABLE dbo.Orders ADD x int */ SELECT 1")]
    [InlineData("CREATE TABLE #work (Id int)")]
    [InlineData("DROP TABLE IF EXISTS #work")]
    [InlineData("DROP TABLE #work")]
    [InlineData("SELECT * INTO #copy FROM dbo.Orders")]
    [InlineData("DECLARE @t TABLE (Id int); INSERT INTO @t SELECT 1")]
    [InlineData("MERGE INTO dbo.Orders AS t USING dbo.Src AS s ON t.Id = s.Id WHEN MATCHED THEN DELETE;")]
    [InlineData("FETCH NEXT FROM c INTO @id")]
    [InlineData("UPDATE dbo.Orders SET Note = 'x' OUTPUT inserted.Id INTO dbo.AuditLog (Id) WHERE Id = 1")]
    [InlineData("DELETE FROM dbo.Orders OUTPUT deleted.Id, deleted.Note INTO dbo.AuditLog WHERE Id = 1")]
    [InlineData("INSERT dbo.Orders (Id) OUTPUT inserted.Id INTO dbo.AuditLog SELECT 1")]
    [InlineData("EXEC ('CREATE TABLE dbo.Dyn (Id int)')")]
    [InlineData("CREATE DATABASE Shop2")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (Id)")]
    [InlineData("ALTER DATABASE CURRENT SET RECOVERY SIMPLE")]
    [InlineData("CREATE LOGIN x WITH PASSWORD = 'p'")]
    [InlineData("")]
    public void DoesNotChangeSchema(string text)
    {
        Assert.False(SchemaChangeDetector.Find(text).ChangesSchema);
    }

    [Fact]
    public void Databases_FromUseStatements()
    {
        var change = SchemaChangeDetector.Find("USE Shop;\nCREATE TABLE dbo.a (Id int);\nUSE [Other Db]\nGO\nUSE shop");
        Assert.True(change.ChangesSchema);
        Assert.Equal(new[] { "Shop", "Other Db" }, change.Databases);
    }

    [Theory]
    [InlineData("CREATE DATABASE Shop2", true)]
    [InlineData("DROP DATABASE IF EXISTS Shop2", true)]
    [InlineData("ALTER DATABASE Shop2 MODIFY NAME = Shop3", true)]
    [InlineData("CREATE TABLE dbo.a (Id int)", false)]
    [InlineData("SELECT 'CREATE DATABASE x'", false)]
    public void ChangesDatabaseList(string text, bool expected)
    {
        Assert.Equal(expected, SchemaChangeDetector.Find(text).ChangesDatabaseList);
    }

    [Fact]
    public void NoUse_NoDatabases()
    {
        Assert.Empty(SchemaChangeDetector.Find("CREATE TABLE dbo.a (Id int)").Databases);
    }
}
