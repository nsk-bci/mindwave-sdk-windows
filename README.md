# NeuroSky MindWave Mobile Windows SDK

[![NuGet](https://img.shields.io/nuget/v/NeuroSky.MindWave.Sdk)](https://www.nuget.org/packages/NeuroSky.MindWave.Sdk)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

Modern C# SDK for the NeuroSky MindWave Mobile 2 EEG headset — Bluetooth Low Energy (BLE) via WinRT.

> [!NOTE]
> v7.0.0 continues the MindWave SDK line (legacy 4.x), rebuilt from scratch
> for the BLE-only MindWave Mobile 2. Bluetooth Classic support is removed.

---

## Getting Started

> [!TIP]
> **Before diving into the steps — read the [Developer Guide](docs/developer-guide.md) first.**  
> It covers the full connection flow, BLE internals, signal quality handling, packet timing, advanced patterns, and the complete API reference. Most integration questions are answered there.

### Step 1 — Add the NuGet package

**Visual Studio — Package Manager UI**
```
Tools → NuGet Package Manager → Manage NuGet Packages
Search: NeuroSky.MindWave.Sdk → Install
```

**Edit `.csproj` directly (recommended)**
```xml
<PackageReference Include="NeuroSky.MindWave.Sdk" Version="7.0.0" />
```

**.NET CLI**
```bash
dotnet add package NeuroSky.MindWave.Sdk --version 7.0.0
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
    <PackageReference Include="NeuroSky.MindWave.Sdk" Version="7.0.0" />
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

await using var sdk = new NeuroSkySdk();
sdk.StateChanged += (_, state) => Console.WriteLine($"[State] {state}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Connect — BLE by default
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

---

## Requirements

| | Minimum |
|---|---|
| OS | Windows 10 version 1903 (build 18362) |
| .NET | .NET 8.0 |
| Bluetooth | BLE adapter |
| Device pairing | Not required |
| Headset | MindWave Mobile 2 (MindWave Mobile 1st gen and third-party TGAM boards are not supported) |

## Connection States

Subscribe to `StateChanged` to observe the lifecycle. `ConnectAsync()` never throws on connection failure — it transitions to `Error` instead, so always check the final state.

| State | Meaning |
|---|---|
| `Disconnected` | Initial state, or after `DisconnectAsync()` / link drop |
| `Scanning` | BLE only — resolving the MAC address |
| `Connecting` | GATT service discovery in progress |
| `Connected` | Notifications enabled and handshake sent — `DataStream` will emit packets |
| `Error` | Device not found, GATT discovery failed, or handshake characteristic missing. `DataStream` will not emit; call `DisconnectAsync()` and retry. |

```csharp
sdk.StateChanged += (_, state) =>
{
    if (state == ConnectionState.Error)
    {
        Console.WriteLine("Connection failed — verify power / MAC address.");
    }
};
```

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

## Finding Your Device Address

`ConnectAsync()` takes a Bluetooth MAC address. Use `FindDeviceAddressAsync()` once on first launch to discover it, then store it (e.g., in app settings) for faster subsequent connections.

```csharp
await using var sdk = new NeuroSkySdk();

// Discover by name — scans BLE advertisements for up to 10 s
var cached = Properties.Settings.Default.DeviceMac;
var address = !string.IsNullOrEmpty(cached)
    ? cached
    : await sdk.FindDeviceAddressAsync("MindWave Mobile");

if (address is null)
{
    Console.WriteLine("Device not found — check power and BLE adapter.");
    return;
}

Properties.Settings.Default.DeviceMac = address;
Properties.Settings.Default.Save();   // cache — skips scan next launch

await sdk.ConnectAsync(address);
```

## Working with DataStream

### Packet timing

In BLE mode, two characteristics transmit packets at different rates.

| Characteristic | Fields | Rate |
|---|---|---|
| eSense `039afff8` | Attention, Meditation, EEG bands | ~1 Hz |
| RawEEG `039afff4` | `RawEeg` (10 samples) | ~51 Hz (512 Hz ÷ 10) |

`ThinkGearParser` accumulates state. Regardless of which characteristic triggered the emit, each `BrainWaveData` object contains the latest accumulated value of every field.

### Caution — attention-based filter

```csharp
// Wrong pattern — drops all packets in RawEEG-only sessions
await foreach (var data in sdk.DataStream(ct))
{
    if (data.Attention == 0) continue;  // Attention is always 0 when eSense is off
    // ...
}
```

If `StopESense` is sent or `StartESense` is never called, the device does not transmit attention data. `Attention` stays at 0 and this guard silently drops every packet.

**Correct patterns:**

```csharp
// eSense session — filter by signal quality, not value
await foreach (var data in sdk.DataStream(ct))
{
    if (data.SignalQuality == SignalQuality.NoSignal) continue;
    Console.WriteLine($"Attention: {data.Attention}");
}

// RawEEG-only session
await sdk.SendCommandAsync(NeuroSkyCommand.StopESense);
await sdk.SendCommandAsync(NeuroSkyCommand.StartRawEeg);
await foreach (var data in sdk.DataStream(ct))
{
    if (data.RawEeg.Count > 0)
        foreach (var sample in data.RawEeg) ProcessRawSample(sample);
}

// eSense + RawEEG simultaneously — process only the populated fields in each packet
await sdk.SendCommandAsync(NeuroSkyCommand.StartRawEeg);  // eSense is active by default
await foreach (var data in sdk.DataStream(ct))
{
    if (data.RawEeg.Count > 0)  UpdateRawEegChart(data.RawEeg);
    if (data.Attention > 0)     UpdateEsenseUI(data);
}
```

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

## Project Structure

```
NeuroSky.Sdk/
├── NeuroSkySdk.cs              Entry point (BLE)
├── NeuroSkyUuid.cs             BLE UUID constants, command byte constants
├── Model/
│   └── BrainWaveData.cs        EEG data model
├── Transport/
│   ├── ITransport.cs           Common interface, ConnectionState enum
│   └── BleTransport.cs         WinRT BLE GATT implementation
└── Parser/
    └── ThinkGearParser.cs      ThinkGear packet parser

NeuroSky.Sample/
└── Program.cs                  Console sample app
```

## Trimming / Self-contained / AOT

The package is marked `IsTrimmable=true` and ships an internal `TrimmerRootDescriptor.xml` that preserves the BLE/BT transports, parser, and public API surface. WinRT GATT callbacks are dispatched by the Windows Bluetooth stack via reflection-like mechanisms, so without these roots the trimmer would silently strip the handlers and BLE data would never arrive.

No consumer action is required — publishing with `PublishTrimmed=true`, self-contained, or `PublishAot=true` works out of the box.

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <SelfContained>true</SelfContained>
</PropertyGroup>
```

## Build

```bash
dotnet build
dotnet run --project NeuroSky.Sample
```

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

Apache License 2.0
