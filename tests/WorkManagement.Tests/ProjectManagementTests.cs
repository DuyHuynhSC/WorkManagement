using System;
using System.IO;
using System.Linq;
using WorkManagement.Models;
using WorkManagement.Services;
using Xunit;

namespace WorkManagement.Tests;

public class ProjectManagementTests
{
    [Fact]
    public void ProjectEntry_Clone_CreatesDeepCopy()
    {
        var original = new ProjectEntry
        {
            No = "316",
            ProjectName = "UTSUGI",
            Description = "Customize V10",
            SourceFolder = @"Q:\V10_UTG",
            TnsName = "ORCL19F",
            HostServer = "172.16.6.23",
            ServiceName = "ORCL",
            Port = "1521",
            DbUsers = "UTG_RPS, UTG_USR1",
            Tablespace = "TS_UTSUGI",
            DbaUsername = "SYS",
            DbaPassword = "password123",
            Notes = "2026/06/23 Tạo mới",
            PersonInCharge = "Duy",
            Notes2 = "Note 2",
            IsSelected = true
        };

        var clone = original.Clone();

        Assert.Equal(original.No, clone.No);
        Assert.Equal(original.ProjectName, clone.ProjectName);
        Assert.Equal(original.Description, clone.Description);
        Assert.Equal(original.SourceFolder, clone.SourceFolder);
        Assert.Equal(original.TnsName, clone.TnsName);
        Assert.Equal(original.HostServer, clone.HostServer);
        Assert.Equal(original.ServiceName, clone.ServiceName);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.DbUsers, clone.DbUsers);
        Assert.Equal(original.Tablespace, clone.Tablespace);
        Assert.Equal(original.DbaUsername, clone.DbaUsername);
        Assert.Equal(original.DbaPassword, clone.DbaPassword);
        Assert.Equal(original.Notes, clone.Notes);
        Assert.Equal(original.PersonInCharge, clone.PersonInCharge);
        Assert.Equal(original.Notes2, clone.Notes2);
        Assert.Equal(original.IsSelected, clone.IsSelected);

