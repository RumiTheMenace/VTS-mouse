# 🖱️ VTS Mouse — Mouse-Driven VTube Studio Controller

> **No face tracking hardware required.**
> Drive your VTube Studio model's eyes, head, and body with nothing but your mouse —
> then let it come alive on its own when you stop.

A lightweight Windows tray app that connects to VTube Studio via the Public API and
injects eye/head/body tracking parameters in real time. Built for VTubers who want
expressive, reactive models without a webcam or face tracker.

---

## ✨ What it does

| | |
|---|---|
| 🎯 **Mouse tracking** | Eye, head, and body follow your cursor with configurable smoothing and range |
| 😴 **Autonomous gaze** | When you're idle, the model keeps looking around naturally using spring-physics saccades and a gaze history that remembers familiar screen regions |
| 💤 **Sleep / Wake** | Goes to sleep after configurable idle time, wakes up with a jolt animation when you move again |
| 🌀 **Dizzy** | Flick your mouse fast enough and the model spins out — or trigger it remotely |
| 📺 **DVD Bounce** | Model bounces around the screen while idle (optional, configurable) |
| 🎮 **Game mode** | Delta mode uses raw mouse input for smooth tracking even when the cursor is hidden in games |
| 🔌 **Remote expressions** | TCP server lets any app (chatbot, LLM, stream overlay) trigger expressions and animations over the network |
| 👁️ **Blink / Smile / Wink** | Automatic randomized blinks, hover-triggered smiles, and dizzy-triggered winks |
| ⚡ **Ultra low overhead** | ~0.1–0.2% CPU, ~35 MB RAM on a modern machine |

---

## 🚀 Quick Start

### Using the prebuilt release
1. Download `vts-mouse.exe` and `config.json` from the [latest release](../../releases/latest)
2. Drop both into a dedicated folder
3. Enable the VTube Studio Public API: **Settings → General → Start API (Port 8001)**
4. Run `vts-mouse.exe` — accept the VTS permission prompt on first run
5. The app sits in your system tray. Right-click for options.

No .NET install required — the runtime is bundled in the exe.

### Building from source
Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Windows.
```
dotnet publish RumiVtsController/RumiVtsController.sln -c Release -r win-x64 --self-contained true
```
Output: `RumiVtsController/src/RumiVtsController/bin/Release/net10.0-windows/win-x64/publish/vts-mouse.exe`

### Auto-launch with VTube Studio (Steam)
The app detects when VTS closes and exits automatically. Add it to your Steam launch options so it starts and stops with VTS automatically:

**Steam → Right-click VTube Studio → Properties → Launch Options:**
```
cmd /c start "" "C:\path\to\vts-mouse.exe" & start "" %command%
```
Replace the path with the full path to your built `vts-mouse.exe`.

### Custom tray icon
Drop an `icon.ico` file next to `vts-mouse.exe` and it will be used as the tray icon automatically.

---

## 🎛️ Tray Menu

| Item | Description |
|---|---|
| Version | Current build (read-only) |
| **Reconnect** | Retry the VTS WebSocket connection |
| **Hz** | Cycle injection rate: 30 / 60 / 120. VTS is capped at 60 Hz so values above 60 only reduce latency, not actual smoothness. 24 Hz is the minimum before motion becomes noticeably choppy. |
| **Calibrate Head Offset** | Click once, then click on your model's head to store the center offset |
| **Delta Radius Targets** | Pick which windows should use the larger `radiusOverridePx` dead zone for delta mode activation (see Delta Mode below) |
| **Update Model Profile** | Re-sync hotkeys from VTS for the active model |
| **Restart as Admin** | Re-launch elevated (required for raw mouse input in some games) |
| **Status** | Live connection / error info |

---

## 🔧 How Tracking Works

### Normal mode
Cursor position drives eye/head/body params relative to your model's position in VTS —
essentially the same thing VTS's built-in mouse tracking does, just with more tuning
options. This is the baseline the app builds on, not the reason to use it.

### Delta mode (games)
Activates when the cursor is hidden (fullscreen games) or when the cursor is held near
the center of a focused window for `centerHoldSeconds`. Uses WM_INPUT raw mouse deltas
to nudge params around a center pose with a spring return.

**Delta Radius Targets** — some games keep a visible cursor on screen (e.g. an in-game
crosshair) and the default 8px dead zone is too small to reliably trigger delta mode.
Add the window's title (or a substring of it) via the tray menu or `deltaMode.radiusWindowTitles`
in config and that window will use `radiusOverridePx` (default 320px) as its dead zone instead
— large enough that delta mode activates as soon as the cursor is anywhere near center.

A configurable delay (`hiddenCursorDelaySeconds`, default 3s) prevents brief cursor hides
(e.g. video players auto-hiding the cursor) from triggering delta mode accidentally.

### Smart idle
After `idleAfterSeconds` of no mouse movement the loop throttles to a low keep-alive Hz.
After `afkAfterSeconds` of smart idle the sleep animation plays. Moving the mouse wakes
her up with a jolt.

