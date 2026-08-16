using LightSync.Core.Mapping;

namespace LightSync.Core.Tests;

public class ZoneMapperTests
{
    [Fact]
    public void DefaultVerticalLeftToRightIsTheNaturalOrder()
    {
        var mapper = new ZoneMapper(4, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false);

        Assert.Equal([0, 1, 2, 3], mapper.Order.ToArray());
    }

    [Fact]
    public void VerticalRightToLeftReversesTheColumns()
    {
        var mapper = new ZoneMapper(4, ZoneLayout.Vertical, ZoneDirection.RightToLeft, reverse: false);

        Assert.Equal([3, 2, 1, 0], mapper.Order.ToArray());
    }

    [Fact]
    public void HorizontalTopToBottomIsTheNaturalOrder()
    {
        var mapper = new ZoneMapper(3, ZoneLayout.Horizontal, ZoneDirection.TopToBottom, reverse: false);

        Assert.Equal([0, 1, 2], mapper.Order.ToArray());
    }

    [Fact]
    public void HorizontalBottomToTopReversesTheRows()
    {
        var mapper = new ZoneMapper(3, ZoneLayout.Horizontal, ZoneDirection.BottomToTop, reverse: false);

        Assert.Equal([2, 1, 0], mapper.Order.ToArray());
    }

    [Fact]
    public void ReverseFlipsTheNaturalOrder()
    {
        var mapper = new ZoneMapper(4, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: true);

        Assert.Equal([3, 2, 1, 0], mapper.Order.ToArray());
    }

    [Fact]
    public void ReverseCancelsOutADescendingDirection()
    {
        var mapper = new ZoneMapper(4, ZoneLayout.Vertical, ZoneDirection.RightToLeft, reverse: true);

        Assert.Equal([0, 1, 2, 3], mapper.Order.ToArray());
    }

    [Fact]
    public void ADirectionAcrossTheLayoutAxisCannotOrderTheSlices()
    {
        // "top-to-bottom" says nothing about how to order columns, so the natural order stands.
        var mapper = new ZoneMapper(4, ZoneLayout.Vertical, ZoneDirection.TopToBottom, reverse: false);

        Assert.Equal([0, 1, 2, 3], mapper.Order.ToArray());
    }

    [Fact]
    public void CustomOrderIsUsedVerbatim()
    {
        var mapper = new ZoneMapper(
            4, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false, customOrder: [2, 0, 3, 1]);

        Assert.Equal([2, 0, 3, 1], mapper.Order.ToArray());
    }

    [Fact]
    public void CustomOrderOverridesDirectionAndReverse()
    {
        var mapper = new ZoneMapper(
            3, ZoneLayout.Vertical, ZoneDirection.RightToLeft, reverse: true, customOrder: [0, 1, 2]);

        Assert.Equal([0, 1, 2], mapper.Order.ToArray());
    }

    [Fact]
    public void SliceForOutputMatchesTheOrder()
    {
        var mapper = new ZoneMapper(
            3, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false, customOrder: [2, 0, 1]);

        Assert.Equal(2, mapper.SliceForOutput(0));
        Assert.Equal(0, mapper.SliceForOutput(1));
        Assert.Equal(1, mapper.SliceForOutput(2));
    }

    [Fact]
    public void RejectsACustomOrderOfTheWrongLength()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ZoneMapper(
            4, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false, customOrder: [0, 1]));

        Assert.Contains("zoneCount is 4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsACustomOrderWithDuplicates()
    {
        Assert.Throws<ArgumentException>(() => new ZoneMapper(
            3, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false, customOrder: [0, 0, 1]));
    }

    [Fact]
    public void RejectsACustomOrderWithAnOutOfRangeIndex()
    {
        Assert.Throws<ArgumentException>(() => new ZoneMapper(
            3, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false, customOrder: [0, 1, 7]));
    }

    [Fact]
    public void RejectsANonPositiveZoneCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ZoneMapper(0, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false));
    }

    [Fact]
    public void EveryOrderIsAPermutationForTheDefaultTwentyFourZones()
    {
        foreach (var layout in Enum.GetValues<ZoneLayout>())
        {
            foreach (var direction in Enum.GetValues<ZoneDirection>())
            {
                foreach (var reverse in (bool[])[false, true])
                {
                    var mapper = new ZoneMapper(24, layout, direction, reverse);

                    Assert.Equal(
                        Enumerable.Range(0, 24),
                        mapper.Order.ToArray().Order());
                }
            }
        }
    }
}
