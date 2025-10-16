using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MeetingShared;

namespace MeetingClient.Net
{
    public class ClientNet
    {
        public TcpClient Tcp { get; private set; } = new();
        public NetworkStream Stream => Tcp.GetStream();
        // Sự kiện: bắn ra mỗi khi nhận được gói tin đầy đủ từ server
        public event Action<MsgType, byte[]>? OnMessage;

        private readonly byte[] _buf = new byte[1024 * 1024];
        private MemoryStream _recv = new();
        private bool _running = false;

        // Kết nối tới server TCP và khởi chạy vòng lặp nhận dữ liệu
        public async Task ConnectAsync(string host, int port)
        {
            try
            {
                if (Tcp.Connected)
                    return;

                Tcp = new TcpClient
                {
                    NoDelay = true, // giảm độ trễ khi gửi gói nhỏ (chat/audio)
                    ReceiveTimeout = 5000,
                    SendTimeout = 5000
                };

                await Tcp.ConnectAsync(host, port);
                _running = true;
                _ = Task.Run(RecvLoop);
            }
            catch (Exception ex)
            {
                throw new Exception($"Không thể kết nối tới server {host}:{port}. Lỗi: {ex.Message}");
            }
        }

        // Vòng lặp nhận dữ liệu: đọc socket -> đẩy vào buffer -> tách gói với Packet.TryParse
        private async Task RecvLoop()
        {
            try
            {
                while (_running && Tcp.Connected)
                {
                    int read = await Stream.ReadAsync(_buf, 0, _buf.Length);
                    if (read <= 0) break;

                    _recv.Write(_buf, 0, read);

                    while (Packet.TryParse(ref _recv, out var type, out var payload))
                        OnMessage?.Invoke(type, payload);
                }
            }
            catch (IOException)
            {
                // Kết nối bị ngắt hoặc server đóng socket
                OnMessage?.Invoke(MsgType.Info, Packet.Str("DISCONNECTED"));
            }
            catch (Exception ex)
            {
                OnMessage?.Invoke(MsgType.Info, Packet.Str("Lỗi mạng: " + ex.Message));
            }
            finally
            {
                _running = false;
                try { Tcp.Close(); } catch { }
            }
        }

        // Gửi gói tin với header theo chuẩn Packet.Make
        public async Task SendAsync(MsgType type, byte[] payload)
        {
            if (!Tcp.Connected) 
                throw new Exception("Mất kết nối đến server.");

            var data = Packet.Make(type, payload);
            try
            {
                await Stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                throw new Exception("Gửi dữ liệu thất bại: " + ex.Message);
            }
        }

        // Ngắt kết nối một cách an toàn
        public void Disconnect()
        {
            _running = false;
            try { Tcp.Close(); } catch { }
        }
    }
}