---

## ⚡ Triggers
Attach these to hotkeys in `config.json` to fire VTS hotkeys automatically in response
to tracking events. Set `triggers` to fire on entry and `resetTriggers` to fire on exit.
Add `durationSeconds` to auto-cancel after a delay, and `cooldownSeconds` to throttle re-fires.

> **`durationSeconds: 0` means not applicable — the hotkey stays active until a reset trigger fires or it is toggled off manually. It does not mean instant.**

| Trigger | What actually fires it |
|---|---|
| `onConnect` | VTS WebSocket connection succeeds |
| `onCenter` | Cursor is within `model.hotkeys.centerRadiusPx` px of the model center for at least `hoverDwellSeconds`. Fires once on entry. |
| `offCenter` | Cursor leaves the center zone (exit radius is `centerRadiusPx × hoverExitRadiusScale` for hysteresis) |
| `onModel` | Cursor enters the model outline zone (`model.hotkeys.outlineRadiusScale` × outline height) |
| `offModel` | Cursor leaves the model outline zone |
| `onDizzy` | Dizzy animation starts — triggered by rapid back-and-forth flicking past `model.hotkeys.dizzyThresholdPx`, or by sending `dizzy` via the TCP server. Requires `model.flick.enabled: true` for flick detection. |
| `offDizzy` | Dizzy animation ends — either its `durationSeconds` expires, a `resetTriggers` entry fires, or `stopdizzy` is sent via TCP |
| `onWakeJolt` | The wake jolt "panic" phase fires — eyes wide, body jumps. Happens at the start of waking from AFK if `animations.wake.wakeJolt.enabled: true` |
| `offWakeJolt` | The wake jolt "compose" phase fires — model settles back to neutral |
| `onSmartMode` | Mouse has been still for `vts.smart.idleAfterSeconds` — loop throttles to keep-alive Hz |
| `offSmartMode` | Mouse moved again, resuming from smart idle |
| `onAFK` | Smart idle has persisted for `vts.smart.afkAfterSeconds` — sleep animation plays |
| `offAFK` | Mouse moved while in AFK — wake animation plays |
| `onDeltaMode` | Game mode activated: cursor hidden, a fullscreen app is focused, or a window matching `deltaMode.radiusWindowTitles` is focused and the cursor is within `radiusOverridePx` of the window center |
| `offDeltaMode` | Cursor becomes visible again / focus leaves the game window |
| `onMonitorTransitionPrimary` | Cursor crosses onto the primary monitor from a secondary |
| `onMonitorTransitionSecondary` | Cursor crosses off the primary monitor onto any secondary |

---

## 🌐 Remote Expression Server (TCP)

Enable in config: `"expression": { "enabled": true, "port": 5100 }`

> **Security**: on first launch a random token is auto-generated and written to `config.json` — open it to find it. Include it in every message as `"token"` or the message is dropped. Set `expression.bindAddress` to the local IP of the interface to listen on — defaults to `127.0.0.1` (loopback, local machine only). Set it to your Tailscale IP to accept connections over Tailscale, or your LAN IP for local network access. Token travels in plaintext — use a VPN on untrusted networks.

Send a newline-terminated JSON object to `host:port`:
```json
{"token": "abc123", "action": "blush"}
{"token": "abc123", "action": "blush", "durationSeconds": 3.0}
{"token": "abc123", "actions": ["blush", "star"]}
{"token": "abc123", "trigger": "onExpression.blush"}
```

The `action` value matches against the `expressions` list on each hotkey in your active profile. If nothing matches it is silently dropped — the tray shows `Expression 'x' had no matching hotkeys.` Use **F3** to auto-populate your profile, then add names to the `expressions` list on each hotkey you want to trigger remotely.

**Built-in actions** (no profile entry needed): `blink` `winkleft` `winkright` `smile` `halfsmile` `sleep` `afk` `wake` `dizzy` `stopdizzy`

> ⚠️ `sleep` and `afk` are indefinite — stays asleep until `wake` is sent or a `resetTrigger` fires. Always pair with a reset path.

A minimal Python integration example is included in `send_emote.py`. Edit `HOST` to
point at the machine running VTS Mouse and use it as a template for your own bot,
LLM integration, or stream tool.

```python
# Trigger an expression with an explicit duration
python send_emote.py angry 2.5

# No duration — the hotkey's own durationSeconds in config.json controls how long it stays.
# If that is 0, the expression stays on indefinitely until a resetTrigger fires.
python send_emote.py blush
```

---

## 📁 Config

`config.json` lives next to the exe and hot-reloads on save — no restart needed.

> ⚠️ The config is JSON, which requires some technical comfort to edit. A visual config
> editor may come in a future version.

### Key sections

