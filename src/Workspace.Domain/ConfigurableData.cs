using System.Globalization;
using System.Text.Json;

namespace Workspace.Domain;

public enum CollectionKind { Cards, Accounts, Free }
public enum FieldKind { Text, Integer, Number, SingleSelect, MultiSelect, Checkbox, Date, DateTime, Relation, Marker }
public enum FieldScope { Individual, Common, Shared }
public enum FieldBinding { None, Resource }
public sealed record FieldOption(Guid Id, string Name, string Color);

public static class BuiltInCollections
{
    public static readonly Guid Cards = Guid.Parse("40000000-0000-0000-0000-000000000001");
    public static readonly Guid Accounts = Guid.Parse("40000000-0000-0000-0000-000000000002");
}

public sealed class CollectionDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public CollectionKind Kind { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class DataRecord
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string Name { get; set; } = "";
    public long Version { get; set; } = 1;
}
public sealed class FieldDefinition
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string Name { get; set; } = "";
    public FieldKind Kind { get; set; }
    public FieldScope Scope { get; set; } = FieldScope.Common;
    public FieldBinding Binding { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public Guid? RelationCollectionId { get; set; }
    public string Color { get; set; } = "#526b84";
    public int Position { get; set; }
    public bool Required { get; set; }
    public bool Hidden { get; set; }
    public bool Deleted { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class RecordFieldValue
{
    public Guid FieldId { get; set; }
    public Guid RecordId { get; set; }
    public string ValueJson { get; set; } = "null";
    public bool Attached { get; set; } = true;
    public long Version { get; set; } = 1;
}
public sealed class SharedFieldValue
{
    public Guid FieldId { get; set; }
    public string ValueJson { get; set; } = "null";
    public long Version { get; set; } = 1;
}
public sealed class ViewDefinition
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string Name { get; set; } = "";
    public string Display { get; set; } = "Table";
    public int Position { get; set; }
    public Guid? StageId { get; set; }
    public string HiddenFieldsJson { get; set; } = "[]";
    public Guid? SortFieldId { get; set; }
    public bool Descending { get; set; }
    public Guid? FilterFieldId { get; set; }
    public string FilterText { get; set; } = "";
    public long Version { get; set; } = 1;
}
public sealed class StageDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public long Version { get; set; } = 1;
    public StageRequirement EntryRequirement { get; set; }
    public string AllowedFromStageIdsJson { get; set; } = "[]";
}
public sealed class WorkspaceSettings
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "協作工作區";
    public string CardLabel { get; set; } = "卡片";
    public string SettingsLabel { get; set; } = "設定";
    public long Version { get; set; } = 1;
}

public static class FieldValuePolicy
{
    public static string Name(string? name) => string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80
        ? throw new DomainRuleException("名稱需為 1 到 80 個字元。") : name.Trim();

    public static void Validate(FieldDefinition field, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            if (field.Required) throw new DomainRuleException("此欄位為必填，不能清空。");
            return;
        }
        var options = JsonSerializer.Deserialize<FieldOption[]>(field.OptionsJson)!;
        bool IsOption(JsonElement v) => v.ValueKind == JsonValueKind.String && v.TryGetGuid(out var id) && options.Any(x => x.Id == id);
        var valid = field.Kind switch
        {
            FieldKind.Text => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 4000 && (!field.Required || !string.IsNullOrWhiteSpace(value.GetString())),
            FieldKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && Math.Abs((decimal)n) <= 9007199254740991m,
            FieldKind.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            FieldKind.SingleSelect => IsOption(value),
            FieldKind.MultiSelect => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(IsOption) && value.EnumerateArray().Select(x => x.GetString()).Distinct().Count() == value.GetArrayLength() && (!field.Required || value.GetArrayLength() > 0),
            FieldKind.Checkbox or FieldKind.Marker => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            FieldKind.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            FieldKind.DateTime => value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out _) && (value.GetString()!.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(value.GetString()!, @"[+-]\d{2}:\d{2}$")),
            FieldKind.Relation => value.ValueKind == JsonValueKind.String && value.TryGetGuid(out _),
            _ => false
        };
        if (!valid) throw new DomainRuleException("值不符合目前欄位型別或選項，請確認後再保存。");
    }
}
