# Changelog

All notable changes to the NeuroSky MindWave Mobile Windows SDK are documented here.

---

## v2.0.1

### Fixed

- **Namespace flattening** — all SDK types are now in the single `NeuroSky.Sdk` namespace.  
  Previously, types were split across sub-namespaces that were undocumented and required extra `using` directives:

  | Type | Before | After |
  |---|---|---|
  | `BrainWaveData`, `SignalQuality` | `NeuroSky.Sdk.Model` | `NeuroSky.Sdk` |
  | `ConnectionState`, `TransportMode`, `ITransport` | `NeuroSky.Sdk.Transport` | `NeuroSky.Sdk` |
  | `SimulatorTransport` | `NeuroSky.Sdk.Simulator` | `NeuroSky.Sdk` |
  | `ThinkGearParser` | `NeuroSky.Sdk.Parser` | `NeuroSky.Sdk` |

  **Migration:** remove any `using NeuroSky.Sdk.Model;`, `using NeuroSky.Sdk.Transport;`, `using NeuroSky.Sdk.Simulator;`, `using NeuroSky.Sdk.Parser;` — `using NeuroSky.Sdk;` alone is sufficient.

---

## v2.0.0

Complete rewrite — TGC eliminated, WinRT Bluetooth stack, modern async API.

### Breaking Changes

- No dependency on ThinkGear Connector (TGC). The TGC background service is no longer required.
- API is fully async: `ConnectAsync()`, `DisconnectAsync()`, `SendCommandAsync()`, `DataStream()`.
- Requires `net8.0-windows10.0.19041.0` TFM — plain `net8.0` will not compile.

### New

- **`BleTransport`** — WinRT BLE GATT implementation (`Windows.Devices.Bluetooth`)
  - `connectGatt()` → CCCD subscribe → Handshake(`0x17`) → data stream
  - Subscribes to eSense (`039afff8`) and RawEEG (`039afff4`) characteristics
- **`BtClassicTransport`** — WinRT RFCOMM SPP implementation (`Windows.Devices.Bluetooth.Rfcomm`)
  - SPP UUID `00001101-0000-1000-8000-00805f9b34fb`
  - Requires device pairing in Windows Settings before connecting
- **`TransportMode` enum** — `Ble` (default), `BtClassic`
- **`IAsyncEnumerable<BrainWaveData>` stream API** — native `await foreach`, cancel via `CancellationToken`
- **`NeuroSkySdk.FindDeviceAddressAsync(name, timeoutMs)`** — resolves device name to MAC address via BLE advertisement scan; cache result in app settings for faster subsequent connects
- **`BrainWaveData` model**
  - `Timestamp` (Unix ms), `PoorSignal` (0~200), `Attention` (0~100), `Meditation` (0~100)
  - 8 EEG frequency bands: Delta, Theta, LowAlpha, HighAlpha, LowBeta, HighBeta, LowGamma, MidGamma
  - `RawEeg` (`IReadOnlyList<int>`) — 512 Hz, 10 samples/packet
  - `EyeBlink` (0~255)
  - `SignalQuality` (derived enum: `Good` / `Fair` / `Poor` / `NoSignal`)
- **`SimulatorTransport`** — virtual data source for development without hardware
  - Modes: `Random`, `Focused`, `Relaxed`, `PoorSignal`
  - Emits one `BrainWaveData` per second
- **`ThinkGearParser`** — shared parser for BLE (0xEA / 0xEB / 0xEC packets) and BT Classic (0xAA 0xAA sync header, XOR checksum validation)
- **NuGet package** — `NeuroSky.MindWave.Sdk` published to NuGet.org
- **.NET 8, C# 12** — nullable annotations, implicit usings, primary constructors

### Removed

- All dependency on `ThinkGear.dll` and TGC COM/socket API
- `TransportMode.Auto` — explicit transport selection only; no hidden fallback logic
