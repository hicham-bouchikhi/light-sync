# Setup

## Prerequisites

Arch / CachyOS:

```bash
sudo pacman -S --needed dotnet-sdk pipewire gst-plugin-pipewire \
                        gst-plugins-base gst-plugins-good avahi
```

You also need a Wayland compositor with a desktop portal that provides
`ScreenCast`. Developed against `xdg-desktop-portal-hyprland`.

Check everything at once:

```bash
light-sync diagnostics
```

That reports each dependency, your configuration, and whether the device is
reachable. Start here whenever something does not work — it names the failing
item instead of leaving you to guess.

## Build

```bash
dotnet restore
dotnet build
```

Or produce a single native binary with no runtime dependency:

```bash
dotnet publish src/LightSync.Cli -c Release -o out
./out/light-sync --help
```

## 1. Find your device

```bash
light-sync discover
```

Prints every Nanoleaf device on the network with its model and firmware, read
from the mDNS TXT record. You do not need to know its IP in advance.

## 2. Get an auth token

This step needs a physical action, and it differs by product line.

```bash
light-sync pair --save
```

Then, **while the command is polling**:

- **Matter / Essentials models** (lightstrips, floor lamps, bulbs — `NL7xKx`):
  open the Nanoleaf app, go to the device's settings and tap **Connect to API**.
  That opens a 30-second window. Holding a button does nothing on this line.
- **Panel products** (Light Panels, Shapes, Canvas): hold the controller's power
  button for about five seconds until the LEDs flash.

`--save` writes the token to `~/.config/light-sync/secrets.local.json` with
owner-only permissions. Without `--save` it prints the token once so you can put
it in your shell profile instead:

```bash
export NANOLEAF_TOKEN=...
```

The token is never written to the main config file and never logged.

## 3. Check the device

```bash
light-sync test-device
```

Reports the model, firmware, and **how many addressable LEDs it has**. Set
`mapping.zoneCount` to that number — an oversized frame is discarded whole by the
device, which looks like the light freezing rather than a partial update.

Confirm it responds:

```bash
light-sync test-color red
light-sync test-stream --seconds 5
```

## 4. Choose the capture area

```bash
light-sync setup
```

Your compositor shows its screen-share picker. Choose the **Region** tab and drag
a rectangle over the area you want the lamp to follow, then confirm.

**Tick "Allow a restore token"** if the picker offers it. Without a token the
picker reappears every single time you run `light-sync run --source screen`.

On Hyprland you can make tokens the default so the dialog stops appearing. In your
`xdg-desktop-portal-hyprland` config:

```ini
screencopy {
    allow_token_by_default = true
}
```

To change the area later:

```bash
light-sync select-area
```

### About region coordinates

A region is delivered as its own cropped stream, and Hyprland reports its
position as `(0,0)` rather than its offset on the desktop. So the saved `x` and
`y` are 0 and the whole stream is used — which is exactly the area you selected.
This is expected, not a fault.

If you pick a whole screen instead, the stream is the full monitor and the saved
`x`, `y`, `width` and `height` are used to crop within it.

## 5. Run

```bash
light-sync run --source screen  # Ctrl+C to stop
light-sync stop         # or stop it from another terminal
```

Check it without touching your lights first, if you prefer:

```bash
light-sync --dry-run
```

That prints the zone colours as a coloured strip plus the frame rate.

## Tuning

Edit `~/.config/light-sync/config.json`. Changes take effect on the next run.

```json
"processing": {
  "brightness": 1.8,
  "gamma": 0.7,
  "saturation": 1.4,
  "smoothing": 0.25,
  "blackLevel": 0.01,
  "averaging": "luminance-weighted"
}
```

**If the light looks washed out or dim** — the usual complaint when a zone covers
a lot of screen:

- `averaging`: keep `luminance-weighted`. It lets bright content dominate instead
  of being diluted by dark background. `mean` is faithful but duller.
- `brightness`: above 1 scales everything up. 1.5–2.0 is a reasonable range.
- `gamma`: **below** 1 lifts mid-tones, which is usually what "make it brighter"
  actually means. Try 0.7.
- `saturation`: above 1 makes colours more vivid. 1.3–1.5 reads well on a lamp.

**If it flickers or feels twitchy**, raise `smoothing` towards 0.4. Higher means
more of the previous frame is kept, so changes ease in.

**If dark scenes leave a faint glow**, raise `blackLevel` a little. Any zone below
that luminance is forced fully off.

**Frame rate** lives in `capture.fps` and defaults to 30, which is ample for
ambient light. Note that Wayland capture is damage-driven: a static screen
produces fewer frames and the reported rate drops accordingly. That is normal.

## Zone layout

```json
"mapping": {
  "zoneCount": 24,
  "layout": "vertical",
  "direction": "left-to-right",
  "reverse": false
}
```

- `layout`: `vertical` splits the area into columns, `horizontal` into rows.
- `direction`: which end of the area feeds the first LED.
- `reverse`: flips the order. Use it if the colours run the wrong way along the
  strip.
- `customOrder`: an explicit permutation, for hardware whose LEDs are not in a
  simple line. Overrides layout, direction and reverse.

Set `zoneCount` to the LED count from `test-device` for a one-to-one mapping.

## Using another device

```bash
light-sync list-adapters
```

`nanoleaf`, `openrgb` and `fake` are implemented; `wled` and `hue` are placeholders.
For internal PC lighting, follow the [OpenRGB guide](OPENRGB.md).
`fake` records frames in memory and is useful for trying the pipeline with no
hardware:

```json
"device": { "adapter": "fake", "settings": {} }
```
