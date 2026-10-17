using SsmsHelper.Core.Execs;
using SsmsHelper.Core.Tests.Metadata;

namespace SsmsHelper.Core.Tests.Execs;

public class ExecGeneratorTests
{
    private static SampleSchema Sample() => new SampleSchema()
        .Procedure(1, "dbo", "GetOrders")
            .Parameter(1, 1, "@CustomerId")
            .Parameter(1, 2, "@From", "datetime2", maxLength: 8, precision: 27, scale: 7)
            .Parameter(1, 3, "@Status", "varchar", maxLength: 20)
        .Procedure(2, "dbo", "CountOrders")
            .Parameter(2, 1, "@CustomerId")
            .Parameter(2, 2, "@Total", output: true)
        .Procedure(3, "dbo", "Cleanup")
        .Procedure(4, "dbo", "Wide");

    private static async Task<(string Result, int Caret)> Generate(string marked, ExecLayout layout = ExecLayout.Expanded, SampleSchema? sample = null)
    {
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        var context = ExecContextAnalyzer.Analyze(text, caret);
        Assert.NotNull(context);
        var schema = await (sample ?? Sample()).LoadAsync();
        var procedure = schema.FindProcedure(context.Schema, context.Name);
        Assert.NotNull(procedure);

        var expansion = ExecGenerator.Generate(context, procedure, "\n", layout);
        return (text.Substring(0, context.ReplaceStart) + expansion.Text + text.Substring(caret), context.ReplaceStart + expansion.CaretOffset);
    }

    [Fact]
    public async Task Expanded_OneParameterPerLine_EmptyValues_TypeComments()
    {
        var (result, caret) = await Generate("EXEC dbo.GetOrders|");

        Assert.Equal(
            """
            EXEC dbo.GetOrders
                @CustomerId = , -- int
                @From = ,       -- datetime2
                @Status =       -- varchar(20)
            ;
            """.ReplaceLineEndings("\n"),
            result);
        Assert.Equal(result.IndexOf("@CustomerId = ", StringComparison.Ordinal) + "@CustomerId = ".Length, caret);
    }

    [Fact]
    public async Task Expanded_MarksOutputParameters()
    {
        var (result, _) = await Generate("EXEC dbo.CountOrders|");

        Assert.Contains("@Total =        -- int OUTPUT", result);
    }

    [Fact]
    public async Task Compact_OnTheExecLine()
    {
        var (result, caret) = await Generate("EXEC dbo.GetOrders|", ExecLayout.Compact);

        Assert.Equal("EXEC dbo.GetOrders @CustomerId = , @From = , @Status = ;", result);
        Assert.Equal("EXEC dbo.GetOrders @CustomerId = ".Length, caret);
    }

    [Fact]
    public async Task Compact_WrapsLongListsUnderExec()
    {
        var sample = Sample();
        for (var i = 1; i <= 12; i++)
        {
            sample.Parameter(4, i, $"@SomeLongParameterName{i:00}");
        }

        var (result, _) = await Generate("  EXEC dbo.Wide|", ExecLayout.Compact, sample);

        var lines = result.Split('\n');
        Assert.True(lines.Length > 1);
        Assert.StartsWith("  EXEC dbo.Wide @SomeLongParameterName01 = , ", lines[0]);
        Assert.All(lines, l => Assert.True(l.Length <= 120, l));
        Assert.All(lines[1..], l => Assert.StartsWith("      @SomeLongParameterName", l));
        Assert.EndsWith("@SomeLongParameterName12 = ;", lines[^1]);
    }

    [Fact]
    public async Task FollowsTheExecLineIndent()
    {
        var (result, _) = await Generate("BEGIN\n  EXEC dbo.CountOrders|\nEND");

        Assert.Equal("BEGIN\n  EXEC dbo.CountOrders\n      @CustomerId = , -- int\n      @Total =        -- int OUTPUT\n  ;\nEND", result);
    }

    [Theory]
    [InlineData(ExecLayout.Expanded)]
    [InlineData(ExecLayout.Compact)]
    public async Task NoParameters_JustEndsTheStatement(ExecLayout layout)
    {
        var (result, caret) = await Generate("EXEC dbo.Cleanup|", layout);

        Assert.Equal("EXEC dbo.Cleanup;", result);
        Assert.Equal(result.Length, caret);
    }

    [Fact]
    public async Task UnqualifiedName_ResolvesInDbo()
    {
        var (result, _) = await Generate("EXEC Cleanup|");

        Assert.Equal("EXEC Cleanup;", result);
    }
}
