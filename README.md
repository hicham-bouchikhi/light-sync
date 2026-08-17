# light-sync

Ambient-light synchronization for Linux/Wayland. Captures a region of your screen,
reduces it to colour zones, and streams those colours to lighting hardware in real
time.

Built on .NET 10, published as a single native binary. Vendor-independent by
design: adding a lighting brand means writing one adapter, nothing else.

## Status

Working end to end on the author's setup — Hyprland on CachyOS driving a Nanoleaf
Matter Wi-Fi Floor Lamp at 30 fps. Nanoleaf is the only implemented adapter so far;
WLED, Hue and OpenRGB are placeholders.

## Requirements

- Wayland compositor with a desktop portal providing `ScreenCast`
  (developed against `xdg-desktop-portal-hyprland`)
- PipeWire
- GStreamer with the `pipewiresrc` plugin
- avahi, for device discovery
- .NET 10 SDK to build

```bash
sudo pacman -S --needed dotnet-sdk pipewire gst-plugin-pipewire \
                        gst-plugins-base gst-plugins-good avahi
```

## Quick start

```bash
dotnet build

light-sync diagnostics        # check the environment first
light-sync discover           # find your device
light-sync pair --save        # get a token (needs a physical action)
light-sync test-device        # confirm it works, and see its LED count
light-sync setup              # choose the capture area
light-sync run                # go
```

Two of those steps need a human and cannot be automated: **pairing** (someone has
to arm it on the device) and **setup** (the compositor shows a picker that must be
answered by hand). [docs/SETUP.md](docs/SETUP.md) walks through both.

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
| `discover` | Find Nanoleaf devices on the local network |
| `pair` | Obtain an auth token (`--host`, `--save`) |
| `setup` | Choose the capture area and validate the device |
| `select-area` | Choose the capture area again |
| `list-displays` | Show detected displays |
| `list-adapters` | Show available device adapters |
| `test-device` | Connect and report capabilities |
| `test-color <red\|green\|blue\|white>` | Send a static colour |
| `test-stream` | Stream a moving pattern (`--seconds`) |
| `run` | Start the synchronization pipeline |
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

**If the light looks dim**, that is the usual first impression when a zone covers a
lot of screen. Keep `averaging` at `luminance-weighted`, raise `brightness` above
1, and lower `gamma` below 1 to lift mid-tones. See
[docs/SETUP.md](docs/SETUP.md#tuning).

## Architecture

```
portal (region pick) → PipeWire node → GStreamer (crop + downscale)
    → IScreenCapture → IColorProcessor → ZoneMapper → ILightDevice
```

`LightSync.Core` holds capture, colour, mapping and pipeline code and has no
reference to any adapter, so vendor independence is structural rather than a
convention. Heavy pixel reduction happens in GStreamer — a 5120×1440 monitor
becomes about 96×8 before any pixels reach managed code — so the frame loop is
cheap and allocation-free.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Documentation

- [docs/SETUP.md](docs/SETUP.md) — installation, pairing, first run, tuning
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — design decisions and why
- [docs/NANOLEAF_PROTOCOL.md](docs/NANOLEAF_PROTOCOL.md) — what the hardware
  actually does, including where it contradicts the official documentation
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) — building, conventions, and the
  traps that cost real debugging time

## Licence

MIT. See [LICENSE](LICENSE).
