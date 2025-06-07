# Consistent Hashing Implementation

A C# implementation of Consistent Hashing with a Blazor WebAssembly visualization interface.

## Features

- Implementation of Consistent Hashing algorithm with virtual nodes
- Visual representation of the hash ring
- Interactive web interface for:
  - Adding/removing servers
  - Adding client IDs
  - Toggling server status (up/down)
  - Visualizing client distribution
- Automatic client redistribution on server failure
- Configurable number of virtual nodes per server

## Project Structure

- `ConsistentHashing/`: Core library implementing the consistent hashing algorithm
  - `CacheServer.cs`: Server implementation
  - `Distributor.cs`: Main consistent hashing implementation
  - `MockNoSqlTable.cs`: Mock storage for client-server mappings
  - `Models/`: Shared data models

- `ConsistentHashingWeb/`: Blazor WebAssembly UI
  - `Pages/`: Blazor pages including the main interface
  - `Services/`: Services for interfacing with the core library
  - `Layout/`: UI layout components

## Getting Started

1. Prerequisites:
   - .NET 8.0 SDK
   - A modern web browser

2. Running the application:
   ```bash
   dotnet run --project ConsistentHashingWeb
   ```

3. Navigate to `http://localhost:5192` in your web browser

## Implementation Details

- Uses SHA-1 for consistent hashing
- Each server has configurable number of virtual nodes (default: 2)
- Handles server failures with configurable retry attempts
- Provides real-time visualization of the hash ring

## License

MIT 