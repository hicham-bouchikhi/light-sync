namespace LightSync.Core.Mapping;

/// <summary>
/// Decides which slice of the capture rectangle feeds each output index, and in what order
/// those outputs are emitted. Computed once at construction so the frame loop only indexes
/// a precomputed array.
/// </summary>
public sealed class ZoneMapper
{
    private readonly int[] outputToSlice;

    public ZoneMapper(
        int zoneCount,
        ZoneLayout layout,
        ZoneDirection direction,
        bool reverse,
        IReadOnlyList<int>? customOrder = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoneCount);

        ZoneCount = zoneCount;
        Layout = layout;
        Direction = direction;

        if (customOrder is { Count: > 0 })
        {
            if (customOrder.Count != zoneCount)
            {
                throw new ArgumentException(
                    $"Custom order has {customOrder.Count} entries but zoneCount is {zoneCount}.",
                    nameof(customOrder));
            }

            outputToSlice = [.. customOrder];

            if (!IsPermutation(outputToSlice))
            {
                throw new ArgumentException(
                    $"Custom order must list every index from 0 to {zoneCount - 1} exactly once.",
                    nameof(customOrder));
            }

            return;
        }

        outputToSlice = BuildOrder(zoneCount, layout, direction, reverse);
    }

    public int ZoneCount { get; }

    public ZoneLayout Layout { get; }

    public ZoneDirection Direction { get; }

    /// <summary>
    /// The slice index feeding output <paramref name="outputIndex"/>. Slice 0 is always the
    /// leftmost column for a vertical layout, or the topmost row for a horizontal one,
    /// regardless of direction.
    /// </summary>
    public int SliceForOutput(int outputIndex) => outputToSlice[outputIndex];

    public ReadOnlySpan<int> Order => outputToSlice;

    private static int[] BuildOrder(int zoneCount, ZoneLayout layout, ZoneDirection direction, bool reverse)
    {
        // A direction that runs against the layout's axis carries no ordering information —
        // "top-to-bottom" cannot order columns — so those combinations fall back to the
        // natural order and let `reverse` do the work.
        var descending = layout switch
        {
            ZoneLayout.Vertical => direction == ZoneDirection.RightToLeft,
            ZoneLayout.Horizontal => direction == ZoneDirection.BottomToTop,
            _ => false,
        };

        if (reverse)
        {
            descending = !descending;
        }

        var order = new int[zoneCount];
        for (var i = 0; i < zoneCount; i++)
        {
            order[i] = descending ? zoneCount - 1 - i : i;
        }

        return order;
    }

    private static bool IsPermutation(int[] order)
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
