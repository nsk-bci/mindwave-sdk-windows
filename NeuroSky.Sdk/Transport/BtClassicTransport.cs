using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace NeuroSky.Sdk;

public sealed class BtClassicTransport : ITransport
{
    private StreamSocket? _socket;
    private DataReader? _reader;
    private DataWriter? _writer;
    private readonly ThinkGearParser _parser = new();
    private Channel<BrainWaveData> _channel = Channel.CreateUnbounded<BrainWaveData>();
    private CancellationTokenSource? _readCts;

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
        // 재연결 시 새 채널 생성
        _channel = Channel.CreateUnbounded<BrainWaveData>();
        SetState(ConnectionState.Connecting);
        try
        {
            ulong address = ParseAddress(deviceAddress);
            var device = await BluetoothDevice.FromBluetoothAddressAsync(address);

            var servicesResult = await device.GetRfcommServicesForIdAsync(
                RfcommServiceId.SerialPort, BluetoothCacheMode.Uncached);

            if (servicesResult.Services.Count == 0)
            {
                SetState(ConnectionState.Error);
                return;
            }

            var service = servicesResult.Services[0];
            _socket = new StreamSocket();
            await _socket.ConnectAsync(
                service.ConnectionHostName,
                service.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            _reader = new DataReader(_socket.InputStream)  { InputStreamOptions = InputStreamOptions.Partial };
            _writer = new DataWriter(_socket.OutputStream);

            SetState(ConnectionState.Connected);

            // Start reading stream in background
            _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = ReadLoopAsync(_readCts.Token);
        }
        catch
        {
            SetState(ConnectionState.Error);
        }
    }

    public async Task DisconnectAsync()
    {
        _readCts?.Cancel();
        _reader?.Dispose();
        _writer?.Dispose();
        _socket?.Dispose();
        _socket = null;
        _channel.Writer.TryComplete();
        SetState(ConnectionState.Disconnected);
        await Task.CompletedTask;
    }

    public async Task SendCommandAsync(byte cmd)
    {
        if (_writer is null) return;
        _writer.WriteByte(cmd);
        await _writer.StoreAsync();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_reader is null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                uint loaded = await _reader.LoadAsync(256).AsTask(ct);
                if (loaded == 0) break;

                while (_reader.UnconsumedBufferLength > 0)
                {
                    byte b = _reader.ReadByte();
                    var data = _parser.ParseByte(b);
                    if (data is not null)
                        _channel.Writer.TryWrite(data);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            SetState(ConnectionState.Error);
        }
        finally
        {
            _channel.Writer.TryComplete();
            SetState(ConnectionState.Disconnected);
        }
    }

    private static ulong ParseAddress(string address)
    {
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
