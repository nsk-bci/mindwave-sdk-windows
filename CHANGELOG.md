# Changelog

All notable changes to the NeuroSky MindWave Mobile Windows SDK are documented here.

---

## v2.0.4 — 2026-06-05

### Fixed

#### `ThinkGearParser.ParseESense` — BLE eSense 패킷 타입을 잘못된 오프셋에서 읽음 (Attention/Meditation 항상 0)
- MWM2 BLE eSense 특성(`039afff8`) 페이로드는 2바이트 프리픽스(`00 00`) 뒤에 패킷 타입(`0xEA/0xEB/0xEC`)이 온다. 즉 타입은 `bytes[2]`에 있다 (레퍼런스 SDK `MWMleService`: `byte packType = data[2]`).
- 그런데 파서는 타입을 `bytes[0]`에서 읽어, 실제 패킷(`00 00 EA …`)이 항상 `default => null`로 빠졌다. 그 결과 **BLE 모드에서 Attention/Meditation/PoorSignal/대역이 전부 0**으로 나왔다 (raw EEG·블링크는 정상이라 증상이 가려짐). BT Classic 경로는 별도 파서라 영향 없음.
- 필드 오프셋(`PoorSignal=bytes[6]`, `Attention=bytes[8]`, `Meditation=bytes[10]`, 대역 `@5/9/13/17`)은 이미 프리픽스를 전제로 정확했음 — 타입 검사만 `bytes[2]`로 정정하고 최소 길이 가드를 `>= 3`으로 보강.
- 실기기(MWM2) 확인: Attention 0→84 / Meditation→61 으로 집중·이완에 정상 반응.
- 회귀 방지: `ThinkGearParserTests`에 실기기 캡처 패킷 기반 테스트 추가. (기존 eSense 테스트가 버그와 동일한 `bytes[0]` 레이아웃을 사용해 버그를 통과시키고 있었음 → 실제 레이아웃으로 정정)

---

## v2.0.3 — 2026-05-11

### Fixed

#### `TrimmerRootDescriptor.xml` — v2.0.1 네임스페이스 평탄화 후속 누락
- v2.0.1에서 모든 타입을 `NeuroSky.Sdk` 단일 네임스페이스로 평탄화했으나, 트리머 디스크립터는 여전히 옛 중첩 네임스페이스(`NeuroSky.Sdk.Transport.BleTransport`, `NeuroSky.Sdk.Parser.ThinkGearParser`, `NeuroSky.Sdk.Model.BrainWaveData` 등)를 `fullname`으로 가리키고 있었음
- 그 결과 트림된 / self-contained / AOT 빌드에서 트리머가 매칭되는 타입을 찾지 못해 모든 보호 대상이 사실상 무시되고, 트랜스포트·파서가 조용히 제거됨 → BLE 데이터가 도착하지 않는 증상(이 파일의 존재 이유 자체가 무력화)
- 모든 `fullname`을 평탄화된 실제 FQN으로 정정

#### `BleTransport.ConnectAsync` — 핸드셰이크 캐릭터리스틱 미발견 시 잘못된 `Connected` 상태
- `_handshakeChar`가 `null`일 때 `SendHandshakeAsync()`가 조용히 반환하면서도 상태는 `Connected`로 전이, 호출자는 연결된 것으로 인식하지만 `DataStream`에 패킷이 영원히 도착하지 않음
- 핸드셰이크가 없으면 `StartESense`를 보낼 수 없으므로 정상 동작이 불가능 → `_eSenseChar`와 동일하게 `null`이면 `ConnectionState.Error`로 전이하도록 수정

---

## v2.0.2 — 2026-04-09

### Fixed

#### `ThinkGearParser` — BT Classic unknown extended code skip
- `ParseSerialPayload()` `default` case가 이제 확장 코드(`>= 0x80`)를 올바르게 처리: 길이 바이트를 읽고 `len`만큼 추가 건너뜀
- 이전에는 모든 미지 코드에서 인덱스를 1씩만 증가시켜, 인식하지 못한 확장 타입 코드에서 파서 위치 오동기 발생

#### `ThinkGearParser` — BT Classic `0x83` bounds guard
- `ParseSerialPayload()` `case 0x83` (EEG Power 24바이트 블록)에서 `len` 바이트 읽기 전 `if (i >= payload.Length) break` 추가
- 이전에는 잘려진 페이로드에서 `IndexOutOfRangeException` 발생 가능

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