| Section | What it controls |
|---|---|
| `expression` | TCP server port and enable flag |
| `vts` | Connection, Hz, debug hotkeys, smart idle thresholds |
| `model` | Tracking range, hover zones, gaze systems, delta mode |
| `eye` / `head` / `body` | VTS parameter names and smoothing |
| `face` | Blink, smile, wink timing and triggers |
| `animations` | Sleep, wake, dizzy, DVD bounce — each has `exclusive` and `stackable` flags (see below) |
| `profiles` | Per-model hotkey maps (keyed by `modelId:` or `modelName:`) |

### Animation flags: `exclusive` and `stackable`

Each animation (`sleep`, `wake`, `dizzy`, `bounceDvd`) has two flags that control how it interacts with others:

| Flag | Effect |
|---|---|
| `exclusive: true` | When this animation starts it cancels all other active animations first. Default for sleep, wake, and dizzy. |
| `exclusive: false` | Starts without cancelling others. Default for DVD bounce. |
| `stackable: true` | Runs alongside any other animation regardless of their `exclusive` flag — nothing cancels it. |
| `stackable: false` | Yields to any animation that has `exclusive: true`. Default for all animations. |

**Example — sleeping DVD bounce:**
```json
"bounceDvd": { "enabled": true, "exclusive": false, "stackable": true, "trigger": "onAFK" }
```
With `stackable: true`, the bounce keeps running even after sleep's `exclusive: true` would normally stop it.

> `exclusive` and `stackable` are independent. An animation can be `exclusive: false, stackable: false` (yields to others), `exclusive: false, stackable: true` (stacks with everything), or `exclusive: true` (clears others on entry regardless of stackable).

### Profiles (per-model hotkeys)
```json
"profiles": {
  "modelId:abc123": {
    "modelName": "My Model",
    "hotkeys": {
      "Blush": {
        "id": "your-vts-hotkey-id",
        "action": "ToggleExpression",
        "expressions": ["blush"],
        "triggers": ["onCenter"],
        "resetTriggers": ["offCenter"],
        "durationSeconds": 0,
        "cooldownSeconds": 0.5
      }
    }
  }
}
```

Press **F3** while VTS is focused to auto-dump the current model's hotkeys into a
profile. **Update Model Profile** in the tray keeps it in sync as you add hotkeys in VTS.

### Minimal config example
```json
{
  "enabled": true,
  "expression": { "enabled": true, "port": 5100 },
  "vts": {
    "port": 8001,
    "pluginName": "VTSMouse",
    "pluginDeveloper": "YourName",
    "inject": { "hz": 60 },
    "smart": { "enabled": true, "idleAfterSeconds": 5, "afkAfterSeconds": 60 }
  },
  "model": {
    "useModelCenter": true,
    "mapping": { "rangePxX": 540, "rangePxY": 960, "useVirtualDesktop": true },
    "deltaMode": { "enabled": true, "useFullscreen": true }
  },
  "eye":  { "paramX": "EyeX",  "paramY": "EyeY"  },
  "head": { "paramX": "HeadX", "paramY": "HeadY", "paramZ": "HeadZ" },
  "body": { "paramX": "BodyX", "paramY": "BodyY", "paramZ": "BodyZ" },
  "face": {
    "blink": { "enabled": true, "paramLeft": "EyeBlinkL", "paramRight": "EyeBlinkR" },
    "smile": { "enabled": true, "paramLeft": "EyeSmileL", "paramRight": "EyeSmileR", "triggers": ["onCenter"] },
    "wink":  { "enabled": true, "triggers": ["onDizzy"] }
  },
  "profiles": {}
}
```

---

## 📝 Notes

- **Resource usage** — ~0.1–0.2% CPU (peak ~0.5%), ~35 MB RAM on a Ryzen 9 9900X.
  Performance on low-end hardware is untested but the workload is minimal.
- **Codebase** — `RumiVtsCore` (class library): tracking, VTS API, config, math.
  `RumiVtsController` (WinExe): tray UI and lifecycle.
- Custom params must exist in your VTS model or be created via the API.
- If centering feels off, use **Calibrate** or adjust `model.offsetY` manually.
- `model.outlineRefHeight` is set during calibration to keep offsets scaled to outline size.
- Debug hotkeys (F1–F4) only fire when VTS is the foreground window.
- **F4** freezes the internal cursor position — the real mouse can move freely without affecting tracking.
- `deltaMode.radiusWindowTitles` / `radiusOverridePx` — by default delta mode requires the cursor to be within `centerRadiusPx` (8px) of the window center before activating. Some games keep a visible cursor on screen and 8px is too small to reliably hit. Add a window title substring to `radiusWindowTitles` (or use the tray **Delta Radius Targets** menu) and that window will use `radiusOverridePx` (default 320px) instead — a much larger catch zone so delta mode activates as soon as the cursor is anywhere near the middle of the screen.
- Some games only deliver raw input to elevated processes. Use **Restart as Admin** if delta mode isn't responding.
- Autonomous gaze and legacy jitter are mutually exclusive — enable only one.
- `gaze_bias.json` persists the gaze history between sessions so the model naturally looks at familiar regions.

---

*This project was vibecoded with Claude. All logic, architecture, and decisions were directed by the author; AI was used as the implementation tool.*
