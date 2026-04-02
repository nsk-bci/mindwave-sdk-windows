# NeuroSky MindWave Windows SDK

Modern C# SDK for NeuroSky MindWave EEG headsets — BLE + BT Classic via WinRT, no TGC dependency.

## Requirements

- Windows 10 1903 (build 18362) or later
- .NET 8.0
- Bluetooth adapter (BLE or Classic)

## Installation

```xml
<!-- .csproj -->
<PackageReference Include="NeuroSky.MindWave.Sdk" Version="2.0.0" />
```

## Quick Start

```csharp
using NeuroSky.Sdk;

await using var sdk = new NeuroSkySdk();
sdk.StateChanged += (_, state) => Console.WriteLine($"[State] {state}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Auto: BLE first, falls back to BT Classic automatically after 5 seconds (default)
await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF");

// BLE only (no pairing required)
await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF", TransportMode.Ble);

// BT Classic only (requires Windows pairing)
await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF", TransportMode.BtClassic);

await foreach (var data in sdk.DataStream(cts.Token))
{
    Console.WriteLine($"Attention  : {data.Attention}");
    Console.WriteLine($"Meditation : {data.Meditation}");
    Console.WriteLine($"Signal     : {data.SignalQuality}");
}
```

## Simulator (without a real device)

```csharp
using NeuroSky.Sdk.Simulator;

var simulator = new SimulatorTransport();
simulator.SetMode(SimulatorTransport.Mode.Focused);

await simulator.ConnectAsync("simulator");

await foreach (var data in simulator.DataStream(cts.Token))
{
    Console.WriteLine($"Attention: {data.Attention}");
}
```

## Transport

| Transport | Method | Requirement |
|---|---|---|
| `BleTransport` | WinRT BLE GATT | Windows 10 1903+, BLE adapter |
| `BtClassicTransport` | WinRT RFCOMM SPP | Paired device required |
| `SimulatorTransport` | Virtual data | For development/testing |

`NeuroSkySdk` tries BLE first. If not connected within 5 seconds, it automatically switches to BT Classic.

## BrainWaveData

| Property | Type | Range | Description |
|---|---|---|---|
| `Timestamp` | `long` | Unix ms | Time of reception |
| `PoorSignal` | `int` | 0~200 | 0=perfect, 200=no signal |
| `Attention` | `int` | 0~100 | Attention level |
| `Meditation` | `int` | 0~100 | Meditation level |
| `Delta` | `int` | 0~∞ | 0.5~2.75 Hz |
| `Theta` | `int` | 0~∞ | 3.5~6.75 Hz |
| `LowAlpha` | `int` | 0~∞ | 7.5~9.25 Hz |
| `HighAlpha` | `int` | 0~∞ | 10~11.75 Hz |
| `LowBeta` | `int` | 0~∞ | 13~16.75 Hz |
| `HighBeta` | `int` | 0~∞ | 18~29.75 Hz |
| `LowGamma` | `int` | 0~∞ | 31~39.75 Hz |
| `MidGamma` | `int` | 0~∞ | 41~49.75 Hz |
| `RawEeg` | `IReadOnlyList<int>` | -32768~32767 | 512Hz, 10 samples/packet |
| `EyeBlink` | `int` | 0~255 | Eye blink intensity |
| `SignalQuality` | `SignalQuality` | enum | NoSignal/Poor/Fair/Good |

## Commands

```csharp
// Set notch filter after connecting (removes power line noise)
await sdk.SendCommandAsync(NeuroSkyCommand.Notch60Hz);  // Korea/USA (60Hz)
await sdk.SendCommandAsync(NeuroSkyCommand.Notch50Hz);  // China/Europe (50Hz)

// Raw EEG stream control
await sdk.SendCommandAsync(NeuroSkyCommand.StartRawEeg);
await sdk.SendCommandAsync(NeuroSkyCommand.StopRawEeg);
```

## Finding Your MAC Address

```
Settings → Bluetooth & other devices → MindWave Mobile → More info
```

Or via PowerShell:

```powershell
Get-PnpDevice -Class Bluetooth | Where-Object { $_.FriendlyName -like "*MindWave*" }
```

## Simulator Modes

| Mode | Attention | Meditation | Use case |
|---|---|---|---|
| `Random` | 0~100 (random) | 0~100 (random) | General testing |
| `Focused` | 70~100 | 40~60 | Focused state UI testing |
| `Relaxed` | 20~50 | 70~100 | Relaxed state UI testing |
| `PoorSignal` | 0 | 0 | Poor signal handling test |

## Project Structure

```
NeuroSky.Sdk/
├── NeuroSkySdk.cs              Entry point (BLE first + BT Classic fallback)
├── NeuroSkyUuid.cs             BLE UUID constants, command byte constants
├── Model/
│   └── BrainWaveData.cs        EEG data model
├── Transport/
│   ├── ITransport.cs           Common interface, ConnectionState enum
│   ├── BleTransport.cs         WinRT BLE GATT implementation
│   └── BtClassicTransport.cs   WinRT RFCOMM SPP implementation
├── Parser/
│   └── ThinkGearParser.cs      ThinkGear packet parser
└── Simulator/
    └── SimulatorTransport.cs   Simulator for development

NeuroSky.Sample/
└── Program.cs                  Console sample app
```

## Build

```bash
dotnet build
dotnet run --project NeuroSky.Sample
```

## Changelog

### v2.0.0
- Removed TGC (ThinkGear Connector) dependency entirely
- WinRT BLE GATT implementation (`Windows.Devices.Bluetooth`)
- WinRT RFCOMM SPP implementation (`Windows.Devices.Bluetooth.Rfcomm`)
- BLE first + automatic BT Classic fallback
- `IAsyncEnumerable<BrainWaveData>` stream API
- Simulator modes: Random / Focused / Relaxed / PoorSignal
- .NET 8, C# 12

## License

Apache License 2.0
