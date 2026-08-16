# light-sync

Ambient-light synchronization for Linux/Wayland. Captures a region of your screen, reduces it to colour zones, and streams those colours to lighting hardware in real time.

Built on .NET 10. Vendor-independent by design: adding a lighting brand means writing one adapter, nothing else.

## Status

Early development. The Nanoleaf Matter Wi-Fi adapter is the first target.

## Requirements

- Linux with Wayland and a desktop portal that provides screen capture
  (developed against `xdg-desktop-portal-hyprland`)
- PipeWire
- GStreamer with `pipewiresrc` (`gst-plugins-good` / `gst-plugin-pipewire`)
- .NET 10 SDK

## Quick start

```bash
dotnet restore
dotnet build

# One-time setup: pick your capture region and configure your device
dotnet run --project src/LightSync.Cli -- setup

# Check everything is wired up
dotnet run --project src/LightSync.Cli -- diagnostics

# See zone colours in the terminal without touching your lights
dotnet run --project src/LightSync.Cli -- --dry-run

# Go
dotnet run --project src/LightSync.Cli -- run
```

During `setup`, your desktop portal shows its screen-capture picker. Choose the
**Region** tab and drag a rectangle — light-sync reads the selected rectangle
back from the portal and remembers it. You never have to type screen
dimensions.

## Commands

| Command | Purpose |
|---|---|
| `setup` | Pick the capture region, configure and validate the device |
| `select-area` | Re-pick the capture region |
| `list-displays` | Show detected displays |
| `list-adapters` | Show available device adapters |
| `test-device` | Connect to the configured device and report capabilities |
| `test-color <red\|green\|blue\|white>` | Send a static colour |
| `run` | Start the synchronization pipeline |
| `stop` | Stop a running instance |
| `diagnostics` | Probe the environment and configuration |
| `--dry-run` | Capture and process, print zones and FPS, contact no device |

## Configuration

Stored at `$XDG_CONFIG_HOME/light-sync/config.json` (default
`~/.config/light-sync/config.json`).

```json
{
  "capture":    { "displayId": 0, "x": 3200, "y": 200, "width": 1600, "height": 1000, "fps": 30 },
  "mapping":    { "zoneCount": 24, "layout": "vertical", "direction": "left-to-right", "reverse": false },
  "processing": { "brightness": 1.0, "gamma": 1.0, "saturation": 1.0, "smoothing": 0.2, "blackLevel": 0.01 },
  "device":     { "adapter": "nanoleaf",
                  "settings": { "host": "192.168.1.24", "port": 16021,
                                "tokenEnvironmentVariable": "NANOLEAF_TOKEN" } }
}
```

Device tokens are never stored in this file and never written to logs. Supply
them through the environment:

```bash
export NANOLEAF_TOKEN=...
```

## Architecture

```
portal (region pick) -> PipeWire node -> GStreamer (crop + downscale)
    -> IScreenCapture -> IColorProcessor -> ZoneMapper -> ILightDevice
```

`LightSync.Core` holds the capture, colour, mapping and pipeline code and has
no knowledge of any vendor. Each adapter is a separate project referencing
Core. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Licence

MIT. See [LICENSE](LICENSE).
