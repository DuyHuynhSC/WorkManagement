using System;
using WorkManagement.Services;
using Xunit;

namespace WorkManagement.Tests;

public class SqlSyntaxHighlighterTests
{
    [Fact]
    public void ExtractPureSql_WhenContainsCodeBlocks_ExtractsSqlSnippets()
    {
        string rawText = @"
Chào bạn, đây là script:

### Script 1
```sql
ALTER SYSTEM KILL SESSION '123,456';
DROP USER TEST_USER CASCADE;
```

### Hướng dẫn chạy:
```bash
sqlplus sys/pass@orcl as sysdba
```

### Xác nhận:
```sql
SELECT count(*) FROM dba_users WHERE username = 'TEST_USER';
```
";

        string extracted = SqlSyntaxHighlighter.ExtractPureSql(rawText);

        Assert.Contains("ALTER SYSTEM KILL SESSION '123,456';", extracted);
        Assert.Contains("DROP USER TEST_USER CASCADE;", extracted);
        Assert.Contains("SELECT count(*) FROM dba_users WHERE username = 'TEST_USER';", extracted);
        Assert.DoesNotContain("Chào bạn", extracted);
    }

    [Fact]
    public void ExtractPureSql_WhenNoCodeBlocks_ReturnsOriginalTrimmed()
    {
        string rawText = "DROP USER DLS_RPS CASCADE;";
        string extracted = SqlSyntaxHighlighter.ExtractPureSql(rawText);
        Assert.Equal("DROP USER DLS_RPS CASCADE;", extracted);
    }

    [Theory]
    [InlineData(0x1E, 0x1E, 0x1E, true)]  // Material Light Terminal #1E1E1E -> Dark
    [InlineData(0x09, 0x0D, 0x16, true)]  // Material Dark Terminal #090D16 -> Dark
    [InlineData(0x23, 0x2B, 0x38, true)]  // Neumorphism Terminal #232B38 -> Dark
    [InlineData(0xFF, 0xFF, 0xFF, false)] // Pure White #FFFFFF -> Light
    [InlineData(0xF8, 0xFA, 0xFC, false)] // Off White #F8FAFC -> Light
    public void IsDarkBackground_CalculatesLuminanceAccurately(byte r, byte g, byte b, bool expectedIsDark)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        bool actual = SqlSyntaxHighlighter.IsDarkBackground(brush);
        Assert.Equal(expectedIsDark, actual);
    }

    [Fact]
    public void IsDarkBackground_NullBrush_DefaultsToTrue()
    {
        Assert.True(SqlSyntaxHighlighter.IsDarkBackground(null));
    }
}
