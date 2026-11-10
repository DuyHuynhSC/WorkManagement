using WorkManagement.Services;
using Xunit;

namespace WorkManagement.Tests;

public class RemoteDesktopAndSchedulerServiceTests
{
    [Theory]
    [InlineData("Administrator", "D2-SVR15-6016", "D2-SVR15-6016\\Administrator")]
    [InlineData("User01", "D2-VM-7023", "D2-VM-7023\\User01")]
    [InlineData("Administrator", "192.168.1.100", "192.168.1.100\\Administrator")]
    [InlineData("User01", "", "User01")]
    [InlineData("D2-SVR\\Admin", "D2-SVR", "D2-SVR\\Admin")]
    [InlineData("admin@domain.com", "D2-SVR", "admin@domain.com")]
    public void FormatUsername_FormatsProperlyForHostOrIpAndUser(string user, string hostOrIp, string expected)
    {
        string actual = RemoteDesktopService.FormatUsername(user, hostOrIp);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildCliCommand_IncludesAllFlagsCorrectly()
    {
        string cmd = TaskSchedulerService.BuildCliCommand("--auto", allProfiles: true, sendEmail: true);
        Assert.Contains("--auto", cmd);
        Assert.Contains("--all", cmd);
        Assert.Contains("--send-email", cmd);
    }

    [Fact]
    public void BuildSchtasksCreateCommand_GeneratesValidSchtasksSyntax()
    {
        string schtasksCmd = TaskSchedulerService.BuildSchtasksCreateCommand("MyTask", "23:00", "--preview", allProfiles: true, sendEmail: false);
        Assert.StartsWith("schtasks /create /tn \"MyTask\"", schtasksCmd);
        Assert.Contains("/sc daily /st 23:00 /f", schtasksCmd);
        Assert.Contains("--preview", schtasksCmd);
        Assert.Contains("--all", schtasksCmd);
        Assert.DoesNotContain("--send-email", schtasksCmd);
    }

    [Fact]
    public void BuildPowerShellLogonCommand_GeneratesValidPowerShellSyntaxWithDelay()
    {
        string psCmd = TaskSchedulerService.BuildPowerShellLogonCommand("WorkManagement_MorningPreview", "--preview", allProfiles: true, sendEmail: true, delayMinutes: 1);
        Assert.StartsWith("powershell -NoProfile -Command", psCmd);
        Assert.Contains("New-ScheduledTaskTrigger -AtLogOn", psCmd);
        Assert.Contains("$t.Delay = 'PT1M'", psCmd);
        Assert.Contains("Register-ScheduledTask -TaskName 'WorkManagement_MorningPreview'", psCmd);
        Assert.Contains("--preview --all --send-email", psCmd);
    }

    [Fact]
    public void BuildCommandPreview_RoutesToCorrectEngine()
    {
        string logonPreview = TaskSchedulerService.BuildCommandPreview(TaskSchedulerTriggerType.AtLogonWithDelay, "TaskA", "23:00", "--preview", true, true);
        Assert.Contains("-AtLogOn", logonPreview);

        string dailyPreview = TaskSchedulerService.BuildCommandPreview(TaskSchedulerTriggerType.DailyTime, "TaskB", "08:30", "--auto", true, false);
        Assert.StartsWith("schtasks /create", dailyPreview);
        Assert.Contains("/st 08:30", dailyPreview);
    }
}
