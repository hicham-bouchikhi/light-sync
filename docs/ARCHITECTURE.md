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
- `LightSync.Application` references Core and the vendor projects, resolving adapters
  through a `switch` in `DeviceAdapterFactory`. The CLI and Avalonia desktop use it.

Adapter settings are a flat `Dictionary<string, string>` rather than a
polymorphic payload. That keeps Core ignorant of every vendor's configuration
shape, and avoids the runtime type discovery that native AOT forbids.

Anything genuinely shared but vendor-neutral lives in Core even if only one
adapter currently needs it — `HsvColor.FromRgb` is there because several vendor
APIs are HSV-only, not because Nanoleaf is.

## Extending to internal PC lighting

The existing `ILightDevice` boundary also fits RGB lighting on RAM, GPUs,
motherboards and fan controllers. Keep capture, colour processing and sync
sessions independent of where a light is installed. Fan speed, temperature
monitoring and power management would need separate contracts if added later;
they do not belong on a lighting device.

OpenRGB is the first backend for internal lighting. Its SDK server handles
hardware access; LightSync chooses controllers and sends colours. The adapter
uses managed .NET sockets, with bounded, cancellable requests and reusable frame
buffers. There is no native interop dependency. See [OPENRGB.md](OPENRGB.md) for
setup and the distinction between protocol tests and physical hardware validation.
Compatibility and per-LED control must be checked for the actual component
against the [OpenRGB device list](https://openrgb.org/devices.html).

The [OpenRGB SDK](https://github.com/CalcProgrammer1/OpenRGB/blob/master/Documentation/OpenRGBSDK.md)
describes separate controllers, each with identifying metadata, modes, zones
and LEDs. Protocol versions 0–5 address controllers by list index; version 6
introduces unique device IDs that survive changes to that list. Negotiate the
protocol and keep runtime addresses separate from saved profile identity.
The current implementation negotiates versions 1–4, including when a newer
server offers a higher version. It invalidates saved runtime indices when the
server sends a device-list notification and requires reconnecting.
An independent, read-only startup connection can negotiate up to version 6 to
receive detection-complete notifications. It never uses version 6 controller
IDs for lighting; normal discovery and output still negotiate versions 1–4.
Neither a list index nor the application's profile GUID identifies the physical
hardware on reconnect. Resolve a saved controller selector using available
serial/location metadata and report an ambiguous match rather than selecting the
first controller. Do not assume protocol device IDs persist across server restarts.
Name and serial remain exact requirements. Location disambiguates those matches;
a stale location is accepted only for a unique name/serial identity. Location-only
selectors remain exact. See [ADR 0003](adr/0003-openrgb-controller-identity.md).

The implemented extension boundaries are:

- **Discovery, separate from output.** `DeviceDiscoveryService` returns an
  adapter ID, display name, non-secret connection settings and streaming support
  for each controller. Both Nanoleaf discovery and OpenRGB enumeration use it.
  Enumeration should not start streaming or change colours.
- **One controller per lighting profile.** A RAM module or GPU uses existing
  sync membership and LED tests independently. Each profile currently owns its
  own SDK connection with serialized writes and coordinated response handling.
  Disconnecting one profile therefore cannot close another's connection. Shared
  transport can be introduced later if connection counts justify it.
- **Adapter-specific configuration and capabilities.** Keep protocol settings in
  adapters and the existing flat settings dictionary. The desktop shows endpoint
  and controller selectors for OpenRGB, and pairing, token and panel mapping
  fields for Nanoleaf. OpenRGB requires a per-LED Direct mode and reports no
  master brightness or effects support. `IBrightnessControl` remains optional.

Frames remain flat `RgbColor` buffers. `OpenRgbController` retains named hardware
groups, while `IZoneAddressProvider` exposes actual frame indices rather than
vendor-internal LED values. Core's current “zone” is an output colour slot; an
OpenRGB zone can contain many LEDs. If the component UI needs these group names,
add an optional topology provider at that point. Reconnect rebuilds frame buffers
from freshly discovered metadata.

There are also two remaining consumer limitations:
the desktop currently refuses connections without streaming support, which
excludes static-only lighting even though `ILightDevice` supports static colour;
and audio output sends to devices sequentially, so a slow controller delays all
others. Screen sync already has independent latest-frame output stages. Use that
approach for audio if mixed-device latency requires it.

Tests cover exact protocol bytes, multi-controller routing, ambiguous selectors,
reconnects, malformed responses, cancellation and device-list changes using an
in-process SDK server, plus a recorded controller-data fixture. Capture and colour
processing did not need any changes to support the new adapter.

### OpenRGB application lifecycle

`OpenRgbServerLauncher` lives in Application because launching a local application
is a desktop workflow policy, rather than a lighting operation. Desktop discovery
and profile connection call it before using the existing discovery service or
adapter. CLI discovery remains read-only and does not launch applications.

The launcher first checks the SDK handshake at the configured endpoint. If unavailable on a loopback
address, it checks for an existing OpenRGB process. A missing application is
opened with its GUI, SDK server, configured host and port, and hardware detection
enabled. Startup calls are serialized to prevent multiple launches in one
LightSync process. An already open application is reused; if its SDK server is
disabled, the user enables it there instead of starting another hardware owner.
Remote endpoints receive an actionable error without a local launch.

TCP readiness is separate from hardware readiness: a new OpenRGB server can
accept connections while exposing an empty or partial controller list. A new
launch waits for SDK 6 detection completion when available. Older versions use
a nonempty controller list unchanged for one second, a compatibility heuristic
bounded by the same 30-second startup timeout. Existing reachable servers are
reused immediately, including a genuinely empty server. Cancellation stops
waiting; disposing the launcher closes its process handle without terminating
OpenRGB. SDK connections still belong to individual lighting profiles.

On Unix, a fixed `sh` wrapper replaces itself with OpenRGB and routes both output
streams directly to a launch log. Variable arguments and the log path are passed
as data rather than interpolated into script text. Regular file handles let
OpenRGB keep logging without a parent pipe reader after LightSync closes.
See [ADR 0002](adr/0002-openrgb-process-output.md) for diagnostic ownership.

See [ADR 0001](adr/0001-openrgb-integration-and-startup.md) for the decision,
alternatives, limits and validation evidence.

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

The flow is `CreateSession` → `SelectSources` → `Start`, and the `Start` response
carries the PipeWire node id plus the stream's `position` and `size`.

What measurement against the live portal changed about that picture:

- **The picker appears during `SelectSources`, not `Start`**, on
  `xdg-desktop-portal-hyprland` — the opposite of what the generic portal
  documentation implies. That call therefore gets a user-scale timeout and `Start`
  returns almost immediately.
- **A Region arrives as a `virtual` source already cropped to the region**, so the
  crop stage is skipped entirely for it. Cropping only does real work when a whole
  screen was picked, and even then GStreamer does it.
- **A region's desktop offset is not recoverable.** Hyprland reports
  `position = (0,0)` for a region even though its own logs show the real offset. So
  the saved `x` and `y` are 0 and the whole stream is used. `setup` says this
  plainly rather than saving invented coordinates.
- **`persist_mode` requests a restore token but does not guarantee one.** Hyprland
  only issues one if the user ticks the picker's checkbox, so the code treats an
  absent token as normal and `diagnostics` reports whether one is stored.
- Displays are enumerated from `hyprctl monitors -j`, since the portal exposes no
  monitor list before a source has been chosen.
- The session must outlive the stream: closing it destroys the PipeWire node, which
  then reports "Device or resource busy". Node ids are also reused across sessions,
  so they are never cached as an identity.

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

## Combining pixels within a zone

A plain arithmetic mean is faithful and wrong in practice. A zone covering a large,
mostly dark area averages to a dim mid-tone regardless of the bright content that
actually characterises it — the first real run looked washed out for exactly this
reason.

So `processing.averaging` defaults to `luminance-weighted`,
which weights each pixel by its luminance. A single bright red pixel among fifteen
black ones yields 255 rather than 15. `mean` remains available for faithful
reproduction.

`colour-weighted` weights each pixel by the difference between its largest and
smallest RGB channels. This keeps white browser content from overwhelming a
coloured accent. Entirely neutral zones fall back to luminance weighting so white
and grey content still produces light. Saturation can then strengthen the sampled
colour without changing the captured image.

This is the "average or weighted-average" the brief asked for, and it matters more
than the brightness and gamma knobs do.

## Testing approach

The tests that matter most are the ones that mattered most were the ones that ran the real thing:

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
- **Property initializers do not run during deserialization.** Every omitted config
  section arrived as `null` and every omitted string as `null`, so a hand-written
  partial config threw `NullReferenceException` and omitted strings were reported as
  invalid values. Defaults are now applied by an explicit `Normalized()` on each
  section rather than trusting the serializer.
- **Wayland capture is damage-driven.** A static screen produces fewer frames, so a
  falling frame rate is usually correct rather than a fault.

## Audio and desktop studio

`IAudioCapture` provides reusable interleaved float samples. `PulseAudioCapture`
starts `parec` against a playback monitor (default `@DEFAULT_MONITOR@`), at 48 kHz,
stereo, in 1024-sample blocks. Silence still produces samples. Capture reads time
out if the sound server stalls, and cancellation closes the owned helper process.

`AudioAnalyzer` removes DC and applies a Hann-windowed radix-2 FFT. It combines
channel powers rather than adding stereo waveforms, avoiding cancellation of
opposite-phase content. RMS level and bass/mid/treble energies feed
`AudioColorMapper`, which supports a frequency gradient or a fixed colour whose
brightness follows volume or bass energy. Moving rainbow advances a shared phase
once per capture block, using raw energy and bass attacks independently of gain.
Its intensity uses a linear response up to half intensity and a soft shoulder
above it; colours continue travelling at high gain. Live options are validated
and copied into an atomic snapshot. Buffers are allocated once per session.
The GUI copies 32 logarithmic spectrum bins and the first device's requested RGB
from report callbacks for its visualizer. Frequency bar heights represent
amplified input energy; their colours sample the same LED frame as the output
strip, so selecting another response cannot leave a fixed rainbow behind.
Desktop reports arrive every three blocks; CLI diagnostics default to ten.
Profile removal or sync membership changes stop capture, black out the old
targets and restart with remaining selected devices.

`AudioSyncSession` analyzes each block once and creates a full frame for each
connected device, including devices with different LED counts. Any failure ends
the session and attempts to black out every participating device. Callers own
capture and devices. Audio sync does not require a screen region or desktop portal.

The desktop separates audio controls and visualization from device management
with two static top-level tabs. Existing control instances and the running audio
session survive navigation. The audio tab owns sync membership and its own RGB
inputs (`audio.color`); LED test RGB inputs are independent. Device settings and
LED tests live in nested device tabs. Both lists refresh from the same profiles
when selection, connections or removal change.

The desktop keeps device profiles in `devices.json` and imports the CLI's legacy
single device when that file is absent. Both use source-generated JSON. Pairing
stores separate secrets per profile. Manual tests use `DeviceTestFrame` to preserve
raw RGB values without gamma, smoothing, screen mapping or HSV conversion. Tests
refresh streaming frames at 10 fps and the chase isolates one LED every 700 ms.
Nanoleaf exposes frame-index-to-address metadata and flags inferred layouts so
users can verify the actual strip rather than mistake a fallback for discovery.

`IBrightnessControl` exposes master brightness in [0, 100]. Nanoleaf applies it as
a partial state update without changing hue/saturation. Zero is represented with
power off and a numeric brightness floor of 1 for Essentials. Streaming restores
the chosen master level before entering external control; the shutdown black-out
therefore cannot strand the next stream at 1%. Desktop profiles persist this level
per device, independently of audio frame brightness.

## Screen playback intensity

The desktop `screen` configuration starts at 75% steady brightness with
**Audio intensity** enabled. It is separate from the audio colour mapper's gain,
colour and brightness settings. `ScreenBrightness` copies validated options and
computes RMS power across interleaved samples, preserving opposite-phase stereo
energy. Its dBFS envelope maps −45 to −6 dBFS to the available brightness headroom,
with 80 ms attack and 350 ms release. Silence returns to the steady level.

`ScreenBrightnessProcessor` wraps screen colour processing and applies a uniform
RGB scale after colour smoothing. Colour processing uses unit brightness to avoid
double scaling. Each frame sequence snapshots one common intensity for every
device, regardless of its zone count; the preview snapshots those same processed
colours. Frame processing allocates no extra buffers or queued audio blocks.

`ScreenAudioMonitor` owns one optional `IAudioCapture` and serializes enable,
disable and disposal transitions. Disabling cancels blocked reads, joins capture
and releases its helper process. Audio failure resets the envelope and reports
an error without stopping the screen pipeline. Desktop capture uses the existing
PulseAudio monitor implementation; Core remains independent of device vendors.

Live intensity controls stay outside the locked capture settings. Device master
brightness is set to 100% during screen output and restored from profile settings
before the final blackout, because master commands can also power lamps on.
CLI screen output retains its existing processing policy.
See [ADR 0004](adr/0004-screen-audio-intensity.md).
