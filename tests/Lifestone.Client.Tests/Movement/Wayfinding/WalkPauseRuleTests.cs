using Lifestone.Client.Movement.Wayfinding;
using Xunit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>The world walk pauses for what runs, not for what is mentioned. A miss here runs the walk through a gate, so the real launch lines are pinned.</summary>
public sealed class WalkPauseRuleTests
{
    [Theory]
    [InlineData(@"pwsh -NoProfile -File tools\live-gate.ps1 -Tag g1 -Loopback full", "live-gate")]
    [InlineData(@"""C:\Program Files\PowerShell\7\pwsh.exe"" -NoProfile -File ""C:\a b\tools\smoke.ps1"" -Only x -Backend require", "smoke")]
    [InlineData(@"pwsh.exe -NoProfile tools\smoke.ps1", "smoke")]
    [InlineData(@"pwsh -Command ""& .\tools\soak.ps1 -Minutes 60""", "soak")]
    [InlineData(@"pwsh -NoProfile -Command tools\soak.ps1", "soak")]
    [InlineData(@"pwsh -c ""cd x; tools\live-gate.ps1 -Plan""", "live-gate")]
    [InlineData(@"pwsh -File tools\slice-check.ps1 -Gate", "slice-check-gate")]
    [InlineData(@"dotnet test --filter Category=Loopback", "loopback-tests")]
    [InlineData(@"testhost.exe --source tests\Lifestone.Loopback.Tests\bin\x.dll", "loopback-tests")]
    [InlineData(@"pwsh -NoProfile -File tools/climb.ps1 -SkipCreate -Minutes 2880", "climb")]
    public void A_real_run_pauses_the_walk(string line, string reason) => Assert.Equal(reason, WalkPauseRule.Reason(line, ignoreClimb: false));

    [Theory]
    [InlineData(@"pwsh -Command ""Select-String -Path tools\live-gate.ps1 -Pattern Tag""")]
    [InlineData(@"pwsh -Command ""git log -- tools/smoke.ps1""")]
    [InlineData(@"pwsh -NoProfile -Command ""Get-Content tools\soak.ps1 | Select-Object -First 5""")]
    [InlineData(@"pwsh -Command ""git diff master -- tools/slice-check.ps1 tools/climb.ps1""")]
    [InlineData(@"pwsh -File tools\slice-check.ps1 -Slice 3")]
    [InlineData(@"dotnet build Lifestone.slnx --disable-build-servers")]
    public void A_mention_does_not_pause_the_walk(string line) => Assert.Null(WalkPauseRule.Reason(line, ignoreClimb: false));

    [Fact]
    public void The_climb_pauses_only_when_it_is_not_ignored() => Assert.Null(WalkPauseRule.Reason(@"pwsh -File tools/climb.ps1", ignoreClimb: true));
}
