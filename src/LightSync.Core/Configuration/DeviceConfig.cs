using System.Text.Json.Serialization;

namespace LightSync.Core.Configuration;

public sealed record DeviceConfig
{
    [JsonPropertyName("adapter")]
    public string Adapter { get; init; } = "fake";

    /// <summary>
    /// Free-form adapter settings. Kept as strings so Core stays ignorant of every vendor's
    /// shape and no polymorphic deserialisation is needed, which native AOT would not allow.
    /// Secrets are never stored here — settings name an environment variable instead.
    /// </summary>
    [JsonPropertyName("settings")]
    public Dictionary<string, string> Settings { get; init; } = [];

    public IReadOnlyList<string> Validate() =>
        string.IsNullOrWhiteSpace(Adapter) ? ["device.adapter must not be empty."] : [];

    // The compiler-generated record equality would compare Settings by reference, making two
    // configs holding the same values unequal. Configs are compared to detect changes, so
    // equality has to look at the settings themselves.
    public bool Equals(DeviceConfig? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(Adapter, other.Adapter, StringComparison.Ordinal)
            && Settings.Count == other.Settings.Count
            && Settings.All(pair =>
                other.Settings.TryGetValue(pair.Key, out var value)
                && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Adapter, StringComparer.Ordinal);
        hash.Add(Settings.Count);

        // Ordinal ordering so the hash does not depend on dictionary iteration order.
        foreach (var pair in Settings.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
