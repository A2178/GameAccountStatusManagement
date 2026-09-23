using Workspace.Domain;

namespace Workspace.Domain.Tests;

public sealed class ProgressionTests
{
    [Theory]
    [InlineData("2026-09-09T03:59:59+08:00", "2026-09-08T20:00:00Z")]
    [InlineData("2026-09-09T04:00:00+08:00", "2026-09-09T20:00:00Z")]
    [InlineData("2026-09-09T20:00:00+08:00", "2026-09-09T20:00:00Z")]
    [InlineData("2026-09-30T23:59:59+08:00", "2026-09-30T20:00:00Z")]
    [InlineData("2026-12-31T04:00:00+08:00", "2026-12-31T20:00:00Z")]
    [InlineData("2028-02-29T12:00:00+08:00", "2028-02-29T20:00:00Z")]
    public void AC033_035_next_boundary_is_strictly_later_and_in_workspace_time(string reached, string expected)
        => Assert.Equal(DateTimeOffset.Parse(expected), GameDayPolicy.NextBoundary(DateTimeOffset.Parse(reached), "Asia/Taipei"));
    [Fact]
    public void Configurable_zone_handles_missing_and_ambiguous_boundary_conservatively()
    {
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T07:00:00Z"), GameDayPolicy.NextBoundary(DateTimeOffset.Parse("2026-03-08T06:59:00Z"), "America/New_York", 2));
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T06:00:00Z"), GameDayPolicy.NextBoundary(DateTimeOffset.Parse("2026-11-01T04:59:00Z"), "America/New_York", 1));
        // The first 01:30 is still before the chosen (second) 01:00 boundary.
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T06:00:00Z"), GameDayPolicy.NextBoundary(DateTimeOffset.Parse("2026-11-01T05:30:00Z"), "America/New_York", 1));
        Assert.Equal(DateTimeOffset.Parse("2026-11-02T06:00:00Z"), GameDayPolicy.NextBoundary(DateTimeOffset.Parse("2026-11-01T06:00:00Z"), "America/New_York", 1));
    }
    [Fact]
    public void AC037_server_boundary_and_invalidation_are_checked()
    {
        var cycle = new QualificationCycle { EligibleFrom = DateTimeOffset.Parse("2026-09-09T20:00:00Z") };
        Assert.Throws<DomainRuleException>(() => cycle.EnsureEligible(cycle.EligibleFrom.AddTicks(-1)));
        cycle.EnsureEligible(cycle.EligibleFrom); cycle.InvalidatedAt = cycle.EligibleFrom;
        Assert.Throws<DomainRuleException>(() => cycle.EnsureEligible(cycle.EligibleFrom.AddDays(1)));
    }
    [Fact]
    public void AC038_conversion_to_zero_preserves_activated_milestone()
    {
        var state = new CardProgression { MeritBalance = 1000, CumulativeCredits = 200 }; var cycle = new QualificationCycle { ActivatedAt = DateTimeOffset.UtcNow };
        state.Convert(300, cycle, false, false); Assert.Equal(700, state.MeritBalance); Assert.Equal(500, state.CumulativeCredits);
        state.Convert(700, cycle, false, false); Assert.Equal(0, state.MeritBalance); Assert.Equal(1200, state.CumulativeCredits); Assert.NotNull(cycle.ActivatedAt);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1001)]
    public void AC039_invalid_conversions_leave_balances_unchanged(long amount)
    {
        var state = new CardProgression { MeritBalance = 1000 }; var cycle = new QualificationCycle { ActivatedAt = DateTimeOffset.UtcNow };
        Assert.Throws<DomainRuleException>(() => state.Convert(amount, cycle, true, false)); Assert.Equal(1000, state.MeritBalance); Assert.Equal(0, state.CumulativeCredits);
    }
    [Fact]
    public void AC039_disqualified_or_inactive_cycles_cannot_convert_and_overflow_does_not_deduct()
    {
        var state = new CardProgression { MeritBalance = 10, CumulativeCredits = CardProgression.Maximum };
        var cycle = new QualificationCycle(); Assert.Throws<DomainRuleException>(() => state.Convert(1, cycle, true, false));
        cycle.ActivatedAt = DateTimeOffset.UtcNow;
        Assert.Throws<DomainRuleException>(() => state.Convert(1, cycle, true, false)); Assert.Equal(10, state.MeritBalance);
        state.CumulativeCredits = 0; Assert.Throws<DomainRuleException>(() => state.Convert(1, cycle, false, true));
        state.PermanentlyDisqualified = true; Assert.Throws<DomainRuleException>(() => state.Convert(1, cycle, true, false));
    }
}
