using System;
using System.Collections.Generic;

namespace ConsistentHashing;

public enum ServerState
{
    Available,
    TemporarilyOffline,
    Down
}

public class CacheServer
{
    public string Name { get; }
    public ServerState State { get; private set; }
    public HashSet<string> ClientIds { get; }

    public CacheServer(string name)
    {
        Name = name;
        State = ServerState.Available;
        ClientIds = new HashSet<string>();
    }

    public void AddClientId(string clientId)
    {
        if (State == ServerState.Available)
        {
            ClientIds.Add(clientId);
        }
        else
        {
            throw new InvalidOperationException($"Cannot add client to server in state: {State}");
        }
    }

    public void RemoveClientId(string clientId)
    {
        ClientIds.Remove(clientId);
    }

    public void MarkTemporarilyOffline()
    {
        State = ServerState.TemporarilyOffline;
    }

    public void MarkDown()
    {
        State = ServerState.Down;
        ClientIds.Clear();
    }

    public void BringUp()
    {
        State = ServerState.Available;
    }

    public bool IsAvailable => State == ServerState.Available;
    public bool IsDown => State == ServerState.Down;
    public bool IsTemporarilyOffline => State == ServerState.TemporarilyOffline;
} 