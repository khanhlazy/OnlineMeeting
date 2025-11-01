using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MeetingShared;

// Kiểu thông điệp trao đổi giữa Client và Server qua TCP.
// Mỗi gói tin có header gồm 1 byte (MsgType) + 4 byte (độ dài payload Big Endian), theo sau là payload.
public enum MsgType : byte
{
    Register = 1,
    Login = 2,
    CreateRoom = 3,
    JoinRoom = 4,
    Chat = 5,
    Video = 6,
    Audio = 7,
    Kick = 8,
    Leave = 9,
    Info = 10,
    Participants = 11,
    ToggleCam = 12,
    ToggleMic = 13
}

public static class Packet
{
    public const int HeaderSize = 5;
    // Giới hạn payload 1 MB để tránh client/server bị DoS bởi gói quá lớn
    public const int MaxPayloadLength = 1024 * 1024;

    // Đóng gói: [1 byte type][4 byte length BE][payload]
    public static byte[] Make(MsgType type, byte[] payload)
    {
        payload ??= Array.Empty<byte>();
        if (payload.Length > MaxPayloadLength)
            throw new InvalidDataException($"Payload vượt quá giới hạn {MaxPayloadLength} bytes");

        var buf = new byte[HeaderSize + payload.Length];
        buf[0] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(1, 4), payload.Length);
        payload.CopyTo(buf, HeaderSize);
        return buf;
    }

    // Giải gói từ buffer nhận. Nếu chưa đủ dữ liệu sẽ trả về false và chờ thêm.
    public static bool TryParse(ref MemoryStream recvBuf, out MsgType type, out byte[] payload)
    {
        type = default;
        payload = Array.Empty<byte>();

        if (recvBuf.Length < HeaderSize)
            return false;

        if (!recvBuf.TryGetBuffer(out ArraySegment<byte> segment))
        {
            segment = new ArraySegment<byte>(recvBuf.ToArray());
        }

        if (segment.Array is null)
            return false;

        var span = segment.Array.AsSpan(segment.Offset, segment.Count);
        var msgType = (MsgType)span[0];
        int len = BinaryPrimitives.ReadInt32BigEndian(span.Slice(1, 4));

        if (len < 0 || len > MaxPayloadLength)
            throw new InvalidDataException($"Độ dài payload không hợp lệ: {len}");

        if (span.Length < HeaderSize + len)
            return false;

        payload = span.Slice(HeaderSize, len).ToArray();

        var remainingCount = span.Length - HeaderSize - len;
        recvBuf.SetLength(0);
        if (remainingCount > 0)
        {
            recvBuf.Write(span.Slice(HeaderSize + len, remainingCount));
        }
        recvBuf.Position = 0;
        type = msgType;
        return true;
    }

    // Tiện ích chuyển đổi chuỗi <-> bytes UTF8 cho payload text
    public static byte[] Str(string s) => Encoding.UTF8.GetBytes(s);
    public static string Str(byte[] b) => Encoding.UTF8.GetString(b);
}
