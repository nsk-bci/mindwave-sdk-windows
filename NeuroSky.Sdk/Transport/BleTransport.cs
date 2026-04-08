using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace NeuroSky.Sdk;

public sealed class BleTransport : ITransport
{
    private BluetoothLEDevice? _device;
    private GattCharacteristic? _eSenseChar;
    private GattCharacteristic? _rawEegChar;
    private GattCharacteristic? _handshakeChar;
    private readonly ThinkGearParser _parser = new();
    private readonly Channel<BrainWaveData> _channel = Channel.CreateUnbounded<BrainWaveData>();

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event EventHandler<ConnectionState>? StateChanged;

    public async IAsyncEnumerable<BrainWaveData> DataStream(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var data in _channel.Reader.ReadAllAsync(ct))
            yield return data;
    }

    public async Task ConnectAsync(string deviceAddress, CancellationToken ct = default)
    {
        SetState(ConnectionState.Scanning);

        // MAC address string → ulong
        ulong address = ParseAddress(deviceAddress);
        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);

        if (_device is null)
        {
            SetState(ConnectionState.Error);
            return;
        }

        SetState(ConnectionState.Connecting);

        var servicesResult = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (servicesResult.Status != GattCommunicationStatus.Success)
        {
            SetState(ConnectionState.Error);
            return;
        }

        // Discover eSense / RawEEG / Handshake characteristics
        foreach (var service in servicesResult.Services)
        {
            var charsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (charsResult.Status != GattCommunicationStatus.Success) continue;

            foreach (var ch in charsResult.Characteristics)
            {
                if (ch.Uuid == NeuroSkyUuid.ESense)    _eSenseChar    = ch;
                if (ch.Uuid == NeuroSkyUuid.RawEeg)    _rawEegChar    = ch;
                if (ch.Uuid == NeuroSkyUuid.Handshake) _handshakeChar = ch;
            }
        }

        if (_eSenseChar is null)
        {
            SetState(ConnectionState.Error);
            return;
        }

        await EnableNotificationsAsync(_eSenseChar);
        if (_rawEegChar is not null)
            await EnableNotificationsAsync(_rawEegChar);

        // Handshake — start receiving eSense data
        await SendHandshakeAsync(NeuroSkyCommand.StartESense);
        SetState(ConnectionState.Connected);
    }

    public async Task DisconnectAsync()
    {
        if (_eSenseChar is not null)
            await _eSenseChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None);

        if (_rawEegChar is not null)
            await _rawEegChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None);

        _device?.Dispose();
        _device = null;
        _channel.Writer.TryComplete();
        SetState(ConnectionState.Disconnected);
    }

    public async Task SendCommandAsync(byte cmd) => await SendHandshakeAsync(cmd);

    private async Task EnableNotificationsAsync(GattCharacteristic ch)
    {
        ch.ValueChanged += OnCharacteristicValueChanged;
        await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
    }

    private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var reader = DataReader.FromBuffer(args.CharacteristicValue);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);

        var data = _parser.Parse(sender.Uuid, bytes);
        if (data is not null)
            _channel.Writer.TryWrite(data);
    }

    private async Task SendHandshakeAsync(byte cmd)
    {
        if (_handshakeChar is null) return;

        var packet = new byte[20];
        packet[0] = 0x77;
        packet[1] = 0x01;
        packet[2] = cmd;
        // Checksum: bytes[1..18] XOR 0xFF AND 0xFF
        int sum = 0;
        for (int i = 1; i <= 18; i++) sum += packet[i];
        packet[19] = (byte)((sum ^ 0xFF) & 0xFF);

        var writer = new DataWriter();
        writer.WriteBytes(packet);
        await _handshakeChar.WriteValueAsync(writer.DetachBuffer());
    }

    private static ulong ParseAddress(string address)
    {
        // "AA:BB:CC:DD:EE:FF" → ulong
        var parts = address.Split(':');
        ulong result = 0;
        foreach (var part in parts)
            result = (result << 8) | Convert.ToByte(part, 16);
        return result;
    }

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
