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
        private readonly Dictionary<string, int> _serverFailureCount;
        private static readonly BigInteger MAX_HASH = BigInteger.Parse("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", System.Globalization.NumberStyles.HexNumber);
        private const int POSITION_SCALE = 1000000; // Use 6 decimal places of precision
        private const int HASH_BITS = 160; // SHA1 produces 160 bits

        public Distributor(MockNoSqlTable noSqlTable, int virtualNodeCount)
        {
            _servers = new Dictionary<string, CacheServer>();
            _virtualNodes = new Dictionary<string, string>();
            _noSqlTable = noSqlTable;
            _virtualNodeCount = virtualNodeCount;
            _serverFailureCount = new Dictionary<string, int>();
        }

        public void AddServer(CacheServer server)
        {
            if (_servers.ContainsKey(server.Name))
                return;

            _servers[server.Name] = server;
            _serverFailureCount[server.Name] = 0;
            
            // Add virtual nodes
            var random = new Random();
            for (int i = 0; i < _virtualNodeCount; i++)
            {
                // Generate random position between 0 and 1
                double position = random.NextDouble();
                
                // Convert position to hex hash (8 significant digits + padding zeros)
                uint value = (uint)(position * uint.MaxValue);
                // Prefix with first digit of server name and underscore
                string prefix = server.Name.Length > 0 ? server.Name[0] + "_" : "0_";
                string hash = prefix + value.ToString("X8").PadLeft(8, '0') + new string('0', 30);
                
                Console.WriteLine($"Virtual node {i} for {server.Name}: pos={position:F6}, hash={hash}");
                _virtualNodes[hash] = server.Name;
            }
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
        }

        public void AddClientId(string clientId)
        {
            if (_servers.Count == 0)
                return;

            // Generate random position for client
            var random = new Random();
            double position = random.NextDouble();
            uint value = (uint)(position * uint.MaxValue);
            string hash = value.ToString("X8").PadLeft(8, '0');
            
            Console.WriteLine($"Adding client {clientId} at position {position:F6}, hash={hash}");
            AssignClient(hash);
        }

        public void AddClientIdAtPosition(string clientId, double position)
        {
            if (_servers.Count == 0)
                return;

            Console.WriteLine($"Adding client {clientId} at exact position {position:F6}");
            
            // Convert position to hex hash (8 significant digits + padding zeros)
            uint value = (uint)(position * uint.MaxValue);
            string hash = value.ToString("X8").PadLeft(8, '0') + new string('0', 32);
            
            Console.WriteLine($"Generated hash for position {position:F6}: {hash}");
            
            AssignClient(hash);
        }

        private void AssignClient(string clientHash, HashSet<string> triedServers = null)
        {
            if (_servers.Count == 0)
                return;

            // Initialize triedServers if this is the first attempt
            triedServers = triedServers ?? new HashSet<string>();

            // Get position of the client
            double clientPos = HashToPosition(clientHash);
            
            // Get all nodes (both physical and virtual)
            var allNodes = new List<NodeInfo>();
            
            // Add physical server nodes
            foreach (var physicalServerName in _servers.Keys)
            {
                // Use server name as hash for physical nodes
                allNodes.Add(new NodeInfo(physicalServerName, physicalServerName, HashToPosition(physicalServerName)));
            }
            
            // Add virtual nodes
            foreach (var kvp in _virtualNodes)
            {
                allNodes.Add(new NodeInfo(kvp.Key, kvp.Value, HashToPosition(kvp.Key)));
            }

            // Order all nodes by position
            var orderedNodes = allNodes
                .OrderBy(n => n.Position)
                .ToList();

            // Find all nodes we haven't tried yet
            var availableNodes = orderedNodes
                .Where(n => !triedServers.Contains(n.ServerName))
                .ToList();

            if (!availableNodes.Any())
            {
                Console.WriteLine($"No available servers to assign client {clientHash}");
                return;
            }

            // Find the first node with position > client position
            var nextNode = availableNodes.FirstOrDefault(n => n.Position > clientPos);
            
            // If no node found after client, wrap around to first node
            if (nextNode == null)
            {
                nextNode = availableNodes.First();
            }

            string serverName = nextNode.ServerName;
            Console.WriteLine($"Attempting to assign client at position {clientPos:F6} to server {serverName} at position {nextNode.Position:F6} (node hash: {nextNode.Hash})");

            // Mark this server as tried
            triedServers.Add(serverName);

            // Assign to server if it's up and hasn't exceeded retry count
            if (_servers.TryGetValue(serverName, out var server))
            {
                // Always ensure the server has a failure count entry
                if (!_serverFailureCount.ContainsKey(serverName))
                {
                    _serverFailureCount[serverName] = 0;
                }

                Console.WriteLine($"Server {serverName} status - State: {server.State}, FailureCount: {_serverFailureCount[serverName]}");

                if (!server.IsAvailable)
                {
                    _serverFailureCount[serverName]++;
                    Console.WriteLine($"Server {serverName} unavailable. Failure count: {_serverFailureCount[serverName]}");
                    
                    // If failure count hits MAX_RETRY, mark server as permanently down
                    if (_serverFailureCount[serverName] >= MAX_RETRY)
                    {
                        Console.WriteLine($"Server {serverName} hit max failures ({MAX_RETRY}), marking as permanently down");
                        server.MarkDown();
                    }
                    
                    // Try next server
                    Console.WriteLine($"Trying next server as current server {serverName} is unavailable");
                    AssignClient(clientHash, triedServers);
                    return;
                }

                try 
                {
                    Console.WriteLine($"Attempting to write to server {serverName}");
                    server.AddClientId(clientHash);
                    _noSqlTable.SaveMapping(clientHash, serverName);
                    Console.WriteLine($"Assignment successful to server {serverName}");
                    // Reset failure count on successful write
                    _serverFailureCount[serverName] = 0;
                }
                catch (Exception ex)
                {
                    _serverFailureCount[serverName]++;
                    Console.WriteLine($"Failed to write to server {serverName}. Failure count: {_serverFailureCount[serverName]}, Error: {ex.Message}");
                
                    if (_serverFailureCount[serverName] >= MAX_RETRY)
                    {
                        Console.WriteLine($"Server {serverName} reached max retries ({MAX_RETRY}), marking as down");
                        server.MarkDown();
                        return;
                    }
                    
                    // Try next server if we haven't hit MAX_RETRY yet
                    if (_serverFailureCount[serverName] < MAX_RETRY)
                    {
                        Console.WriteLine($"Trying next server as we haven't hit max retries yet");
                        AssignClient(clientHash, triedServers);
                    }
                    else
                    {
                        Console.WriteLine($"Not trying next server as we've hit max retries");
                    }
                }
            }
        }

        public void ToggleServer(string serverName)
        {
            if (!_servers.ContainsKey(serverName))
                return;

            var server = _servers[serverName];
            if (server.IsAvailable)
            {
                Console.WriteLine($"Manually marking server {serverName} as temporarily offline");
                server.MarkTemporarilyOffline();
            }
            else if (server.IsTemporarilyOffline)
            {
                Console.WriteLine($"Bringing server {serverName} back online");
                ReviveServer(serverName);
            }
        }

        public void BringUpServer(string serverName)
        {
            ReviveServer(serverName);
        }

        public int GetServerFailureCount(string serverName)
        {
            return _serverFailureCount.TryGetValue(serverName, out var count) ? count : 0;
        }

        private void ReviveServer(string serverName)
        {
            if (!_servers.ContainsKey(serverName))
                return;

            var server = _servers[serverName];
            if (!server.IsAvailable)
            {
                // Reset failure count and bring server back online
                _serverFailureCount[serverName] = 0;
                server.BringUp();
                Console.WriteLine($"Server {serverName} brought back online");
            }
        }

        private BigInteger ParseHashToPositiveBigInt(string hexHash)
        {
            // Ensure we parse as unsigned by adding a leading 0 if needed
            if (hexHash.Length % 2 == 1)
            {
                hexHash = "0" + hexHash;
            }
            
            byte[] bytes = new byte[hexHash.Length / 2 + 1];
            // Parse hex string to bytes
            for (int i = 0; i < hexHash.Length; i += 2)
            {
                bytes[i / 2] = byte.Parse(hexHash.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);
            }
            // Add an extra 0 byte at the end to ensure positive
            bytes[bytes.Length - 1] = 0;
            
            // Create BigInteger from bytes (will be positive due to extra 0 byte)
            return new BigInteger(bytes);
        }

        private double HashToPosition(string hexHash)
        {
            // Remove prefix if it exists (e.g., "0_" or "S_")
            string hashWithoutPrefix = hexHash;
            int underscoreIndex = hexHash.IndexOf('_');
            if (underscoreIndex >= 0 && underscoreIndex + 1 < hexHash.Length)
            {
                hashWithoutPrefix = hexHash.Substring(underscoreIndex + 1);
            }

            // Take first 8 hex digits (32 bits)
            string truncatedHash = hashWithoutPrefix.Length > 8 ? hashWithoutPrefix.Substring(0, 8) : hashWithoutPrefix.PadLeft(8, '0');
            Console.WriteLine($"Hash: {hexHash}, Without Prefix: {hashWithoutPrefix}, Truncated: {truncatedHash}");
            
            uint value = uint.Parse(truncatedHash, System.Globalization.NumberStyles.HexNumber);
            Console.WriteLine($"Value: {value}");
            
            // Convert to position between 0 and 1
            double position = (double)value / uint.MaxValue;
            Console.WriteLine($"Position: {position}");
            return position;
        }

        public IEnumerable<VirtualNodeInfo> GetVirtualNodePositions()
        {
            // First add the main server nodes
            var mainNodes = _servers.Keys
                .Select(serverName => new VirtualNodeInfo(
                    serverName,
                    HashToPosition(serverName),
                    _servers[serverName].IsDown
                ));

            // Then add the virtual nodes
            var virtualNodes = _virtualNodes
                .Select(kvp => new VirtualNodeInfo(
                    kvp.Key,
                    HashToPosition(kvp.Key),
                    _servers.TryGetValue(kvp.Value, out var server) && server.IsDown
                ));

            // Combine and order all nodes
            return mainNodes.Concat(virtualNodes).OrderBy(n => n.Position);
        }

        public IEnumerable<ClientNodeInfo> GetClientPositions()
        {
            return _servers.Values
                .SelectMany(s => s.ClientIds)
                .Select(clientId => {
                    var position = HashToPosition(clientId);
                    return new ClientNodeInfo(
                        clientId,
                        position
                    );
                })
                .OrderBy(n => n.Position);
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

            return _virtualNodes.TryGetValue(serverHash ?? string.Empty, out var serverName) ? serverName : string.Empty;
        }

        private static string ComputeHash(string input)
        {
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hash).Replace("-", "");
        }

        public IEnumerable<CacheServer> GetAllServers() => _servers.Values;

        private class NodeInfo
        {
            public string Hash { get; }
            public string ServerName { get; }
            public double Position { get; }

            public NodeInfo(string hash, string serverName, double position)
            {
                Hash = hash;
                ServerName = serverName;
                Position = position;
            }
        }
    }
} 