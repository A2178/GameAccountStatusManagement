using Workspace.Domain;

namespace Workspace.Domain.Tests;

public sealed class AccountRegionPolicyTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid RegionA = Guid.NewGuid();
    private static readonly Guid RegionB = Guid.NewGuid();

    [Fact]
    public void Same_account_can_have_multiple_cards_in_the_same_region()
    {
        var active = new[] { Reservation(Guid.NewGuid(), RegionA), Reservation(Guid.NewGuid(), RegionA) };
        AccountRegionPolicy.EnsureDestinationAllowed(active, RegionA);
    }

    [Fact]
    public void Same_account_cannot_target_a_different_region()
    {
        var exception = Assert.Throws<DomainRuleException>(() =>
            AccountRegionPolicy.EnsureDestinationAllowed([Reservation(Guid.NewGuid(), RegionA)], RegionB));
        Assert.Contains("其他區域", exception.Message);
    }

    [Fact]
    public void Released_reservations_do_not_prevent_switching_region()
    {
        var reservation = Reservation(Guid.NewGuid(), RegionA); reservation.Cancel(DateTimeOffset.UtcNow);
        AccountRegionPolicy.EnsureDestinationAllowed([reservation], RegionB);
    }

    [Fact]
    public void Occupancy_can_only_be_released_by_return_home()
    {
        var reservation = Reservation(Guid.NewGuid(), RegionA); reservation.Enter(DateTimeOffset.UtcNow);
        Assert.Throws<DomainRuleException>(() => reservation.Cancel(DateTimeOffset.UtcNow));
        reservation.ReturnHome(DateTimeOffset.UtcNow);
        Assert.False(reservation.IsActive);
    }

    private static ResourceReservation Reservation(Guid cardId, Guid regionId) =>
        new(Guid.NewGuid(), AccountId, cardId, regionId, Guid.NewGuid(), DateTimeOffset.UtcNow);
}
