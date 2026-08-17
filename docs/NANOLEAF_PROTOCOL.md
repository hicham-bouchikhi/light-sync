# Nanoleaf local API notes

Everything below was measured against a real device, because the published
documentation for the Matter Essentials line disagrees with how the hardware
actually behaves.

Test device:

| | |
|---|---|
| Product | Nanoleaf Matter Wi-Fi Floor Lamp |
| Model | `NL72K4` |
| Firmware | `4.0.11` (also seen advertising `3.0.31` before an update) |
| Hardware | `1.1.0` |
| mDNS service | `_nanoleafapi._tcp` on port 16021 |

## Discovery

Devices advertise `_nanoleafapi._tcp`. The TXT record already carries the model,
so a device can be identified before authenticating:

```
=;enp7s0;IPv4;Nanoleaf\032IML\032294-3340;_nanoleafapi._tcp;local;\
  808AF726FC0F.local;192.168.1.24;16021;"id=3340" "eui64=0000808AF726FC0F" \
  "md=NL72K4" "srcvers=4.0.11"
```

`md` is the model and `srcvers` the firmware version. light-sync shells out to
`avahi-browse -tpr _nanoleafapi._tcp` rather than binding port 5353 itself,
which the running avahi daemon already owns.

## Authentication

```
POST http://<host>:16021/api/v1/new
```

- Returns `{"auth_token": "..."}` on success.
- Returns **403** when the device is not in pairing mode. 403 means "the window
  is closed", not "unsupported" — the endpoint is there.
- The pairing window must be opened first. On the **panel** products this is a
  physical hold of the power button. On the **Matter Essentials** line the
  documented route is the Nanoleaf app's *Connect to API* button, which opens a
  30-second window. The inline controller's buttons are not documented as an
  API-pairing gesture.

All later requests put the token in the path: `/api/v1/<token>/<resource>`.

light-sync never logs the token: it appears only in request URIs, `ToString()` on
the settings type omits it, and a malformed token file is reported as absent
rather than echoed.

## What this model supports

`GET /api/v1/<token>/` returns:

```json
{
  "name": "Nanoleaf IML 294",
  "serialNo": "V25310ND002Y2",
  "manufacturer": "Nanoleaf",
  "firmwareVersion": "4.0.11",
  "hardwareVersion": "1.1.0",
  "model": "NL72K4",
  "state": {
    "on":         { "value": true },
    "brightness": { "value": 100, "max": 100, "min": 1 },
    "hue":        { "value": 0,   "max": 360, "min": 0 },
    "sat":        { "value": 100, "max": 100, "min": 0 },
    "ct":         { "value": 2702, "max": 6535, "min": 2127 },
    "colorMode":  "effect"
  }
}
```

Note there is **no `panelLayout` and no `effects` key** in the root response for
this model, unlike the panel products.

Endpoint behaviour measured on `NL72K4`:

| Endpoint | Result |
|---|---|
| `GET /` | 200 |
| `GET /panelLayout/layout` | **500** |
| `GET /panelLayout/globalOrientation` | **500** |
| `GET /effects` | **500** |
| `GET /effects/effectsList` | 200 |
| `PUT /effects` (`extControl`, `v2`) | **204** |
| `PUT /effects` (`extControl`, no version) | **400** |

So panel ids cannot be read back on this line. Since `panelLayout` is
unavailable, light-sync falls back to addressing LEDs as a sequential run and
lets the count be overridden in configuration.

### Setting a colour

There is **no RGB route**. Only hue/saturation/brightness:

```
PUT /api/v1/<token>/state
{"on":{"value":true},"hue":{"value":120},"sat":{"value":100},"brightness":{"value":80}}
```

Brightness has `min: 1` on this model and **0 is rejected with HTTP 400**. "Off"
must be expressed through the `on` flag, not a zero brightness.

## Streaming (extControl)

The Matter Essentials API documentation does not mention `extControl` at all.
**It nevertheless works on this device.**

Enable it with:

```
PUT /api/v1/<token>/effects
{"write":{"command":"display","animType":"extControl","extControlVersion":"v2"}}
```

- Responds **204 No Content** — so, unlike the panel products, it returns *no*
  `streamControlIpAddr` / `streamControlPort` body. Fall back to the device host
  and UDP port **60222**.
- After this, `GET /effects/select` returns `"*ExtControl*"`, confirming the mode.
- v1 (`animType` without `extControlVersion`) is rejected with 400. v2 only.

### v2 frame format

UDP to port 60222. Big-endian throughout.

```
offset  size  field
0       2     panel count
        then, per panel:
+0      2     panel id
+2      1     red
+3      1     green
+4      1     blue
+5      1     white          (always 0 here; the strip has no white channel)
+6      2     transition time
```

Frame size is `2 + panelCount * 8`. For the 24 LEDs on this lamp that is
**194 bytes**.

Example, one panel, id `0x0176`, red, transition `0x0032`:

```
00 01  01 76  FF 00 00  00  00 32
```

### The important gotcha

**The device validates the whole frame.** If a frame mentions a single
out-of-range panel id, the entire frame is discarded and the previous frame stays
on the strip. An oversized frame is therefore not a partial update — it is a
silent freeze, which looks exactly like the stream having stopped.

This was how the LED count was determined. Sending solid colours with an
increasing panel count and observing which colour stuck:

| Panel count | Accepted |
|---|---|
| 16 | yes (lit the lower ⅔ of the strip) |
| 20 | yes |
| 24 | **yes** |
| 25, 26, 27 | no |
| 28, 32, 64 | no |

So the strip has **exactly 24 addressable LEDs, ids 0–23**. Consistent with the
visual check: 16 ids lit two thirds of the strip, and 16/24 = 67%.

light-sync therefore refuses to send a frame with fewer colours than the device
has LEDs, rather than emitting something the device will drop on the floor.

### Sockets

Use an **unconnected** UDP socket and send to an explicit endpoint.

On a connected UDP socket the kernel reports the peer's ICMP port-unreachable as
`ECONNREFUSED` on the *next* send. A single frame sent in the window before the
device has opened port 60222 therefore poisons the socket permanently, and the
stream dies after exactly one frame. Sending to an explicit endpoint keeps the
stream properly fire-and-forget.

### Measured throughput

492 frames in 8 seconds — about **61 fps** — with no dropped frames, via the
application's own streaming path. light-sync defaults to 30 fps, which is ample
for ambient light and lighter on the device.

## Model support

Nanoleaf's Essentials API documentation lists these models, so the notes above
should broadly apply across the line (only `NL72K4` was tested):

| Product | Model |
|---|---|
| Essentials Indoor HD Lightstrip | `NL72K1` |
| Essentials Indoor Lightstrip | `NL72K3` |
| Floor Lamp | `NL72K4` |
| Rope Lights | `NL72K6` |
| Essentials Wi-Fi A19 bulb | `NL75K1` |
| Holiday String Lights | `NL71K1`, `NL71K2` |
| Outdoor / Permanent Outdoor | `NL73K1`, `NL73K3` |

The panel products (Light Panels, Shapes, Canvas) use a different document and do
expose `panelLayout`, so on those the real panel ids should be read from the
device instead of assumed sequential. light-sync already prefers the layout when
the endpoint answers.