        clone.ProjectName = "MODIFIED";
        Assert.Equal("UTSUGI", original.ProjectName);
    }

    [Fact]
    public void ParseUserList_StripsCommentsAndHandlesDelimiters()
    {
        string rawUsers = "COR_RPS, COR_USR1, COR_USR9, \r\nCOR_LOG\nCOR_TST_USR1(Dùng để test script); USER_EXTRA(ghi chu)";

        var parsed = ProjectCleanupService.ParseUserList(rawUsers);

        Assert.Equal(6, parsed.Count);
        Assert.Contains("COR_RPS", parsed);
        Assert.Contains("COR_USR1", parsed);
        Assert.Contains("COR_USR9", parsed);
        Assert.Contains("COR_LOG", parsed);
        Assert.Contains("COR_TST_USR1", parsed);
        Assert.Contains("USER_EXTRA", parsed);
    }

    [Fact]
    public void ParseTablespaceList_StripsCommentsAndHandlesDelimiters()
    {
        string rawTs = "TS_DATA(primary), TS_INDEX; TS_LOG\r\nTS_ARCHIVE (old)";

        var parsed = ProjectCleanupService.ParseTablespaceList(rawTs);

        Assert.Equal(4, parsed.Count);
        Assert.Contains("TS_DATA", parsed);
        Assert.Contains("TS_INDEX", parsed);
        Assert.Contains("TS_LOG", parsed);
        Assert.Contains("TS_ARCHIVE", parsed);
    }

    [Fact]
    public void ProjectManagerService_SaveAndLoad_RoundtripsSuccessfully()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"wm_proj_test_{Guid.NewGuid():N}.json");

        try
        {
            var service = new ProjectManagerService(tempFile);
            var sample = new ProjectEntry
            {
                No = "324",
                ProjectName = "CUORE",
                SourceFolder = @"Q:\V10_CUORE",
                HostServer = "172.16.6.23",
                DbUsers = "COR_RPS, COR_USR1",
                Tablespace = "TS_CUORE"
            };

            service.SaveAll(new[] { sample });
            var loaded = service.LoadAll();

            Assert.Single(loaded);
            Assert.Equal("CUORE", loaded[0].ProjectName);
            Assert.Equal(@"Q:\V10_CUORE", loaded[0].SourceFolder);
            Assert.Equal("172.16.6.23", loaded[0].HostServer);
            Assert.Equal("TS_CUORE", loaded[0].Tablespace);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ProjectExcelService_ExportTemplate_GeneratesFile()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"wm_template_{Guid.NewGuid():N}.xlsx");

        try
        {
            var service = new ProjectExcelService();
            await service.ExportTemplateAsync(tempFile);

            Assert.True(File.Exists(tempFile));
            var info = new FileInfo(tempFile);
            Assert.True(info.Length > 0);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task GetTablespacesForUsersAsync_EmptyUsers_ReturnsEmptyListWithoutDbCall()
    {
        var project = new ProjectEntry { HostServer = "127.0.0.1" };
        var result = await ProjectCleanupService.GetTablespacesForUsersAsync(project, "SYSTEM", "pwd", Array.Empty<string>());
        Assert.Empty(result);

        var whitespaceUsers = await ProjectCleanupService.GetTablespacesForUsersAsync(project, "SYSTEM", "pwd", new[] { "", "   " });
        Assert.Empty(whitespaceUsers);
    }

    [Fact]
    public void FilterByPersonInCharge_OnlyImportsSelectedProjects()
    {
        var projects = new List<ProjectEntry>
        {
            new() { ProjectName = "Proj1", PersonInCharge = "Duy" },
            new() { ProjectName = "Proj2", PersonInCharge = "Duy" },
            new() { ProjectName = "Proj3", PersonInCharge = "Tuấn" },
            new() { ProjectName = "Proj4", PersonInCharge = "" }
        };

        // Giả lập người dùng chỉ chọn người phụ trách "Duy"
        var selectedPersons = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Duy" };
        var filtered = projects
            .Where(p => selectedPersons.Contains((p.PersonInCharge ?? string.Empty).Trim()))
            .ToList();

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, p => Assert.Equal("Duy", p.PersonInCharge));
    }

    [Fact]
    public async Task ReadProjectsFromExcel_Row1And2Ignored_Row3IsTitle_ImportsFromRow4()
    {
        string tempXlsx = Path.Combine(Path.GetTempPath(), $"proj_row3_title_{Guid.NewGuid():N}.xlsx");
        try
        {
            var rows = new List<System.Collections.Generic.Dictionary<string, object>>
            {
                // Dòng 1: Tiêu đề báo cáo (bỏ qua)
                new()
                {
                    { "A", "DANH SÁCH DỰ ÁN NỘI BỘ" },
                    { "B", "" }, { "C", "" }, { "D", "" }, { "E", "" },
                    { "F", "" }, { "G", "" }, { "H", "" }, { "I", "" },
                    { "J", "" }, { "K", "" }, { "L", "" }
                },
                // Dòng 2: Ghi chú phiên bản (bỏ qua)
                new()
                {
                    { "A", "Cập nhật ngày 2026-11-09" },
                    { "B", "" }, { "C", "" }, { "D", "" }, { "E", "" },
                    { "F", "" }, { "G", "" }, { "H", "" }, { "I", "" },
                    { "J", "" }, { "K", "" }, { "L", "" }
                },
                // Dòng 3: Title của các cột
                new()
                {
                    { "A", "STT" },
                    { "B", "Tên Dự Án" },
                    { "C", "Mô tả" },
                    { "D", "Thư mục source" },
                    { "E", "Máy ảo" },
                    { "F", "SID" },
                    { "G", "Port" },
                    { "H", "DB User" },
                    { "I", "Tablespace" },
                    { "J", "Tài khoản DBA" },
                    { "K", "Người phụ trách" },
                    { "L", "Ghi chú" }
                },
                // Dòng 4: Dự án 1
                new()
                {
                    { "A", "1" },
                    { "B", "PROJ_ALPHA" },
                    { "C", "Dự án Alpha" },
                    { "D", @"D:\Sources\Alpha" },
                    { "E", "172.16.6.100" },
                    { "F", "ORCL" },
                    { "G", "1521" },
                    { "H", "USR_ALPHA" },
                    { "I", "TS_ALPHA" },
                    { "J", "SYSTEM" },
                    { "K", "Duy" },
                    { "L", "Khởi tạo" }
                },
                // Dòng 5: Dự án 2
                new()
                {
                    { "A", "2" },
                    { "B", "PROJ_BETA" },
                    { "C", "Dự án Beta" },
                    { "D", @"D:\Sources\Beta" },
                    { "E", "172.16.6.101" },
                    { "F", "ORCL19" },
                    { "G", "1521" },
                    { "H", "USR_BETA" },
                    { "I", "TS_BETA" },
                    { "J", "SYS" },
                    { "K", "Huy" },
                    { "L", "Bảo trì" }
                }
            };

            var workbook = new System.Collections.Generic.Dictionary<string, object>
            {
                { "Projects", rows }
            };

            MiniExcelLibs.MiniExcel.SaveAs(tempXlsx, workbook, overwriteFile: true);

            var service = new ProjectExcelService();
            var result = await service.ReadProjectsFromExcelAsync(tempXlsx);

            Assert.Equal(2, result.Count);

            var p1 = result[0];
            Assert.Equal("1", p1.No);
            Assert.Equal("PROJ_ALPHA", p1.ProjectName);
            Assert.Equal("Dự án Alpha", p1.Description);
            Assert.Equal(@"D:\Sources\Alpha", p1.SourceFolder);
            Assert.Equal("172.16.6.100", p1.HostServer);
            Assert.Equal("USR_ALPHA", p1.DbUsers);
            Assert.Equal("TS_ALPHA", p1.Tablespace);
            Assert.Equal("SYSTEM", p1.DbaUsername);
            Assert.Equal("Duy", p1.PersonInCharge);

            var p2 = result[1];
            Assert.Equal("2", p2.No);
            Assert.Equal("PROJ_BETA", p2.ProjectName);
            Assert.Equal(@"D:\Sources\Beta", p2.SourceFolder);
            Assert.Equal("172.16.6.101", p2.HostServer);
            Assert.Equal("Huy", p2.PersonInCharge);
        }
        finally
        {
            if (File.Exists(tempXlsx)) File.Delete(tempXlsx);
        }
    }
}
