using System;
using System.Collections.Generic;
using ConsistentHashing;
using ConsistentHashing.Models;

namespace ConsistentHashingWeb.Services
{
    public class CacheSystemService
    {
        private readonly Distributor _distributor;
        private readonly MockNoSqlTable _noSqlTable;
        private const int VIRTUAL_NODE_COUNT = 2;

        public event Action? OnChange;

        public CacheSystemService()
        {
            _noSqlTable = new MockNoSqlTable();
            _distributor = new Distributor(_noSqlTable, VIRTUAL_NODE_COUNT);
        }

        public IEnumerable<CacheServer> GetAllServers()
        {
            return _distributor.GetAllServers();
        }

        public void AddServer(string serverName)
        {
            var server = new CacheServer(serverName);
            _distributor.AddServer(server);
            NotifyStateChanged();
        }

        public void RemoveServer(string serverName)
        {
            var server = _distributor.GetAllServers().FirstOrDefault(s => s.Name == serverName);
            if (server != null)
            {
                _distributor.RemoveServer(serverName);
                NotifyStateChanged();
            }
        }

        public void ToggleServer(string serverName)
        {
            _distributor.ToggleServer(serverName);
            NotifyStateChanged();
        }

        public void AddClientId(string clientId)
        {
            _distributor.AddClientId(clientId);
            NotifyStateChanged();
        }

        public Dictionary<string, string> GetClientServerMappings()
        {
            var mappings = new Dictionary<string, string>();
            foreach (var server in _distributor.GetAllServers())
            {
                foreach (var clientId in server.ClientIds)
                {
                    mappings[clientId] = server.Name;
                }
            }
            return mappings;
        }

        public IEnumerable<VirtualNodeInfo> GetVirtualNodePositions()
        {
            return _distributor.GetVirtualNodePositions();
        }

        public IEnumerable<ClientNodeInfo> GetClientPositions()
        {
            return _distributor.GetClientPositions();
        }

            private void NotifyStateChanged() => OnChange?.Invoke();
}
} 