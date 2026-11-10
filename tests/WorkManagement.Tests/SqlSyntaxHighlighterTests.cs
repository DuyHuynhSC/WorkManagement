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
}
