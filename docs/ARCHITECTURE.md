# Architecture

## The shape of it

```
XDG portal (Region pick)
        │  PipeWire node id
        ▼
GStreamer helper process ──── crop + downscale happen here
        │  raw BGRx frames on stdout
        ▼
IScreenCapture   ──►  CapturedFrame
        │
        ▼
IColorProcessor  ──►  one RgbColor per zone
        │            (average, gamma, saturation, brightness, black level, smoothing)
        ▼
ZoneMapper       ──►  which output index gets which slice
        │
        ▼
ILightDevice     ──►  Nanoleaf │ WLED │ Hue │ OpenRGB │ Fake
```

## Vendor independence

The requirement was that adding a lighting brand must not touch screen capture or
colour processing. That is enforced structurally rather than by convention:

- `LightSync.Core` has **no project reference to any adapter**. It cannot know
  about a vendor even by accident.
- Each adapter is its own project referencing Core.
- The CLI is the only place that knows both, and it resolves adapters through a
  `switch` in `DeviceAdapterFactory`.

Adapter settings are a flat `Dictionary<string, string>` rather than a
polymorphic payload. That keeps Core ignorant of every vendor's configuration
shape, and avoids the runtime type discovery that native AOT forbids.

Anything genuinely shared but vendor-neutral lives in Core even if only one
adapter currently needs it — `HsvColor.FromRgb` is there because several vendor
APIs are HSV-only, not because Nanoleaf is.

## Why a GStreamer child process

There is no managed PipeWire binding for .NET. The alternatives were P/Invoking
`libpipewire` and implementing SPA format negotiation by hand, or driving
`gst-launch-1.0` with `pipewiresrc`, which already does that work.

The child process turned out to be the better engineering choice for a second
reason: `videocrop` and `videoscale` reduce a 5120×1440 monitor to roughly 96×8
**before any pixels cross into managed memory**. That is about 3 KB per frame
instead of 29 MB. Averaging 24 zones then costs almost nothing, and the frame
loop never touches a large buffer.

Three things about that pipeline are easy to get wrong and were only found by
running it:

- `gst-launch-1.0` parses each argv entry as its own token. An element and its
  properties must be separate arguments; one spaced string is a syntax error.
- `videoscale method=nearest` is invalid. The enum nick is `nearest-neighbour`.
- A framerate in the caps filter only *labels* buffers. Without
  `videorate drop-only=true max-rate=N` a non-live source runs flat out — it wrote
  6 GB in ten seconds during testing.

## Why area selection has no custom UI

The original design called for a drag-select overlay. It turned out
`xdg-desktop-portal-hyprland` already offers a **Region** tab in its source
picker, which does exactly that and returns the chosen rectangle. Writing an
overlay would have duplicated a compositor feature and introduced a UI toolkit
dependency for nothing.

So the flow is: `CreateSession` → `SelectSources` (with `persist_mode` so a
restore token is issued) → `Start`, which shows the picker. The response carries
the PipeWire node id plus the stream's `position` and `size`.

Consequences:

- When the user picks a Region, the stream *is* the rectangle, so the crop stage
  is a no-op. It does real work only when a whole screen was selected.
- The rectangle is still validated against detected display bounds, because a
  reported rectangle that does not fit any display means something is wrong.
- Displays are enumerated from `hyprctl monitors -j`, since the portal exposes no
  monitor list before a source has been chosen.

## Pipeline and back pressure

Capture-and-process and device output run as two concurrent stages joined by a
one-deep `LatestFrameSlot`. Publishing over an unconsumed frame counts as a drop
and recycles its buffer.

This is deliberately not a queue. For ambient light a stale frame is worthless —
better to drop it and show the current one. A queue would convert a slow device
into growing latency; a one-deep slot converts it into a dropped-frame count you
can see in the metrics.

Processing rides with capture rather than getting its own stage. After the
GStreamer downscale it costs so little that a third handoff would cost more than
it saves.

The frame loop allocates nothing in steady state: buffers are rented from the
slot and returned after sending, zone arrays are pre-sized, all pixel work is
`Span<T>`, and there is no LINQ.

## Native AOT

The CLI publishes as a single native binary. That is not cosmetic — it forces
useful constraints:

- No reflection-based adapter loading; the factory is a `switch`.
- All JSON goes through source generators (`ConfigJsonContext`,
  `NanoleafJsonContext`, `HyprctlJsonContext`).
- Everything under `src/` sets `IsAotCompatible`, so the trim and AOT analysers
  run at build time rather than surprising us at publish time.

## Zero warnings

`AnalysisMode=All` plus `TreatWarningsAsErrors`. Where a rule genuinely does not
apply it is disabled **once, in `.editorconfig`, with the reason written down** —
never with scattered inline pragmas. If you find yourself wanting a pragma, the
rule is probably telling you something true.

Several real bugs were caught this way rather than by testing, including a task
that could outlive the `IDisposable` it was using.

## Testing approach

214 tests, and the ones that mattered most were the ones that ran the real thing:

- Colour processing and zone mapping are pure logic and tested exhaustively.
- The GStreamer reader is tested against a live `gst-launch-1.0`, including
  sustained throughput and exact frame boundaries.
- Protocol encoding is asserted byte-for-byte against layouts verified on
  hardware.
- Parsers are tested against **recorded real output** (`hyprctl monitors -j`,
  `avahi-browse`), not invented samples.

Tests must not depend on the machine they run on. The Nanoleaf token path is
configurable specifically so tests cannot pick up a developer's real token — a
test failure caught exactly that.

## Bugs worth remembering

Recorded because each was invisible on inspection and obvious once observed:

- **Smoothing could never reach black.** Rounding left a channel one step short
  forever, so a dark screen kept the lamp faintly lit at `#010101`.
- **`Stopwatch.ElapsedTicks` are not `TimeSpan` ticks.** On Linux the reported
  frame rate was 100× too low — 0.3 fps instead of 30.
- **Record equality compared dictionaries by reference**, so two identical
  configurations were unequal.
- **A connected UDP socket dies from stale ICMP.** See
  [NANOLEAF_PROTOCOL.md](NANOLEAF_PROTOCOL.md).
- **Two competing signal handlers.** System.CommandLine already handles process
  termination and force-exits if the action ignores its token. A private
  lifetime type meant shutdown was killed halfway through.
- **Background jobs inherit SIGINT as ignored** (`SigIgn: 0x7`), so `stop` has to
  send SIGTERM.
