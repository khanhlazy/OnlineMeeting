using System.Net;
using System.Net.Sockets;
using MeetingShared;
using System.Collections.Concurrent;
using System.Linq;

namespace MeetingServer;

public class Server
{
    private readonly Db _db;
    private readonly RoomManager _rooms = new();
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<ClientConn, Task> _clients = new();
    private const int MaxVideoBytes = 300_000; // ~300 KB per frame
    // Server TCP đơn giản: nhận gói từ client, phân loại theo MsgType và xử lý.
    // Quản lý phòng họp qua RoomManager, broadcast chat/video/audio tới các thành viên trong phòng.

    public Server(IPAddress ip, int port, Db db)
    {
        _db = db;
        _listener = new TcpListener(ip, port);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _listener.Start();
        Console.WriteLine($"Server đang lắng nghe tại {_listener.LocalEndpoint}");
        while (!ct.IsCancellationRequested)
        {
            if (_listener.Pending())
            {
                var tcp = await _listener.AcceptTcpClientAsync(ct);
                var conn = new ClientConn(tcp);
                var task = Task.Run(() => HandleClientAsync(conn, ct));
                _clients[conn] = task;
            }
            else
            {
                await Task.Delay(50, ct);
            }
        }
    }

    private async Task HandleClientAsync(ClientConn c, CancellationToken ct)
    {
        Console.WriteLine("Client kết nối: " + c.Tcp.Client.RemoteEndPoint);
        var ns = c.Stream;
        var buf = new byte[1024 * 1024];
        var recv = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && c.Tcp.Connected)
            {
                int read = await ns.ReadAsync(buf, 0, buf.Length, ct);
                if (read <= 0) break;
                recv.Write(buf, 0, read);
                // Tách các gói đầy đủ từ buffer và xử lý từng gói
                while (Packet.TryParse(ref recv, out var type, out var payload))
                {
                    await OnMessageAsync(c, type, payload, ct);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lỗi client: {ex.Message}");
        }
        finally
        {
            await OnDisconnectAsync(c);
            c.Tcp.Close();
            Console.WriteLine("Client ngắt kết nối");
        }
    }

