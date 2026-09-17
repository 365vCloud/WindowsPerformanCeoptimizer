namespace WPO.Domain.Models;

/// <summary>
/// A diagnostic measurement that distinguishes an unavailable value from zero.
/// </summary>
public sealed record MetricValue<T>
    where T : struct
{
    public required bool IsAvailable { get; init; }

    public T? Value { get; init; }

    public string? UnavailabilityReason { get; init; }

    public static MetricValue<T> Available(T value) => new()
    {
        IsAvailable = true,
        Value = value
    };

    public static MetricValue<T> Unavailable(string reason) => new()
    {
        IsAvailable = false,
        UnavailabilityReason = reason
    };
}
