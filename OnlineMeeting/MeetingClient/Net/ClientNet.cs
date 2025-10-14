using System.Net.Sockets;
using MeetingShared;

namespace MeetingClient.Net;

public class ClientNet
{
    public TcpClient Tcp { get; private set; } = new();
    public NetworkStream Stream => Tcp.GetStream();
    public event Action<MsgType, byte[]>? OnMessage;

    private readonly byte[] _buf = new byte[1024 * 1024];
    private MemoryStream _recv = new();

    public async Task ConnectAsync(string host, int port)
    {
        await Tcp.ConnectAsync(host, port);
        _ = Task.Run(RecvLoop);
    }

    private async Task RecvLoop()
    {
        try
        {
            while (Tcp.Connected)
            {
                int read = await Stream.ReadAsync(_buf, 0, _buf.Length);
                if (read <= 0) break;
                _recv.Write(_buf, 0, read);
                while (Packet.TryParse(ref _recv, out var t, out var p))
                    OnMessage?.Invoke(t, p);
            }
        }
        catch { }
    }

    public Task SendAsync(MsgType t, byte[] payload)
        => Stream.WriteAsync(Packet.Make(t, payload)).AsTask();
}
