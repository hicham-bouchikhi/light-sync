using LightSync.Core.Devices;
using LightSync.Core.Processing;

namespace LightSync.Core.Pipeline;

/// <summary>One connected device and the processor that samples its own screen zones.</summary>
public sealed record ScreenSyncOutput(IColorProcessor Processor, ILightDevice Device);
