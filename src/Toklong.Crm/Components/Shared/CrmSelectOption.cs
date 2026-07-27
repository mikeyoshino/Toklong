namespace Toklong.Crm.Components.Shared;

public sealed record CrmSelectOption<TValue>(
    TValue Value,
    string Label,
    string? Description = null);
