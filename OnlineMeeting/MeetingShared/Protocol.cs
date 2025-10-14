using System.Buffers.Binary;
using System.Text;

namespace MeetingShared;

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
    public static byte[] Make(MsgType type, byte[] payload)
    {
        payload ??= Array.Empty<byte>();
        var buf = new byte[1 + 4 + payload.Length];
        buf[0] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(1,4), payload.Length);
        payload.CopyTo(buf, 5);
        return buf;
    }

    public static bool TryParse(ref MemoryStream recvBuf, out MsgType type, out byte[] payload)
    {
        type = 0; payload = Array.Empty<byte>();
        if (recvBuf.Length < 5) return false;
        var span = recvBuf.ToArray().AsSpan();
        type = (MsgType)span[0];
        int len = BinaryPrimitives.ReadInt32BigEndian(span.Slice(1,4));
        if (span.Length < 5 + len) return false;
        payload = span.Slice(5, len).ToArray();
        var remaining = span.Slice(5 + len).ToArray();
        recvBuf.SetLength(0);
        recvBuf.Position = 0;
        recvBuf.Write(remaining);
        recvBuf.Position = 0;
        return true;
    }

    public static byte[] Str(string s) => Encoding.UTF8.GetBytes(s);
    public static string Str(byte[] b) => Encoding.UTF8.GetString(b);
}
