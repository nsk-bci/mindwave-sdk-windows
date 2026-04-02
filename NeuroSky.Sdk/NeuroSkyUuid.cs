using System;

namespace NeuroSky.Sdk;

public static class NeuroSkyUuid
{
    // MindWave Mobile BLE Characteristics
    public static readonly Guid ESense    = new("039afff8-2c94-11e3-9e06-0002a5d5c51b");
    public static readonly Guid Handshake = new("039affa0-2c94-11e3-9e06-0002a5d5c51b");
    public static readonly Guid RawEeg    = new("039afff4-2c94-11e3-9e06-0002a5d5c51b");
    public static readonly Guid Cccd      = new("00002902-0000-1000-8000-00805f9b34fb");

    // BT Classic SPP
    public static readonly Guid Spp       = new("00001101-0000-1000-8000-00805f9b34fb");

    // Device Info (standard BLE)
    public static readonly Guid Manufacturer  = new("00002a29-0000-1000-8000-00805f9b34fb");
    public static readonly Guid ModelNumber   = new("00002a24-0000-1000-8000-00805f9b34fb");
    public static readonly Guid SerialNumber  = new("00002a25-0000-1000-8000-00805f9b34fb");
    public static readonly Guid HwRevision    = new("00002a27-0000-1000-8000-00805f9b34fb");
    public static readonly Guid FwRevision    = new("00002a26-0000-1000-8000-00805f9b34fb");
    public static readonly Guid SwRevision    = new("00002a28-0000-1000-8000-00805f9b34fb");
}

public static class NeuroSkyCommand
{
    public const byte StartRawEeg = 0x15;
    public const byte StopRawEeg  = 0x16;
    public const byte StartESense = 0x17;
    public const byte StopESense  = 0x18;
    public const byte Notch50Hz   = 0x1B;  // China/Europe
    public const byte Notch60Hz   = 0x1C;  // Korea/USA
}
