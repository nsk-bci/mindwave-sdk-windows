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

// BLE 우선 연결 — 5초 내 실패 시 BT Classic 자동 폴백
await sdk.ConnectAsync("AA:BB:CC:DD:EE:FF");

await foreach (var data in sdk.DataStream(cts.Token))
{
    Console.WriteLine($"Attention  : {data.Attention}");
    Console.WriteLine($"Meditation : {data.Meditation}");
    Console.WriteLine($"Signal     : {data.SignalQuality}");
}
```

## Simulator (실기기 없이 개발)

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

| Transport | 연결 방식 | 조건 |
|---|---|---|
| `BleTransport` | WinRT BLE GATT | Windows 10 1903+, BLE 어댑터 |
| `BtClassicTransport` | WinRT RFCOMM SPP | 페어링 필요 |
| `SimulatorTransport` | 가상 데이터 | 개발/테스트용 |

`NeuroSkySdk`는 BLE를 먼저 시도하고, 5초 내 연결되지 않으면 BT Classic으로 자동 전환합니다.

## BrainWaveData

| 프로퍼티 | 타입 | 범위 | 설명 |
|---|---|---|---|
| `Timestamp` | `long` | Unix ms | 수신 시각 |
| `PoorSignal` | `int` | 0~200 | 0=완벽, 200=무신호 |
| `Attention` | `int` | 0~100 | 집중도 |
| `Meditation` | `int` | 0~100 | 명상도 |
| `Delta` | `int` | 0~∞ | 0.5~2.75 Hz |
| `Theta` | `int` | 0~∞ | 3.5~6.75 Hz |
| `LowAlpha` | `int` | 0~∞ | 7.5~9.25 Hz |
| `HighAlpha` | `int` | 0~∞ | 10~11.75 Hz |
| `LowBeta` | `int` | 0~∞ | 13~16.75 Hz |
| `HighBeta` | `int` | 0~∞ | 18~29.75 Hz |
| `LowGamma` | `int` | 0~∞ | 31~39.75 Hz |
| `MidGamma` | `int` | 0~∞ | 41~49.75 Hz |
| `RawEeg` | `IReadOnlyList<int>` | -32768~32767 | 512Hz, 10샘플/패킷 |
| `EyeBlink` | `int` | 0~255 | 눈 깜빡임 세기 |
| `SignalQuality` | `SignalQuality` | enum | NoSignal/Poor/Fair/Good |

## Commands

```csharp
// 연결 후 노치 필터 설정 (전원 노이즈 제거)
await sdk.SendCommandAsync(NeuroSkyCommand.Notch60Hz);  // 한국/미국 (60Hz)
await sdk.SendCommandAsync(NeuroSkyCommand.Notch50Hz);  // 중국/유럽 (50Hz)

// Raw EEG 스트림 제어
await sdk.SendCommandAsync(NeuroSkyCommand.StartRawEeg);
await sdk.SendCommandAsync(NeuroSkyCommand.StopRawEeg);
```

## MAC 주소 확인 방법

```
설정 → Bluetooth 및 기타 디바이스 → MindWave Mobile → 자세한 정보
```

또는 PowerShell:

```powershell
Get-PnpDevice -Class Bluetooth | Where-Object { $_.FriendlyName -like "*MindWave*" }
```

## Simulator 모드

| 모드 | Attention | Meditation | 용도 |
|---|---|---|---|
| `Random` | 0~100 (랜덤) | 0~100 (랜덤) | 일반 테스트 |
| `Focused` | 70~100 | 40~60 | 집중 상태 UI 테스트 |
| `Relaxed` | 20~50 | 70~100 | 이완 상태 UI 테스트 |
| `PoorSignal` | 0 | 0 | 신호 불량 처리 테스트 |

## 프로젝트 구조

```
NeuroSky.Sdk/
├── NeuroSkySdk.cs              진입점 (BLE 우선 + BT Classic 폴백)
├── NeuroSkyUuid.cs             BLE UUID 상수, 명령 바이트 상수
├── Model/
│   └── BrainWaveData.cs        뇌파 데이터 모델
├── Transport/
│   ├── ITransport.cs           공통 인터페이스, ConnectionState enum
│   ├── BleTransport.cs         WinRT BLE GATT 구현
│   └── BtClassicTransport.cs   WinRT RFCOMM SPP 구현
├── Parser/
│   └── ThinkGearParser.cs      ThinkGear 패킷 파서
└── Simulator/
    └── SimulatorTransport.cs   개발용 시뮬레이터

NeuroSky.Sample/
└── Program.cs                  콘솔 샘플 앱
```

## 빌드

```bash
dotnet build
dotnet run --project NeuroSky.Sample
```

## CHANGELOG

### v2.0.0
- TGC(ThinkGear Connector) 완전 제거
- WinRT BLE GATT 구현 (`Windows.Devices.Bluetooth`)
- WinRT RFCOMM SPP 구현 (`Windows.Devices.Bluetooth.Rfcomm`)
- BLE 우선 + BT Classic 자동 폴백
- `IAsyncEnumerable<BrainWaveData>` 스트림 API
- Simulator 모드 (Random / Focused / Relaxed / PoorSignal)
- .NET 8, C# 12
