namespace Workspace.Domain;

public enum StageRequirement { None, Level, Task, Qualification }
public enum CreditKind { Income, Conversion, Correction }
public static class GameDayPolicy
{
    public static DateTimeOffset NextBoundary(DateTimeOffset qualifiedAt, string timeZoneId, int resetHour = 4)
    {
        if (resetHour is < 0 or > 23) throw new DomainRuleException("換日時間不正確。");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new DomainRuleException("找不到指定時區。"); }
        var local = TimeZoneInfo.ConvertTime(qualifiedAt, zone).DateTime;
        DateTimeOffset Resolve(DateTime date)
        {
            var boundary = DateTime.SpecifyKind(date.AddHours(resetHour), DateTimeKind.Unspecified);
            // Move a nonexistent boundary to the first valid minute; use the later
            // instant for ambiguous time, so eligibility never starts early.
            while (zone.IsInvalidTime(boundary)) boundary = boundary.AddMinutes(1);
            var offset = zone.IsAmbiguousTime(boundary) ? zone.GetAmbiguousTimeOffsets(boundary).Min() : zone.GetUtcOffset(boundary);
            return new DateTimeOffset(boundary, offset).ToUniversalTime();
        }
        var candidate = Resolve(local.Date);
        return candidate > qualifiedAt ? candidate : Resolve(local.Date.AddDays(1));
    }
}
public sealed class ProgressionProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long LevelTarget { get; set; }
    public long TaskItemTarget { get; set; }
    public long MeritTarget { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class ProgressionSettings
{
    public int Id { get; set; } = 1;
    public string TimeZoneId { get; set; } = "Asia/Taipei";
    public int ResetHour { get; set; } = 4;
    public bool ConversionRequiresActiveActivity { get; set; }
    public Guid AccumulationStageId { get; set; } = Guid.Parse("50000000-0000-0000-0000-000000000003");
    public long Version { get; set; } = 1;
}
public sealed class CardProgression
{
    public const long Maximum = 9007199254740991;
    public Guid CardId { get; set; }
    public Guid? ProfileId { get; set; }
    public long Level { get; set; }
    public long TaskItems { get; set; }
    public long MeritBalance { get; set; }
    public long CumulativeCredits { get; set; }
    public long CreditVersion { get; set; }
    public Guid? CurrentCycleId { get; set; }
    public bool PermanentlyDisqualified { get; set; }
    public long Version { get; set; }
    public static void Amount(long value, bool positive = false)
    {
        if (value < (positive ? 1 : 0) || value > Maximum) throw new DomainRuleException(positive ? "數量必須為可安全保存的正整數。" : "數量必須為可安全保存的非負整數。");
    }
    public bool Meets(ProgressionProfile profile) => Level >= profile.LevelTarget && TaskItems >= profile.TaskItemTarget && MeritBalance >= profile.MeritTarget;
    public void Credit(long amount)
    {
        Amount(amount, true);
        if (CumulativeCredits > Maximum - amount) throw new DomainRuleException("累積金幣超過可保存範圍。");
        CumulativeCredits += amount; CreditVersion++;
    }
    public void Convert(long amount, QualificationCycle? cycle, bool hasActiveActivity, bool activeRequired)
    {
        Amount(amount, true);
        if (PermanentlyDisqualified || cycle?.ActivatedAt == null || cycle.InvalidatedAt != null)
            throw new DomainRuleException("目前資格輪次尚未成功入場或已失效，不能記錄兌換。");
        if (activeRequired && !hasActiveActivity) throw new DomainRuleException("目前設定要求仍在活動中才能記錄兌換。");
        if (amount > MeritBalance) throw new DomainRuleException("兌換量不能超過目前已確認的功勳。");
        Credit(amount); MeritBalance -= amount;
    }
}
public sealed class QualificationCycle
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public DateTimeOffset QualifiedAt { get; set; }
    public DateTimeOffset EligibleFrom { get; set; }
    public string TimeZoneId { get; set; } = "";
    public int ResetHour { get; set; }
    public string ThresholdSnapshotJson { get; set; } = "{}";
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
    public string InvalidationReason { get; set; } = "";
    public void EnsureEligible(DateTimeOffset now)
    {
        if (InvalidatedAt != null) throw new DomainRuleException("本輪資格已失效，請重新累積並達標。");
        if (now < EligibleFrom) throw new DomainRuleException("尚未到達本輪可投入時間。");
    }
}
public sealed class ActivitySession
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid QualificationCycleId { get; set; }
    public Guid OperatorParticipantId { get; set; }
    public bool OperatorWasHidden { get; set; }
    public string Channel { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class CreditEntry
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid RequestId { get; set; }
    public CreditKind Kind { get; set; }
    public long Amount { get; set; }
    public long TotalAfter { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}
public sealed class CommandReceipt
{
    public Guid RequestId { get; set; }
    public Guid CardId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
}
