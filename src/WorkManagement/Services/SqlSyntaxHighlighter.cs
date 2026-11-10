using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WorkManagement.Services;

public static class SqlSyntaxHighlighter
{
    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "DROP", "USER", "CASCADE", "ALTER", "SYSTEM",
        "KILL", "SESSION", "TABLESPACE", "DATAFILES", "INCLUDING", "CONTENTS",
        "PURGE", "CREATE", "REPLACE", "PROCEDURE", "PACKAGE", "BODY", "FUNCTION",
        "TRIGGER", "VIEW", "INDEX", "TABLE", "INSERT", "UPDATE", "DELETE", "INTO",
        "VALUES", "SET", "COMMIT", "ROLLBACK", "GRANT", "REVOKE", "EXEC", "EXECUTE",
        "BEGIN", "END", "EXCEPTION", "WHEN", "THEN", "DECLARE", "CURSOR", "LOOP",
        "IF", "ELSE", "ELSIF", "NULL", "NOT", "OR", "AND", "IN", "LIKE", "IS",
        "AS", "ORDER", "BY", "GROUP", "HAVING", "UNION", "ALL", "EXISTS", "BETWEEN",
        "TRUNCATE", "DATABASE", "SCHEMA", "SYSDBA", "IMMEDIATE", "COMPILE", "REBUILD",
        "COUNT", "ROWNUM", "DUAL", "EXPDP", "IMPDP", "DIRECTORY", "DUMPFILE", "LOGFILE",
        "SCHEMAS", "TABLES", "QUERY", "FORCE", "CONNECT", "IDENTIFIED"
    };

    public static void Highlight(RichTextBox rtb, string text, bool isDarkTheme)
    {
        var doc = new FlowDocument
        {
            PageWidth = 2500,
            TextAlignment = TextAlignment.Left
        };

        // Bảng màu tối ưu theo Theme
        var normalBrush = isDarkTheme
            ? new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC))  // Trắng sáng sắc nét
            : new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)); // Đen than tương phản tuyệt đối

        var keywordBrush = isDarkTheme
            ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8))  // Cyan sáng
            : new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)); // Xanh dương đậm

        var commentBrush = isDarkTheme
            ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))  // Xanh lá sáng
            : new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D)); // Xanh lá cây đậm

        var stringBrush = isDarkTheme
            ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))  // Cam ấm
            : new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09)); // Nâu cam đậm

        var headingBrush = isDarkTheme
            ? new SolidColorBrush(Color.FromRgb(0x81, 0x8C, 0xF8))  // Tím chàm sáng
            : new SolidColorBrush(Color.FromRgb(0x43, 0x38, 0xCA)); // Tím chàm đậm

        var fenceBrush = isDarkTheme
            ? new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
            : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

        string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');

        foreach (string line in lines)
        {
            var p = new Paragraph
            {
                Margin = new Thickness(0, 1.5, 0, 1.5),
                LineHeight = 18
            };

            string trimmed = line.TrimStart();

            // 1. Tiêu đề Markdown (### hoặc ##)
            if (trimmed.StartsWith("#"))
            {
                p.Inlines.Add(new Run(line)
                {
                    Foreground = headingBrush,
                    FontWeight = FontWeights.Bold
                });
            }
            // 2. Khối Code fence (```)
            else if (trimmed.StartsWith("```"))
            {
                p.Inlines.Add(new Run(line)
                {
                    Foreground = fenceBrush,
                    FontStyle = FontStyles.Italic
                });
            }
            // 3. Toàn bộ dòng là Comment SQL (-- ...)
            else if (trimmed.StartsWith("--"))
            {
                p.Inlines.Add(new Run(line)
                {
                    Foreground = commentBrush,
                    FontStyle = FontStyles.Italic
                });
            }
            // 4. Dòng mã hoặc văn bản thông thường: Phân tích token chi tiết
            else
            {
                ColorizeLine(p, line, normalBrush, keywordBrush, commentBrush, stringBrush);
            }

            doc.Blocks.Add(p);
        }

        rtb.Document = doc;
    }

    private static void ColorizeLine(
        Paragraph p,
        string line,
        Brush normalBrush,
        Brush keywordBrush,
        Brush commentBrush,
        Brush stringBrush)
    {
        // Regex bóc tách:
        // Group 1: Comment cuối dòng (--.*)
        // Group 2: Chuỗi ký tự ('[^']*')
        // Group 3: Từ định danh / từ khóa (\b[A-Za-z0-9_]+\b)
        // Group 4: Các ký tự khác
        var regex = new Regex(@"(--.*$)|('([^'\\]|\\.)*')|(\b[A-Za-z_][A-Za-z0-9_]*\b)|(\S+|\s+)", RegexOptions.Compiled);
        var matches = regex.Matches(line);

        bool inInlineComment = false;

        foreach (Match match in matches)
        {
            string val = match.Value;

            if (inInlineComment || match.Groups[1].Success)
            {
                inInlineComment = true;
                p.Inlines.Add(new Run(val)
                {
                    Foreground = commentBrush,
                    FontStyle = FontStyles.Italic
                });
            }
            else if (match.Groups[2].Success)
            {
                p.Inlines.Add(new Run(val)
                {
                    Foreground = stringBrush,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else if (match.Groups[4].Success)
            {
                if (SqlKeywords.Contains(val))
                {
                    p.Inlines.Add(new Run(val)
                    {
                        Foreground = keywordBrush,
                        FontWeight = FontWeights.Bold
                    });
                }
                else
                {
                    p.Inlines.Add(new Run(val)
                    {
                        Foreground = normalBrush
                    });
                }
            }
            else
            {
                p.Inlines.Add(new Run(val)
                {
                    Foreground = normalBrush
                });
            }
        }

        if (p.Inlines.Count == 0)
        {
            p.Inlines.Add(new Run(string.Empty));
        }
    }

    public static string GetPlainText(RichTextBox rtb)
    {
        if (rtb?.Document == null) return string.Empty;
        var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        return range.Text.TrimEnd('\r', '\n');
    }

    public static string ExtractPureSql(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

        // Nếu nội dung có khối ```sql ... ``` thì gom toàn bộ các khối lệnh SQL
        var matches = Regex.Matches(rawText, @"```(?:sql|plsql)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (matches.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (Match m in matches)
            {
                string snippet = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    sb.AppendLine(snippet);
                    sb.AppendLine();
                }
            }
            string combined = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(combined))
            {
                return combined;
            }
        }

        return rawText.Trim();
    }
}
