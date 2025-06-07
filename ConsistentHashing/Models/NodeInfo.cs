namespace ConsistentHashing.Models;

public record VirtualNodeInfo(string Name, double Position, bool IsDown);
public record ClientNodeInfo(string Id, double Position); 