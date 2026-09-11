namespace Workspace.Domain;

public static class AssemblyMarker;

public enum CardUsageStatus
{
    Available,
    InUse,
    NotInUse
}

public enum ReservationState
{
    Reserved,
    Occupied,
    Released
}

public sealed class GameAccount
{
    private GameAccount() { }
    public GameAccount(Guid id, string displayName) { Id = id; Rename(displayName); }
    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public long CoordinationVersion { get; private set; }
    public void Rename(string value) => DisplayName = Required(value, "帳號名稱");
    public void AdvanceVersion() => CoordinationVersion++;
    private static string Required(string value, string label) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainRuleException($"{label}不能空白。") : value.Trim();
}

public sealed class CharacterCard
{
    private CharacterCard() { }
    public CharacterCard(Guid id, Guid accountId, string displayName)
    { Id = id; AccountId = accountId; Rename(displayName); }
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public CardUsageStatus UsageStatus { get; private set; }
    public Guid? PrimaryOperatorId { get; private set; }
    public void Rename(string value) => DisplayName = string.IsNullOrWhiteSpace(value)
        ? throw new DomainRuleException("卡片名稱不能空白。") : value.Trim();
    public void SetUsage(CardUsageStatus status, Guid? primaryOperatorId)
    { UsageStatus = status; PrimaryOperatorId = status == CardUsageStatus.InUse ? primaryOperatorId : null; }
}

public sealed class FieldRegion
{
    private FieldRegion() { }
    public FieldRegion(Guid id, string displayName) { Id = id; DisplayName = displayName.Trim(); }
    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
}

public sealed class ResourceReservation
{
    private ResourceReservation() { }
    public ResourceReservation(Guid id, Guid accountId, Guid cardId, Guid regionId, Guid actorId, DateTimeOffset now)
    { Id = id; AccountId = accountId; CardId = cardId; RegionId = regionId; State = ReservationState.Reserved; CreatedByParticipantId = actorId; UpdatedAt = now; Version = 1; }
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid CardId { get; private set; }
    public Guid RegionId { get; private set; }
    public ReservationState State { get; private set; }
    public Guid CreatedByParticipantId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }
    public bool IsActive => State is ReservationState.Reserved or ReservationState.Occupied;
    public void Enter(DateTimeOffset now)
    { if (State != ReservationState.Reserved) throw new DomainRuleException("只有尚未入場的預約可以回報入場。"); State = ReservationState.Occupied; Touch(now); }
    public void Cancel(DateTimeOffset now)
    { if (State != ReservationState.Reserved) throw new DomainRuleException("只有尚未入場的預約可以取消。"); State = ReservationState.Released; Touch(now); }
    public void ReturnHome(DateTimeOffset now)
    { if (State != ReservationState.Occupied) throw new DomainRuleException("只有已入場的卡片可以回報回村。"); State = ReservationState.Released; Touch(now); }
    private void Touch(DateTimeOffset now) { UpdatedAt = now; Version++; }
}

public static class AccountRegionPolicy
{
    public static void EnsureDestinationAllowed(IEnumerable<ResourceReservation> active, Guid destinationRegionId)
    {
        var occupiedRegion = active.Where(value => value.IsActive).Select(value => value.RegionId).Distinct().SingleOrDefault();
        if (occupiedRegion != Guid.Empty && occupiedRegion != destinationRegionId)
            throw new DomainRuleException("同一帳號仍有卡片預約或位於其他區域，請先取消預約或回村。");
    }
}

public sealed class DomainRuleException(string message) : Exception(message);
