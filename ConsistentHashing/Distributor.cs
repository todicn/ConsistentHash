using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Numerics;
using ConsistentHashing.Models;

namespace ConsistentHashing
{
    public class Distributor
    {
        private readonly Dictionary<string, CacheServer> _servers;
        private readonly Dictionary<string, string> _virtualNodes; // hash -> server name
        private readonly MockNoSqlTable _noSqlTable;
        private readonly int _virtualNodeCount;
        private const int MAX_RETRY = 3;
        private static readonly BigInteger MAX_HASH = BigInteger.Parse("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", System.Globalization.NumberStyles.HexNumber);

        public Distributor(MockNoSqlTable noSqlTable, int virtualNodeCount)
        {
            _servers = new Dictionary<string, CacheServer>();
            _virtualNodes = new Dictionary<string, string>();
            _noSqlTable = noSqlTable;
            _virtualNodeCount = virtualNodeCount;
        }

        public void AddServer(CacheServer server)
        {
            if (_servers.ContainsKey(server.Name))
                return;

            _servers[server.Name] = server;
            
            // Add virtual nodes
            for (int i = 0; i < _virtualNodeCount; i++)
            {
                string virtualNodeName = $"{server.Name}-{i}";
                string hash = ComputeHash(virtualNodeName);
                _virtualNodes[hash] = server.Name;
            }

            // Redistribute clients if needed
            RedistributeClients();
        }

        public void RemoveServer(string serverName)
        {
            if (!_servers.ContainsKey(serverName))
                return;

            // Remove virtual nodes
            var virtualNodesToRemove = _virtualNodes
                .Where(kvp => kvp.Value == serverName)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var hash in virtualNodesToRemove)
            {
                _virtualNodes.Remove(hash);
            }

            _servers.Remove(serverName);
            RedistributeClients();
        }

        public void AddClientId(string clientId)
        {
            if (_servers.Count == 0)
                return;

            string hash = ComputeHash(clientId);
            string serverName = FindServerForHash(hash);
            
            if (string.IsNullOrEmpty(serverName))
                return;

            var server = _servers[serverName];
            if (!server.IsDown)
            {
                server.AddClientId(clientId);
                _noSqlTable.SaveMapping(clientId, serverName);
            }
        }

        public void ToggleServer(string serverName)
        {
            if (!_servers.ContainsKey(serverName))
                return;

            var server = _servers[serverName];
            if (server.IsDown)
            {
                server.BringUp();
                RedistributeClients();
            }
            else
            {
                var clientIds = new List<string>(server.ClientIds);
                server.Shutdown();
                foreach (var clientId in clientIds)
                {
                    RedistributeClient(clientId);
                }
            }
        }

        public IEnumerable<VirtualNodeInfo> GetVirtualNodePositions()
        {
            return _virtualNodes
                .Select(kvp => new
                {
                    Hash = kvp.Key,
                    ServerName = kvp.Value,
                    Position = BigInteger.Parse(kvp.Key, System.Globalization.NumberStyles.HexNumber)
                })
                .Select(n => new VirtualNodeInfo(
                    $"{n.ServerName}-{n.Hash[..4]}",
                    (double)n.Position / (double)MAX_HASH,
                    _servers[n.ServerName].IsDown
                ))
                .OrderBy(n => n.Position);
        }

        public IEnumerable<ClientNodeInfo> GetClientPositions()
        {
            return _servers.Values
                .SelectMany(s => s.ClientIds)
                .Select(clientId =>
                {
                    var hash = ComputeHash(clientId);
                    var position = BigInteger.Parse(hash, System.Globalization.NumberStyles.HexNumber);
                    return new ClientNodeInfo(
                        clientId,
                        (double)position / (double)MAX_HASH
                    );
                })
                .OrderBy(n => n.Position);
        }

        private void RedistributeClients()
        {
            var allClientIds = _servers.Values
                .SelectMany(s => s.ClientIds)
                .Distinct()
                .ToList();

            foreach (var server in _servers.Values)
            {
                server.ClientIds.Clear();
            }

            foreach (var clientId in allClientIds)
            {
                RedistributeClient(clientId);
            }
        }

        private void RedistributeClient(string clientId)
        {
            int retryCount = 0;
            string hash = ComputeHash(clientId);

            while (retryCount < MAX_RETRY)
            {
                string serverName = FindServerForHash(hash);
                if (string.IsNullOrEmpty(serverName))
                    return;

                var server = _servers[serverName];
                if (!server.IsDown)
                {
                    server.AddClientId(clientId);
                    _noSqlTable.SaveMapping(clientId, serverName);
                    return;
                }

                retryCount++;
                hash = ComputeHash($"{hash}-retry{retryCount}");
            }
        }

        private string FindServerForHash(string hash)
        {
            if (_virtualNodes.Count == 0)
                return string.Empty;

            var serverHash = _virtualNodes.Keys
                .Where(k => string.Compare(k, hash, StringComparison.Ordinal) >= 0)
                .MinBy(k => k);

            if (serverHash == null)
            {
                // Wrap around to the first server if we're past the last one
                serverHash = _virtualNodes.Keys.Min();
            }

            return _virtualNodes[serverHash];
        }

        private static string ComputeHash(string input)
        {
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hash).Replace("-", "");
        }

        public IEnumerable<CacheServer> GetAllServers() => _servers.Values;
    }
} 