    private async Task OnMessageAsync(ClientConn c, MsgType type, byte[] payload, CancellationToken ct)
    {
        // Router: phân luồng xử lý theo loại thông điệp
        switch (type)
        {
            case MsgType.Register:
            {
                // payload: "username|password"
                var parts = Packet.Str(payload).Split('|', 2);
                var ok = await _db.RegisterAsync(parts[0], parts[1]);
                await SendAsync(c, MsgType.Info, Packet.Str(ok ? "REGISTER_OK" : "REGISTER_FAIL"));
                break;
            }
            case MsgType.Login:
            {
                // payload: "username|password"
                var parts = Packet.Str(payload).Split('|', 2);
                var ok = await _db.LoginAsync(parts[0], parts[1]);
                if (ok) c.Username = parts[0];
                await SendAsync(c, MsgType.Info, Packet.Str(ok ? "LOGIN_OK" : "LOGIN_FAIL"));
                break;
            }
            case MsgType.CreateRoom:
            {
                // Tạo phòng mới, đặt client là Host, trả về mã phòng
                if (string.IsNullOrEmpty(c.Username)) { await SendAsync(c, MsgType.Info, Packet.Str("NEED_LOGIN")); break; }
                var room = _rooms.CreateRoom(c);
                await SendAsync(c, MsgType.Info, Packet.Str($"ROOM_CREATED|{room.Id}"));
                await BroadcastParticipants(room);
                break;
            }
            case MsgType.JoinRoom:
            {
                // Tham gia phòng theo mã; yêu cầu đã đăng nhập
                if (string.IsNullOrEmpty(c.Username)) { await SendAsync(c, MsgType.Info, Packet.Str("NEED_LOGIN")); break; }
                var rid = Packet.Str(payload);
                var room = _rooms.Get(rid);
                if (room == null) { await SendAsync(c, MsgType.Info, Packet.Str("ROOM_NOT_FOUND")); break; }
                c.RoomId = rid;
                room.Add(c);
                await SendAsync(c, MsgType.Info, Packet.Str($"JOIN_OK|{rid}|HOST={room.Host.Username}"));
                await BroadcastParticipants(room);
                break;
            }

            // Gửi chat/video/audio cho các thành viên khác
            case MsgType.Chat:
{
    if (c.RoomId is null) break;
    var room = _rooms.Get(c.RoomId);
    if (room is null) break;

    var text = $"{c.Username}: {Packet.Str(payload)}";
    var pkt = Packet.Make(MsgType.Chat, Packet.Str(text));

    foreach (var m in room.Members)
    {
        if (m == c) continue; // không echo cho chính người gửi
        try { await m.Stream.WriteAsync(pkt); } catch { }
    }
    break;
}

            case MsgType.Video:
            {
                // Giới hạn kích thước khung hình để tránh nghẽn băng thông
                if (payload is { Length: > MaxVideoBytes })
                {
                    // Drop oversized frames silently
                    break;
                }
                if (c.RoomId is null) break;
                var room = _rooms.Get(c.RoomId);
                if (room is null) break;
                await BroadcastRoom(room, c, type, payload);
                break;
            }
            case MsgType.Audio:
            {
                // Audio PCM 16kHz mono, trộn đơn giản ở client
                if (c.RoomId is null) break;
                var room = _rooms.Get(c.RoomId);
                if (room is null) break;
                await BroadcastRoom(room, c, type, payload);
                break;
            }

            // CẬP NHẬT TRẠNG THÁI CAM/MIC + THÔNG BÁO LẠI DANH SÁCH
            case MsgType.ToggleCam:
            {
                if (c.RoomId is null) break;
                var room = _rooms.Get(c.RoomId);
                if (room is null) break;

                c.CamOn = Packet.Str(payload) == "ON";
                await BroadcastParticipants(room);                 // cập nhật [Cam:On/Off] cho tất cả
                await BroadcastRoom(room, c, MsgType.ToggleCam, payload);
                break;
            }
            case MsgType.ToggleMic:
            {
                if (c.RoomId is null) break;
                var room = _rooms.Get(c.RoomId);
                if (room is null) break;

                c.MicOn = Packet.Str(payload) == "ON";
                await BroadcastParticipants(room);                 // cập nhật [Mic:On/Off]
                await BroadcastRoom(room, c, MsgType.ToggleMic, payload);
                break;
            }

            case MsgType.Kick:
            {
                // Chỉ Host được kick; không được kick Host
                if (c.RoomId is null) break;
                var room = _rooms.Get(c.RoomId);
                if (room is null) break;
                if (!c.IsHost) { await SendAsync(c, MsgType.Info, Packet.Str("NOT_HOST")); break; }
                var targetUser = Packet.Str(payload);
                var target = room.Members.FirstOrDefault(m => m.Username == targetUser);
                if (target != null && target != room.Host)
                {
                    await SendAsync(target, MsgType.Info, Packet.Str("KICKED"));
                    target.Tcp.Close();
                    room.Remove(target.Username);
                    await BroadcastParticipants(room);
                }
                break;
            }
            case MsgType.Leave:
            {
                // Client chủ động rời phòng
                await OnDisconnectAsync(c);
                break;
            }
        }
    }

    // Gửi payload tới tất cả thành viên trong phòng (trừ người gửi)
    private async Task BroadcastRoom(Room room, ClientConn from, MsgType type, byte[] payload)
    {
        var pkt = Packet.Make(type, payload);
        foreach (var m in room.Members)
        {
            if (m == from) continue;
            try { await m.Stream.WriteAsync(pkt); } catch { }
        }
    }

    // Gửi danh sách người tham gia (và trạng thái host/cam/mic) cho cả phòng
    private async Task BroadcastParticipants(Room room)
    {
        var list = room.Members
            .Select(m => $"{m.Username}:{(m.IsHost?1:0)}:{(m.CamOn?1:0)}:{(m.MicOn?1:0)}")
            .ToArray();
        var payload = Packet.Str(string.Join(";", list));
        var pkt = Packet.Make(MsgType.Participants, payload);
        foreach (var m in room.Members)
            try { await m.Stream.WriteAsync(pkt); } catch { }
    }

    // Gửi 1 gói tin đơn lẻ tới client
    private async Task SendAsync(ClientConn c, MsgType t, byte[] p)
    {
        var pkt = Packet.Make(t, p);
        try { await c.Stream.WriteAsync(pkt); } catch { }
    }

    // Dọn dẹp khi client rời phòng/ngắt kết nối
    private async Task OnDisconnectAsync(ClientConn c)
    {
        if (c.RoomId != null)
        {
            var room = _rooms.Get(c.RoomId);
            if (room != null)
            {
                room.Remove(c.Username);
                await BroadcastParticipants(room);
                if (!room.Members.Any())
                    _rooms.CleanupIfEmpty(room.Id);
            }
        }
    }
}
