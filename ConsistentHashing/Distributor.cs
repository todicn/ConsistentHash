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
        private const int POSITION_SCALE = 1000000; // Use 6 decimal places of precision
        private const int HASH_BITS = 160; // SHA1 produces 160 bits

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
            
            // Add virtual nodes with direct hash values
            var baseValue = BigInteger.Parse(server.Name, System.Globalization.NumberStyles.HexNumber);
            Console.WriteLine($"Base server value: {baseValue.ToString("X40")}");
            
            // Calculate range for random offsets (1/8th of hash space)
            var range = BigInteger.Parse("2000000000000000000000000000000000000000", System.Globalization.NumberStyles.HexNumber);
            var random = new Random();
            
            for (int i = 0; i < _virtualNodeCount; i++)
            {
                // Generate random bytes for the offset
                byte[] bytes = new byte[20]; // 160 bits = 40 hex chars
                random.NextBytes(bytes);
                var randomBig = new BigInteger(bytes);
                if (randomBig < 0) randomBig = -randomBig; // Ensure positive
                
                // Scale the random number to our desired range
                var offset = (randomBig * range) / MAX_HASH;
                Console.WriteLine($"Random offset {i}: {offset.ToString("X40")}");
                
                // Add offset to base value and handle wrapping
                var hashValue = baseValue + offset;
                if (hashValue >= MAX_HASH)
                {
                    hashValue -= MAX_HASH;
                }
                Console.WriteLine($"Hash value {i}: {hashValue.ToString("X40")}");
                
                string hash = hashValue.ToString("X40").PadLeft(40, '0');
                Console.WriteLine($"Final hash {i}: {hash}");
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
                server.AddClientId(hash);
                _noSqlTable.SaveMapping(hash, serverName);
            }
        }

        public void AddClientIdAtPosition(string clientId, double position)
        {
            if (_servers.Count == 0)
                return;

            Console.WriteLine($"Adding client at position: {position}");
            
            // Convert position (0-1) to a hash value by calculating each hex digit
            var hexDigits = "0123456789ABCDEF";
            var hashBuilder = new System.Text.StringBuilder();
            
            // Adjust position to match the circle's coordinate system (0 at top, clockwise)
            // No adjustment needed now since our coordinate systems match
            double currentPosition = position;
            for (int i = 0; i < 40; i++)
            {
                // Scale current position to 0-16 range
                currentPosition *= 16;
                // Get the integer part as hex digit
                int digit = (int)currentPosition;
                hashBuilder.Append(hexDigits[digit]);
                // Keep the fractional part for next iteration
                currentPosition -= digit;
            }
            
            var hash = hashBuilder.ToString();
            Console.WriteLine($"Generated hash: {hash}");
            string serverName = FindServerForHash(hash);
            
            if (string.IsNullOrEmpty(serverName))
                return;

            var server = _servers[serverName];
            if (!server.IsDown)
            {
                server.AddClientId(hash);
                _noSqlTable.SaveMapping(hash, serverName);
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
            // Take first 8 hex digits (32 bits)
            string truncatedHash = hexHash.Length > 8 ? hexHash.Substring(0, 8) : hexHash.PadLeft(8, '0');
            uint value = uint.Parse(truncatedHash, System.Globalization.NumberStyles.HexNumber);
            Console.WriteLine($"Hash: {hexHash}, Truncated: {truncatedHash}, Value: {value}");
            
            // Convert to position between 0 and 1
            double position = (double)value / uint.MaxValue;
            Console.WriteLine($"Position: {position}");
            return position;
        }

        public IEnumerable<VirtualNodeInfo> GetVirtualNodePositions()
        {
            var positions = _virtualNodes
                .Select(kvp => {
                    var position = HashToPosition(kvp.Key);
                    return new VirtualNodeInfo(
                        kvp.Key,
                        position,
                        _servers.TryGetValue(kvp.Value, out var server) && server.IsDown
                    );
                })
                .OrderBy(n => n.Position);
            return positions;
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
            if (string.IsNullOrEmpty(clientId))
                return;

            int retryCount = 0;
            string hash = ComputeHash(clientId);

            while (retryCount < MAX_RETRY)
            {
                string serverName = FindServerForHash(hash);
                if (string.IsNullOrEmpty(serverName) || !_servers.ContainsKey(serverName))
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

            return _virtualNodes.TryGetValue(serverHash ?? string.Empty, out var serverName) ? serverName : string.Empty;
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