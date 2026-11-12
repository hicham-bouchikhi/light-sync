using LightSync.Core.Colors;
using LightSync.Core.Configuration;

namespace LightSync.Core.Tests;

public class AmbientColorMapperTests
{
    [Fact]
    public void StaticHoldsTheConfiguredColourWithoutAnimating()
    {
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "static", Color = new RgbColor(128, 64, 255), Brightness = 1,
        });
        var frame = new RgbColor[4];

        mapper.Map(frame);
        Assert.All(frame, color => Assert.Equal(new RgbColor(128, 64, 255), color));

        mapper.Advance(30);
        mapper.Map(frame);
        Assert.All(frame, color => Assert.Equal(new RgbColor(128, 64, 255), color));
    }

    [Fact]
    public void StaticScalesEveryChannelByBrightness()
    {
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "static", Color = new RgbColor(128, 64, 255), Brightness = 0.5,
        });
        var frame = new RgbColor[1];

        mapper.Map(frame);

        Assert.Equal(new RgbColor(64, 32, 128), frame[0]);
    }

    [Fact]
    public void BlackStaticActsAsAnOffSwitch()
    {
        var mapper = new AmbientColorMapper(new LightingConfig { Mode = "static", Color = RgbColor.Black });
        var frame = new RgbColor[4];
        frame.AsSpan().Fill(new RgbColor(1, 2, 3));

        mapper.Map(frame);

        Assert.All(frame, color => Assert.Equal(RgbColor.Black, color));
    }

    [Fact]
    public void PulseStartsBlackReachesTheColourAtHalfCycleAndReturnsToBlack()
    {
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "pulse", Color = ColorConstants.White, Brightness = 1, Speed = 1,
        });
        var frame = new RgbColor[2];

        mapper.Map(frame);
        Assert.All(frame, color => Assert.Equal(RgbColor.Black, color));

        mapper.Advance(AmbientColorMapper.PulsePeriodSeconds / 2);
        mapper.Map(frame);
        Assert.All(frame, color => Assert.Equal(ColorConstants.White, color));

        mapper.Advance(AmbientColorMapper.PulsePeriodSeconds / 2);
        mapper.Map(frame);
        Assert.All(frame, color => Assert.Equal(RgbColor.Black, color));
    }

    [Fact]
    public void PulseStaysBetweenBlackAndTheConfiguredColour()
    {
        var color = new RgbColor(200, 120, 40);
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "pulse", Color = color, Brightness = 0.8, Speed = 1,
        });
        var frame = new RgbColor[1];

        for (var step = 0; step < 400; step++)
        {
            mapper.Advance(0.01);
            mapper.Map(frame);
            Assert.InRange(frame[0].R, 0, (int)(color.R * 0.8));
            Assert.InRange(frame[0].G, 0, (int)(color.G * 0.8));
            Assert.InRange(frame[0].B, 0, (int)(color.B * 0.8));
        }
    }

    [Fact]
    public void PulseAtSpeedZeroHoldsTheFullColour()
    {
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "pulse", Color = new RgbColor(10, 20, 30), Brightness = 1, Speed = 0,
        });
        var frame = new RgbColor[1];

        mapper.Advance(100);
        mapper.Map(frame);

        Assert.Equal(new RgbColor(10, 20, 30), frame[0]);
    }

    [Fact]
    public void PulseSpeedScalesTheBreathingPeriod()
    {
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "pulse", Color = ColorConstants.White, Brightness = 1, Speed = 2,
        });
        var frame = new RgbColor[1];

        mapper.Advance(AmbientColorMapper.PulsePeriodSeconds / 4);
        mapper.Map(frame);

        Assert.Equal(ColorConstants.White, frame[0]);
    }

    [Fact]
    public void RainbowCompletesOneHueTurnPerDocumentedPeriod()
    {
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "rainbow", Brightness = 1, Speed = 1,
        });
        var start = new RgbColor[4];
        var later = new RgbColor[4];

        mapper.Map(start);
        mapper.Advance(AmbientColorMapper.RainbowPeriodSeconds);
        mapper.Map(later);

        Assert.Equal(start, later);
    }

    [Fact]
    public void RainbowReversedDirectionMirrorsTheStrip()
    {
        var forward = new AmbientColorMapper(new LightingConfig
        {
            Mode = "rainbow", Brightness = 1, Speed = 1, Direction = "left-to-right",
        });
        var reversed = new AmbientColorMapper(new LightingConfig
        {
            Mode = "rainbow", Brightness = 1, Speed = 1, Direction = "right-to-left",
        });
        var forwardFrame = new RgbColor[5];
        var reversedFrame = new RgbColor[5];

        forward.Map(forwardFrame);
        reversed.Map(reversedFrame);

        // Reversing negates the phase, so zone i shows the hue the forward strip puts at zone
        // n - i (the 1-based index from the end).
        for (var i = 1; i < forwardFrame.Length; i++)
        {
            Assert.Equal(forwardFrame[^i], reversedFrame[i]);
        }
    }

    [Fact]
    public void RainbowAtSpeedZeroIsAFrozenGradient()
    {
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "rainbow", Brightness = 1, Speed = 0,
        });
        var start = new RgbColor[4];
        var later = new RgbColor[4];

        mapper.Map(start);
        mapper.Advance(100);
        mapper.Map(later);

        Assert.Equal(start, later);
    }

    [Fact]
    public void RainbowHandlesASingleZoneAndBrightness()
    {
        var mapper = new AmbientColorMapper(new LightingConfig
        {
            Mode = "rainbow", Brightness = 0.5, Speed = 1,
        });
        var frame = new RgbColor[1];

        mapper.Map(frame);

        Assert.Equal(new RgbColor(128, 0, 0), frame[0]);
    }

    [Fact]
    public void UpdateOptionsAppliesTheNextFrameWithoutRestarting()
    {
        var mapper = new AmbientColorMapper(new LightingConfig { Mode = "static", Color = ColorConstants.Red });
        var frame = new RgbColor[1];

        mapper.UpdateOptions(new LightingConfig { Mode = "static", Color = ColorConstants.Blue });
        mapper.Map(frame);

        Assert.Equal(ColorConstants.Blue, frame[0]);
    }

    [Fact]
    public void InvalidOptionsAreRejected()
    {
        var mapper = new AmbientColorMapper(new LightingConfig());

        Assert.Throws<ArgumentException>(() => mapper.UpdateOptions(new LightingConfig { Mode = "unknown" }));
        Assert.Throws<ArgumentException>(() => mapper.UpdateOptions(new LightingConfig { Speed = 3.5 }));
        Assert.Throws<ArgumentException>(() => mapper.UpdateOptions(new LightingConfig { Brightness = -0.1 }));
    }
}
