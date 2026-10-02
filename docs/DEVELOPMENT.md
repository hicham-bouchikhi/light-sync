# Development

## Build, test, publish

```bash
dotnet restore
dotnet build -c Release --no-restore # must produce zero warnings
dotnet test -c Release --no-build    # unit and integration tests
dotnet publish src/LightSync.Cli -c Release -o out    # native AOT binary
```

`global.json` opts into the `Microsoft.Testing.Platform` runner, which .NET 10
requires for xunit.v3. Without it `dotnet test` fails with a VSTest error.

## Layout

```
src/LightSync.Core/            vendor-independent: audio, capture, colour, mapping, pipeline
src/LightSync.Application/     shared adapter factory and profile storage
src/LightSync.Cli/             command line
src/LightSync.Desktop/         Avalonia device studio and audio visualizer
src/LightSync.Devices.*/       one project per lighting vendor
tests/                         xunit.v3
```

`LightSync.Core` must never reference an adapter. That is what keeps the promise
that adding a vendor cannot require touching capture or colour processing. If you
find yourself wanting a reference from Core to an adapter, the abstraction is in
the wrong place.

## Non-negotiables

**Zero warnings.** `AnalysisMode=All` with `TreatWarningsAsErrors`. If a rule
genuinely does not apply, disable it **once in `.editorconfig` with the reason
written down** — not with an inline pragma. Several real bugs surfaced this way,
including a task that could outlive an `IDisposable` it was using.

**Native AOT.** Everything under `src/` sets `IsAotCompatible`, so trim and AOT
analysers run at build time. Practically this means:

- No reflection-based type discovery. The adapter factory is a `switch`.
- All JSON goes through a source-generated `JsonSerializerContext`. Adding a
  serialised type means adding a `[JsonSerializable]` entry.

**No allocation in the frame loop.** Buffers are rented and returned, zone arrays
are pre-sized, pixel work uses `Span<T>`, and there is no LINQ on the hot path.
Heavy pixel reduction is pushed into GStreamer, not done in C#. Audio uses reusable
FFT and spectrum buffers; advance rainbow state once per capture block before
mapping every device. Report callbacks expose reused buffers and must copy them
before returning if retaining data. Desktop previews report every three audio
blocks; CLI diagnostics default to every ten. Set `AudioSyncSession`'s
`reportEveryFrames` to choose the cadence.
Visualizer bar colours sample the reported device frame; never generate a second
palette in the renderer or maintain a separate response mapper for the preview.

**Never log a secret.** Tokens live in memory and in request URIs only.
`NanoleafSettings.ToString()` omits the token deliberately, and a malformed
secrets file is reported as absent rather than echoed. There are tests for this.

## Adding a device adapter

1. New project `src/LightSync.Devices.<Vendor>/` referencing Core only.
2. Implement `ILightDevice`. Report real values from `Capabilities` after
   connecting — `DeviceCapabilityValidator` relies on them to refuse a
   configuration the hardware cannot render.
3. Add a case to `LightSync.Application/DeviceAdapterFactory` and an entry to its descriptor list.
4. Read settings from the flat `IReadOnlyDictionary<string, string>`. Never put a
   secret in it; name an environment variable instead.
5. Add a test project mirroring `LightSync.Devices.Nanoleaf.Tests`.

Capture and colour processing should not need to change. Integrate discovery
through `DeviceDiscoveryService` and add adapter-specific desktop settings and
actions when a backend needs them. Nanoleaf and OpenRGB use this path. See
[Extending to internal PC lighting](ARCHITECTURE.md#extending-to-internal-pc-lighting)
for the proposed discovery, controller identity and LED grouping boundaries.

## Testing conventions

- **Parse recorded real output, not invented samples.** The `hyprctl` and
  `avahi-browse` parsers are tested against output captured from a real machine.
- **Run the real thing where you can.** The GStreamer reader is tested against a
  live `gst-launch-1.0`, including frame boundaries and sustained throughput.
  Those tests skip cleanly when the binary is absent.
- **Assert protocol bytes exactly.** The Nanoleaf frame layout has a
  byte-for-byte test, because it was established by measurement and a silent
  change would be very hard to notice.
- **Tests must not depend on the machine.** The Nanoleaf token path is
  configurable specifically so tests cannot pick up a developer's real token — a
  failing test caught exactly that.

## Things that will waste your time

Each of these cost real debugging. They are written down so they only cost it once.

**`gst-launch-1.0` argv.** Each entry is its own token. An element and its
properties must be separate arguments; one spaced string is a syntax error.

**`videoscale method=nearest` is invalid.** The enum nick is `nearest-neighbour`.

**Caps framerate does not throttle.** A framerate in the caps filter only labels
buffers. Without `videorate drop-only=true max-rate=N` a non-live source runs flat
out — it wrote 6 GB in ten seconds.

**`Stopwatch.ElapsedTicks` are not `TimeSpan` ticks.** They are in
`Stopwatch.Frequency` units. Mixing them made the reported frame rate 100× too low
on Linux. Use `Stopwatch.GetElapsedTime`.

**Record equality and reference types.** A `record` with a `Dictionary` or array
property compares those by reference, so two identical configurations were
unequal. Both affected types override equality explicitly.

**Partial JSON defaults.** Source-generated constructors for init-only members
can receive null or zero for omitted values, replacing their initializers.
Normalize sections and strings explicitly. Audio options and profile brightness
use settable properties so omitted numeric fields keep their defaults while an
explicit zero remains valid. Add a partial-config test whenever extending these
settings.

**Connected UDP sockets die from stale ICMP.** On a connected socket the kernel
reports the peer's ICMP port-unreachable as `ECONNREFUSED` on the *next* send, so
one frame sent before the device opened its port killed the stream after exactly
one frame. Streaming uses an unconnected socket and an explicit endpoint.

**Do not add a second signal handler.** `System.CommandLine` already handles
process termination: it cancels the token it passes to your action and force-exits
if the action does not finish. A private lifetime type meant shutdown was killed
halfway through. Use the token the action is given.

**Background jobs inherit SIGINT as ignored.** A shell starting a job in the
background sets SIGHUP, SIGINT and SIGQUIT to ignored in the child (`SigIgn: 0x7`),
and the runtime correctly leaves an already-ignored signal alone. `stop` therefore
sends SIGTERM.

**Portal quirks** are documented in [ARCHITECTURE.md](ARCHITECTURE.md): the picker
appears during `SelectSources` not `Start`, the signal watch needs a null sender
filter, `Notification.Exception` throws unless `IsCompletion` is set, and the
session must outlive the stream.

## Hardware-specific notes

[NANOLEAF_PROTOCOL.md](NANOLEAF_PROTOCOL.md) records what the device actually
does, including several points where it contradicts the published documentation.
Read it before changing anything in the Nanoleaf adapter.
