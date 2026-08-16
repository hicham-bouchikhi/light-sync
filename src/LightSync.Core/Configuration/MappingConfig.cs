using System.Text.Json.Serialization;
using LightSync.Core.Mapping;

namespace LightSync.Core.Configuration;

public sealed record MappingConfig
{
    [JsonPropertyName("zoneCount")]
    public int ZoneCount { get; init; } = 24;

    /// <summary>"vertical" or "horizontal".</summary>
    [JsonPropertyName("layout")]
    public string Layout { get; init; } = "vertical";

    /// <summary>"left-to-right", "right-to-left", "top-to-bottom" or "bottom-to-top".</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; init; } = "left-to-right";

    [JsonPropertyName("reverse")]
    public bool Reverse { get; init; }

    /// <summary>
    /// Optional explicit zone order. When set it must be a permutation of 0..zoneCount-1,
    /// and it takes precedence over layout, direction and reverse.
    /// </summary>
    [JsonPropertyName("customOrder")]
    public int[]? CustomOrder { get; init; }

    // As with DeviceConfig.Settings, the generated record equality would compare CustomOrder
    // by reference and report two identical mappings as different.
    public bool Equals(MappingConfig? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return ZoneCount == other.ZoneCount
            && string.Equals(Layout, other.Layout, StringComparison.Ordinal)
            && string.Equals(Direction, other.Direction, StringComparison.Ordinal)
            && Reverse == other.Reverse
            && ((CustomOrder is null && other.CustomOrder is null)
                || (CustomOrder is not null && other.CustomOrder is not null
                    && CustomOrder.AsSpan().SequenceEqual(other.CustomOrder)));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ZoneCount);
        hash.Add(Layout, StringComparer.Ordinal);
        hash.Add(Direction, StringComparer.Ordinal);
        hash.Add(Reverse);

        if (CustomOrder is not null)
        {
            hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(CustomOrder.AsSpan()));
        }

        return hash.ToHashCode();
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (ZoneCount is < 1 or > 4096)
        {
            problems.Add($"mapping.zoneCount must be between 1 and 4096, got {ZoneCount}.");
        }

        if (!TryParseLayout(Layout, out _))
        {
            problems.Add($"mapping.layout must be 'vertical' or 'horizontal', got '{Layout}'.");
        }

        if (!TryParseDirection(Direction, out _))
        {
            problems.Add(
                "mapping.direction must be one of 'left-to-right', 'right-to-left', " +
                $"'top-to-bottom', 'bottom-to-top', got '{Direction}'.");
        }

        if (CustomOrder is { Length: > 0 })
        {
            if (CustomOrder.Length != ZoneCount)
            {
                problems.Add(
                    $"mapping.customOrder has {CustomOrder.Length} entries but zoneCount is {ZoneCount}.");
            }
            else if (!IsPermutationOfZoneIndices(CustomOrder))
            {
                problems.Add(
                    $"mapping.customOrder must list every index from 0 to {ZoneCount - 1} exactly once.");
            }
        }

        return problems;
    }

    /// <summary>
    /// Parses the layout, assuming <see cref="Validate"/> has already passed.
    /// </summary>
    public ZoneLayout ParsedLayout => TryParseLayout(Layout, out var layout)
        ? layout
        : throw new InvalidOperationException($"mapping.layout '{Layout}' is not valid.");

    /// <summary>
    /// Parses the direction, assuming <see cref="Validate"/> has already passed.
    /// </summary>
    public ZoneDirection ParsedDirection => TryParseDirection(Direction, out var direction)
        ? direction
        : throw new InvalidOperationException($"mapping.direction '{Direction}' is not valid.");

    public static bool TryParseLayout(string value, out ZoneLayout layout)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "vertical":
                layout = ZoneLayout.Vertical;
                return true;
            case "horizontal":
                layout = ZoneLayout.Horizontal;
                return true;
            default:
                layout = default;
                return false;
        }
    }

    public static bool TryParseDirection(string value, out ZoneDirection direction)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "left-to-right":
                direction = ZoneDirection.LeftToRight;
                return true;
            case "right-to-left":
                direction = ZoneDirection.RightToLeft;
                return true;
            case "top-to-bottom":
                direction = ZoneDirection.TopToBottom;
                return true;
            case "bottom-to-top":
                direction = ZoneDirection.BottomToTop;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    private static bool IsPermutationOfZoneIndices(int[] order)
    {
        var seen = new bool[order.Length];

        foreach (var index in order)
        {
            if (index < 0 || index >= order.Length || seen[index])
            {
                return false;
            }

            seen[index] = true;
        }

        return true;
    }
}
