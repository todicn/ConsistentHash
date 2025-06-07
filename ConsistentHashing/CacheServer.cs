using System;
using System.Collections.Generic;

namespace ConsistentHashing;

public class CacheServer
{
    public string Name { get; }
    public bool IsDown { get; private set; }
    public HashSet<string> ClientIds { get; }

    public CacheServer(string name)
    {
        Name = name;
        IsDown = false;
        ClientIds = new HashSet<string>();
    }

    public void AddClientId(string clientId)
    {
        if (!IsDown)
        {
            ClientIds.Add(clientId);
        }
    }

    public void RemoveClientId(string clientId)
    {
        ClientIds.Remove(clientId);
    }

    public void Shutdown()
    {
        IsDown = true;
        ClientIds.Clear();
    }

    public void BringUp()
    {
        IsDown = false;
    }
} 