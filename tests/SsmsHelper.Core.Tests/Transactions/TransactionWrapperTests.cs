using SsmsHelper.Core.Transactions;

namespace SsmsHelper.Core.Tests.Transactions;

public class TransactionWrapperTests
{
    [Fact]
    public void WrapsOneStatement_WithActiveRollback()
    {
        var wrapped = TransactionWrapper.Wrap(new[] { "DELETE FROM dbo.Reviews WHERE Rating < 2" }, "\n")!;
        Assert.Equal(
            "BEGIN TRANSACTION;\n\n" +
            "DELETE FROM dbo.Reviews WHERE Rating < 2\n\n" +
            "ROLLBACK TRANSACTION;\n" +
            "-- COMMIT TRANSACTION;",
            wrapped.Text);
        Assert.StartsWith("ROLLBACK", wrapped.Text.Substring(wrapped.CaretOffset));
    }

    [Fact]
    public void KeepsLinesAsTheyAre_DropsBlankEdges_AndAlignsWithTheLeastIndentedLine()
    {
        var lines = new[] { "", "    UPDATE dbo.Orders", "        SET Status = 'X'   ", "    WHERE OrderId = 1", "  " };
        var wrapped = TransactionWrapper.Wrap(lines, "\r\n")!;
        Assert.Equal(
            "    BEGIN TRANSACTION;\r\n\r\n" +
            "    UPDATE dbo.Orders\r\n" +
            "        SET Status = 'X'\r\n" +
            "    WHERE OrderId = 1\r\n\r\n" +
            "    ROLLBACK TRANSACTION;\r\n" +
            "    -- COMMIT TRANSACTION;",
            wrapped.Text);
        Assert.StartsWith("ROLLBACK", wrapped.Text.Substring(wrapped.CaretOffset));
    }

    [Fact]
    public void BlankLinesInside_AreKept()
    {
        var wrapped = TransactionWrapper.Wrap(new[] { "DELETE a", "", "DELETE b" }, "\n")!;
        Assert.Contains("DELETE a\n\nDELETE b\n", wrapped.Text);
    }

    [Theory]
    [InlineData]
    [InlineData("")]
    [InlineData("   ", "\t")]
    public void NothingButBlankLines_GivesNull(params string[] lines)
    {
        Assert.Null(TransactionWrapper.Wrap(lines, "\n"));
    }
}
