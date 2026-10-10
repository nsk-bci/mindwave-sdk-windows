# Changelog

All notable changes to the NeuroSky MindWave Mobile Windows SDK are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

v7.0.0 continues the MindWave SDK line (legacy 4.x), rebuilt from scratch for the BLE-only MindWave Mobile 2.
Releases before 7.0.0 are documented in the [legacy changelog (v2.0.4)](https://github.com/nsk-bci/mindwave-sdk-windows/blob/v2.0.4/CHANGELOG.md).

## [Unreleased]

### Removed
- Bluetooth Classic transport (BLE-only from v7.0.0): `BtClassicTransport`, `TransportMode`,
  the `mode` argument of `NeuroSkySdk.ConnectAsync()`, `NeuroSkyUuid.Spp`, and
  `ThinkGearParser.ParseByte()` (ThinkGear serial stream)

### Added
- eyeBlink parsing

## [7.0.0] - TBD

First release of the renewed MindWave SDK line for Windows (.NET 8, `net8.0-windows10.0.19041.0`).

### Added
- `NeuroSkySdk` with an async API: `ConnectAsync()`, `DisconnectAsync()`, `SendCommandAsync()`, and an `IAsyncEnumerable<BrainWaveData>` data stream
- `FindDeviceAddressAsync(name, timeoutMs)` to look up a headset's MAC address with a BLE advertisement scan
- BLE transport (WinRT GATT)
- `ThinkGearParser` for BLE eSense (`0xEA`/`0xEB`/`0xEC`) and Raw EEG packets
- `BrainWaveData` model with eSense values, eight EEG bands, Raw EEG (512 Hz), and derived `SignalQuality`
- `SimulatorTransport` (`Random` / `Focused` / `Relaxed` / `PoorSignal`) for development without a headset
- `TrimmerRootDescriptor.xml` shipped in the package, so trimmed and AOT builds keep the BLE transport and parser
- `LICENSE` (Apache License 2.0) and `NOTICE`

### Changed
- Version scheme realigned with the MindWave SDK line (legacy 4.x)
- The NuGet package version now comes from the Git tag (`publish.yml`) instead of being hard-coded in `NeuroSky.Sdk.csproj`

### Removed
- Developer guide PDFs: superseded by [`docs/developer-guide.md`](docs/developer-guide.md)
