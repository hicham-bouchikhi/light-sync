namespace LightSync.Core.Mapping;

public enum ZoneLayout
{
    /// <summary>Zones are columns; the capture area is split along X.</summary>
    Vertical,

    /// <summary>Zones are rows; the capture area is split along Y.</summary>
    Horizontal,
}

public enum ZoneDirection
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop,
}
