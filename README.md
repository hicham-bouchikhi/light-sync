# light-sync

Audio-reactive lighting and exact LED control for Linux. Follow computer playback
through PipeWire / PulseAudio, or optionally capture a Wayland screen region.
An Avalonia desktop studio manages devices, synchronizes them together, and tests
RGB channels and individual LEDs.

Built on .NET 10. The CLI publishes as a native binary; the desktop uses Avalonia.
Vendor-independent by design: adding a lighting brand means writing one adapter.

## Status

Computer-playback sync, the Avalonia interface, and the 0–100% device brightness
control were confirmed working locally on 2026-10-01 with the Nanoleaf Matter
Wi-Fi Floor Lamp. The previous screen pipeline remains available as an explicit
mode. Nanoleaf and OpenRGB adapters are implemented; WLED and Hue remain
placeholders. OpenRGB has protocol tests; physical PC-component output still
needs hardware verification. See the [validation record](docs/DESKTOP.md#validated-setup).

## Requirements

- For optional screen sync: Wayland compositor with a desktop portal providing `ScreenCast`
  (developed against `xdg-desktop-portal-hyprland`)
- PipeWire with its PulseAudio compatibility server (or PulseAudio), plus `parec` and `pactl`
- For optional screen sync: GStreamer with the `pipewiresrc` plugin
- avahi, for device discovery
- For PC component lighting: OpenRGB running with its SDK server enabled
- .NET 10 SDK to build

```bash
sudo pacman -S --needed dotnet-sdk pipewire gst-plugin-pipewire \
                        gst-plugins-base gst-plugins-good avahi libpulse
```

## Quick start

Start the desktop interface from the checkout:

```bash
dotnet run --project src/LightSync.Desktop
```

The interface has two separate sections. **Devices** manages saved profiles,
pairing, removal, master brightness and exact RGB / LED order tests.
**Audio sync & visualizer** contains playback selection, effect tuning, the live
visualizer and the list of devices to synchronize.

In **Devices → Settings & pairing**, save a profile, pair if necessary, and
connect. In **Brightness & LED tests**, send RGB presets, isolate one LED, run the
chase to verify physical order, and set master brightness from 0% (off) to 100%.
Use **Remove device** under a profile to delete it. In the audio section, uncheck
a device under **Sync devices** to exclude it while keeping its profile.

For stronger impact with changing colours, select **Moving rainbow** and tune
gain and colour motion independently while listening. Volume and bass responses
have their own audio colour controls; LED test colours are separate.
[Desktop guide](docs/DESKTOP.md) includes screenshots, controls and verification.

The CLI defaults to audio and does not need screen selection:

```bash
dotnet run --project src/LightSync.Cli -- run
# Select an explicit output monitor if needed:
dotnet run --project src/LightSync.Cli -- run --audio-source alsa_output.example.monitor
```

New Nanoleaf devices still need pairing and a configured host/token. For optional
screen sync, select a region with `setup`, then use `run --source screen`.
[docs/SETUP.md](docs/SETUP.md) covers pairing and screen capture.

For internal PC lighting, start OpenRGB's SDK server, then use **Devices →
Discover PC components**, or `light-sync discover --adapter openrgb` in the CLI.
See the [OpenRGB guide](docs/OPENRGB.md) for component selection and configuration.

## Building a native binary

```bash
dotnet publish src/LightSync.Cli -c Release -o out
./out/light-sync --help
```

A single self-contained file, no .NET runtime needed at run time, near-instant
startup. Because of this everything under `src/` is trim- and AOT-safe: no
reflection-based adapter loading, and all JSON goes through source generators.

## Commands

| Command | Purpose |
|---|---|
| `diagnostics` | Probe the environment, configuration and device. Start here. |
| `discover` | Find Nanoleaf lights; `--adapter openrgb` enumerates PC components |
| `pair` | Obtain an auth token (`--host`, `--save`) |
| `setup` | Choose the capture area and validate the device |
| `select-area` | Choose the capture area again |
| `list-displays` | Show detected displays |
| `list-adapters` | Show available device adapters |
| `test-device` | Connect and report capabilities |
| `test-color <red\|green\|blue\|white>` | Send a static colour |
| `test-stream` | Stream a moving pattern (`--seconds`) |
| `run` | Follow computer playback (default); `--source screen` retains screen sync |
| `stop` | Stop a running instance |
| `--dry-run` | Capture and process, print zones and FPS, contact no device |
| `--ai-help` | Task-oriented guide for an agent or new operator |

`--dry-run` and `test-color` are the fastest way to narrow a problem: one proves
capture and colour without the light, the other proves the light without capture.

## Configuration

`$XDG_CONFIG_HOME/light-sync/config.json`, by default
`~/.config/light-sync/config.json`. Every field is optional — an empty `{}` is
valid and gets sensible defaults.

```json
{
  "audio":      { "source": "@DEFAULT_MONITOR@", "mode": "spectrum", "gain": 3,
                  "brightness": 1, "smoothing": 0.65, "noiseGate": 0.005 },
  "capture":    { "displayId": 0, "x": 0, "y": 0, "width": 1600, "height": 1000, "fps": 30 },
  "mapping":    { "zoneCount": 24, "layout": "vertical", "direction": "left-to-right", "reverse": false },
  "processing": { "brightness": 1.8, "gamma": 0.7, "saturation": 1.4,
                  "smoothing": 0.25, "blackLevel": 0.01, "averaging": "luminance-weighted" },
  "device":     { "adapter": "nanoleaf",
                  "settings": { "host": "192.168.1.24", "port": "16021",
                                "tokenEnvironmentVariable": "NANOLEAF_TOKEN" } }
}
```

Tokens are never stored in this file and never logged. Supply one through the
environment, or let `pair --save` write it to a separate owner-readable-only file:

```bash
export NANOLEAF_TOKEN=...
```

**If audio-driven light looks dim**, set the device brightness to 100% in the
**Devices → Brightness & LED tests**, check audio output brightness, and increase sensitivity if
the live level is low. Nanoleaf streaming restores master brightness after a
blackout so a later session does not inherit the 1% value used to switch off.

**For dim screen sync**, keep `averaging` at `luminance-weighted`, raise
`processing.brightness` above 1, and lower `processing.gamma` below 1. See
[docs/SETUP.md](docs/SETUP.md#tuning).

## Architecture

```
playback monitor → parec → IAudioCapture → AudioAnalyzer → AudioColorMapper
    → AudioSyncSession → one full RGB frame per selected ILightDevice

portal (region pick) → PipeWire node → GStreamer (crop + downscale)
    → IScreenCapture → IColorProcessor → ZoneMapper → ILightDevice
```

`LightSync.Application` shares the device factory between CLI and Avalonia.
`LightSync.Core` holds audio analysis, capture, colour, mapping and pipeline code
and has no reference to any adapter, so vendor independence is structural rather than a
convention. Heavy pixel reduction happens in GStreamer — a 5120×1440 monitor
becomes about 96×8 before any pixels reach managed code — so the frame loop is
cheap and allocation-free.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Documentation

- [docs/DESKTOP.md](docs/DESKTOP.md) — audio sync, device profiles and exact LED tests
- [docs/SETUP.md](docs/SETUP.md) — installation, pairing, first run, tuning
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — design decisions and why
- [docs/NANOLEAF_PROTOCOL.md](docs/NANOLEAF_PROTOCOL.md) — what the hardware
  actually does, including where it contradicts the official documentation
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — building, conventions, and the
  traps that cost real debugging time

## Licence

MIT. See [LICENSE](LICENSE).
