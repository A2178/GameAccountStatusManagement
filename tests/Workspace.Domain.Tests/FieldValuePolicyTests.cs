using System.Text.Json;
using Workspace.Domain;

namespace Workspace.Domain.Tests;

public class FieldValuePolicyTests
{
    [Theory]
    [InlineData(FieldKind.Integer, "0", true)]
    [InlineData(FieldKind.Integer, "null", true)]
    [InlineData(FieldKind.Integer, "1.5", false)]
    [InlineData(FieldKind.Number, "1.5", true)]
    [InlineData(FieldKind.Text, "\"plain password\"", true)]
    [InlineData(FieldKind.Text, "false", false)]
    [InlineData(FieldKind.Checkbox, "false", true)]
    [InlineData(FieldKind.Marker, "true", true)]
    [InlineData(FieldKind.Date, "\"2026-02-30\"", false)]
    [InlineData(FieldKind.Date, "\"2026-02-28\"", true)]
    [InlineData(FieldKind.DateTime, "\"2026-09-17T04:00:00+08:00\"", true)]
    [InlineData(FieldKind.DateTime, "\"2026-09-17T04:00:00\"", false)]
    [InlineData(FieldKind.Relation, "\"not-an-id\"", false)]
    public void Typed_values_are_validated_without_coercing_null_or_zero(FieldKind kind, string json, bool valid)
    {
        var field = new FieldDefinition { Kind = kind };
        var error = Record.Exception(() => FieldValuePolicy.Validate(field, JsonSerializer.Deserialize<JsonElement>(json)));
        if (valid) Assert.Null(error); else Assert.IsType<DomainRuleException>(error);
    }
    [Fact]
    public void Required_values_and_option_ids_survive_rename()
    {
        var id = Guid.NewGuid();
        var field = new FieldDefinition { Kind = FieldKind.SingleSelect, Required = true, OptionsJson = JsonSerializer.Serialize(new[] { new FieldOption(id, "改名後職業", "#ffffff") }) };
        FieldValuePolicy.Validate(field, JsonSerializer.SerializeToElement(id));
        Assert.Throws<DomainRuleException>(() => FieldValuePolicy.Validate(field, JsonSerializer.SerializeToElement("改名後職業")));
        Assert.Throws<DomainRuleException>(() => FieldValuePolicy.Validate(field, JsonSerializer.SerializeToElement<object?>(null)));
        field.Kind = FieldKind.Integer;
        FieldValuePolicy.Validate(field, JsonSerializer.SerializeToElement(0));
    }
}
