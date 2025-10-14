using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;

namespace MeetingServer;

public class ClientConn
{
    public TcpClient Tcp { get; }
    public NetworkStream Stream => Tcp.GetStream();
    public string Username { get; set; } = "";
    public string? RoomId { get; set; }
    public bool IsHost { get; set; }
    public bool CamOn { get; set; }
    public bool MicOn { get; set; }
    public ClientConn(TcpClient tcp) => Tcp = tcp;
}

public class Room
{
    public string Id { get; }
    public ClientConn Host { get; }
    private readonly ConcurrentDictionary<string, ClientConn> _members = new();

    public Room(string id, ClientConn host)
    {
        Id = id; Host = host;
        host.IsHost = true;
        _members[host.Username] = host;
    }

    public IEnumerable<ClientConn> Members => _members.Values;

    public void Add(ClientConn c) => _members[c.Username] = c;
    public void Remove(string username) => _members.TryRemove(username, out _);
}

public class RoomManager
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly Random _rnd = new();

    public Room CreateRoom(ClientConn host)
    {
        string id;
        do { id = $"R{_rnd.Next(100000,999999)}"; } while (_rooms.ContainsKey(id));
        var room = new Room(id, host);
        host.RoomId = id;
        _rooms[id] = room;
        return room;
    }

    public Room? Get(string id) => _rooms.TryGetValue(id, out var r) ? r : null;
    public void CleanupIfEmpty(string id)
    {
        if (_rooms.TryGetValue(id, out var r) && !r.Members.Any())
            _rooms.TryRemove(id, out _);
    }
}
