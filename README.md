# NeuroSky MindWave Mobile Windows SDK

[![NuGet](https://img.shields.io/nuget/v/NeuroSky.MindWave.Sdk)](https://www.nuget.org/packages/NeuroSky.MindWave.Sdk)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NeuroSky.MindWave.Sdk)](https://www.nuget.org/packages/NeuroSky.MindWave.Sdk)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

Modern C# SDK for NeuroSky MindWave Mobile EEG headsets — BLE + BT Classic via WinRT, no TGC dependency.

---

## Getting Started

### Step 1 — Add the NuGet package

**Visual Studio — Package Manager UI**
```
Tools → NuGet Package Manager → Manage NuGet Packages
Search: NeuroSky.MindWave.Sdk → Install
```

**Edit `.csproj` directly (recommended)**
```xml
<PackageReference Include="NeuroSky.MindWave.Sdk" Version="2.0.0" />
```

**.NET CLI**
```bash
dotnet add package NeuroSky.MindWave.Sdk
```

### Step 2 — Set the Windows target framework

WinRT Bluetooth APIs require a Windows-specific TFM. Open your `.csproj` and confirm:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Must include -windows10.0.19041.0 or later -->
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NeuroSky.MindWave.Sdk" Version="2.0.0" />
  </ItemGroup>
</Project>
```

> Plain `net8.0` will **not** work — WinRT types (`Windows.Devices.Bluetooth`) are only available with the Windows TFM suffix.

### Step 3 — Find your headset's MAC address

```
Settings → Bluetooth & other devices → MindWave Mobile → More info
```

Or via PowerShell:

```powershell
Get-PnpDevice -Class Bluetooth | Where-Object { $_.FriendlyName -like "*MindWave*" }
```

### Step 4 — Connect and stream

```csharp
using NeuroSky.Sdk;
using NeuroSky.Sdk.Transport;

await using var sdk = new NeuroSkySdk();
sdk.StateChanged += (_, state) => Console.WriteLine($"[State] {state}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Connect — BLE first, falls back to BT Classic automatically
await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF");

// Set notch filter for your region (removes power-line noise)
await sdk.SendCommandAsync(NeuroSkyCommand.Notch60Hz);  // Korea/USA
// await sdk.SendCommandAsync(NeuroSkyCommand.Notch50Hz);  // Europe/China

await foreach (var data in sdk.DataStream(cts.Token))
{
    Console.WriteLine($"Attention  : {data.Attention}");
    Console.WriteLine($"Meditation : {data.Meditation}");
    Console.WriteLine($"Signal     : {data.SignalQuality}");
}
```

That's it — four steps from zero to streaming EEG data.

> **Need more detail?** See the full [Developer Guide](docs/developer-guide.pdf) for architecture, all connection modes, signal quality handling, advanced patterns, and the complete API reference.

---

## Requirements

| | Minimum |
|---|---|
| OS | Windows 10 version 1903 (build 18362) |
| .NET | .NET 8.0 |
| Bluetooth | BLE adapter (BLE mode) or Classic BT adapter (BT Classic mode) |
| Device pairing | Not required for BLE; required for BT Classic |

## Connection Modes

Choose how to connect via the `TransportMode` parameter:

| Mode | Behavior | Pairing required? |
|---|---|---|
| `TransportMode.Auto` | BLE first; auto-falls back to BT Classic after 5 sec (default) | No |
| `TransportMode.Ble` | BLE only — fastest, no pairing needed | No |
| `TransportMode.BtClassic` | BT Classic only — more stable in noisy RF environments | Yes |

```csharp
// Auto (default)
await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF");

// BLE only (no pairing required)
await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF", TransportMode.Ble);

// BT Classic only — pair the device first in Windows Settings
await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF", TransportMode.BtClassic);
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

| Mode | Attention | Meditation | Use case |
|---|---|---|---|
| `Random` | 0~100 (random) | 0~100 (random) | General testing |
| `Focused` | 70~100 | 40~60 | Focused state UI testing |
| `Relaxed` | 20~50 | 70~100 | Relaxed state UI testing |
| `PoorSignal` | 0 | 0 | Signal loss / error handling test |

## BrainWaveData

| Property | Type | Range | Description |
|---|---|---|---|
| `Timestamp` | `long` | Unix ms | Time of reception |
| `PoorSignal` | `int` | 0~200 | 0=perfect, 200=no signal |
| `Attention` | `int` | 0~100 | eSense attention level |
| `Meditation` | `int` | 0~100 | eSense meditation level |
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
// Notch filter — removes power-line noise (call after connecting)
await sdk.SendCommandAsync(NeuroSkyCommand.Notch60Hz);  // Korea/USA (60Hz)
await sdk.SendCommandAsync(NeuroSkyCommand.Notch50Hz);  // China/Europe (50Hz)

// Raw EEG stream (disabled by default)
await sdk.SendCommandAsync(NeuroSkyCommand.StartRawEeg);
await sdk.SendCommandAsync(NeuroSkyCommand.StopRawEeg);
```

## Transport

| Transport | Method | Requirement |
|---|---|---|
| `BleTransport` | WinRT BLE GATT | Windows 10 1903+, BLE adapter |
| `BtClassicTransport` | WinRT RFCOMM SPP | Paired device in Windows Settings |
| `SimulatorTransport` | Virtual data | For development/testing |

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
- `TransportMode` enum: Auto (BLE→BT fallback), Ble only, BtClassic only
- `IAsyncEnumerable<BrainWaveData>` stream API
- Simulator modes: Random / Focused / Relaxed / PoorSignal
- .NET 8, C# 12
- Published to NuGet.org

## License

Apache License 2.0
