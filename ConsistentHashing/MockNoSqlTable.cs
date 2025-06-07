using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsistentHashing
{
    public class MockNoSqlTable
    {
        private readonly Dictionary<string, string> _clientServerMappings;

        public MockNoSqlTable()
        {
            _clientServerMappings = new Dictionary<string, string>();
        }

        public void SaveMapping(string clientId, string serverName)
        {
            _clientServerMappings[clientId] = serverName;
        }

        public string GetServerForClient(string clientId)
        {
            return _clientServerMappings.TryGetValue(clientId, out var serverName) ? serverName : string.Empty;
        }

        public IEnumerable<string> GetClientsForServer(string serverName)
        {
            return _clientServerMappings
                .Where(kvp => kvp.Value == serverName)
                .Select(kvp => kvp.Key);
        }

        public void RemoveMapping(string clientId)
        {
            _clientServerMappings.Remove(clientId);
        }

        public void RemoveServerMappings(string serverName)
        {
            var clientsToRemove = GetClientsForServer(serverName).ToList();
            foreach (var clientId in clientsToRemove)
            {
                _clientServerMappings.Remove(clientId);
            }
        }
    }
} 