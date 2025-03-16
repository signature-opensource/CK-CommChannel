# CK-CommChannel

Provides
- A [CommunicationChannel](CK.CommChannel/CommunicationChannel.cs) abstraction using [System.IO.Pipelines types](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines) around input/output byte streams to streamline (no pun intended) code reuse between different means of communication (eg. a network stream from a TcpClient/TcpListener or a stream from a SerialPort), or even use an in-memory stream (for eg. tests or monitoring),
- CommunicationChannel implementations for TCP, SerialPort, and in-memory pipes or NetworkStreams,
- `MessageReader`/`MessageWriter`/`MessageHandler` types that can be plugged on a channel's Reader/Writer to parse and 
   write messages, and provide features such as automatic time-out, message reconstruction and cancellation.

A [CommunicationChannel](CK.CommChannel/CommunicationChannel.cs) is a high level API that
exposes stable `StablePipeReader Reader { get; }` and a `StablePipeWriter Writer { get; }` properties.

"Stable" means that a `CommunicationChannel` instance can be temporarily disconnected, reconnected or
completely reconfigured under the hood without dropping/disposing any object.

Its implementation relies on [`StablePipeReader and Writer`](CK.CommChannel/StablePipe/README.md).

To read incoming messages, a [MessageReaders](CK.CommChannel/MessageReader/README.md)
should be used on a `PipeReader` and to write outgoing messages, [MessageWriters](CK.CommChannel/MessageReader/README.md)
should be used on a `PipeWriter`.

Both of them correctly handle timeouts and cancellations and minimize allocations as much as possible.

`MessageReader` offers a _pull_ model (messages are read one after the other). A generic _push_ adapter is
available thanks to [MessageHandler](CK.CommChannel/MessageHandler/README.md).

# Getting started

## NuGet packages

The `CK.CommChannel` package provides both abstractions (ie. `ICommunicationChannel`, `CommunicationChannel`), and implementations for TCP channels (`TcpChannel`) and in-memory channels (`MemoryChannel`)

```ps1
Install-Package CK.CommChannel
```

The implementation for serial ports is provided by `CK.CommChannel.Serial`.

```ps1
Install-Package CK.CommChannel.Serial
```

## Creating a channel

Channels are created using a configuration type, and the static method at `CommunicationChannel.OpenAsync()`.

### Connecting to a TCP Server

```csharp
var monitor = new ActivityMonitor();
var config = new TcpChannelConfiguration()
{
    Host = "127.0.0.1", // Destination hostname or IP address
    Port = 2101, // Destination TCP port
    AutoReconnect = true
};
using ICommunicationChannel channel = await CommunicationChannel.OpenAsync( monitor, config );
```

### Connecting to a serial port (needs `CK.CommChannel.Serial` package)

```csharp
var config = new SerialChannelConfiguration()
{
    PortName = "COM4", // The destination COM port. COM# on Windows, or eg. /dev/ttyUSB0 on Linux.
    BaudRate = 9600,
    Parity = Parity.None,
    AutoReconnect = true
};
using ICommunicationChannel channel = await CommunicationChannel.OpenAsync( monitor, config );
```

### Creating an in-memory channel

```csharp
// The IPipeChannel has a PipeWriter Input and a PipeReader Output that are bound to the
// future channel. This one uses the `Pipe` implementation:
IPipeChannel mem = MemoryChannel.AllocatePipeChannel( "TestMe!" );

// This one uses 4 NetworkStream and a TCPListener on a dynamic port.
// This enables to easily test behaviors (exceptions, read, flush) specific
// to network communications.
IPipeChannel viaNetwork = await MemoryChannel.AllocateNetworkStreamChannelAsync( "TestMe!" );

var config = new MemoryChannelConfiguration() { EndPointName = "TestMe!" };
ICommunicationChannel channel = await CommunicationChannel.OpenAsync( monitor, config );

channel.Dispose();
// Once done, the memory channel must be released.
await MemoryChannel.DeallocateAsync( "Test" );
```

`IPipeChannel` exposes a `PipeReader` and `PipeWriter`. This is not really easy to work with them.
Thanks to a `bool Revert` on the MemoryChannelConfiguration, there is an easy way to establish
a dialog between 2 parties as they can be "symmetric", both working with a CommunicationChannel.

```csharp
await MemoryChannel.AllocateNetworkStreamChannelAsync( "Test" );

MemoryChannelConfiguration config1 = new MemoryChannelConfiguration
{
  EndPointName = "Test",
  AutoReconnect = true
};
var channel1 = await CommunicationChannel.OpenAsync( TestHelper.Monitor, config1 );

MemoryChannelConfiguration config2 = new MemoryChannelConfiguration
{
  EndPointName = "Test",
  AutoReconnect = true,
  Reverted = true
};
var channel2 = await CommunicationChannel.OpenAsync( TestHelper.Monitor, config2 );

// The 2 CommunicationChannels can then write/read bytes (or better: messages) to/from each other.

channel1.Dispose();
channel2.Dispose();
await MemoryChannel.DeallocateAsync( "Test" );
```

### Sending and receiving data

All channel types expose System.IO.Pipelines types in the `Reader` and `Writer` properties, which are respectively used to receive and send data.

```csharp
// Receiving data
ReadResult r = await channel.Reader.ReadAsync();
Console.WriteLine( $"Received data: {r.Buffer.Length} bytes" );
channel.Reader.AdvanceTo( r.Buffer.End );

// Sending data
byte[] helloBytes = Encoding.ASCII.GetBytes("Hello");
await channel.Writer.WriteAsync( helloBytes );
```

#### Careful with System.IO.Pipelines

[For more information about how to use PipeReader and PipeWriter, read the docs!](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines)

When using PipeReader and PipeWriter, including as a consumer of this library, you should know [the common pitfalls of PipeReader](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines#pipereader-common-problems) caused by problematic code, including:
- Data loss
- Infinite loops
- Unresponsive application
- Out of Memory (OOM)
- Memory corruption

You should also know the [common pitfalls of PipeWriter](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines#pipewriter-common-problems),
and [general tips when using them](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines#tips-for-using-pipereader-and-pipewriter).

> Always use `MessageReader`/`MessageWriter`/`MessageHandler` that are safe and efficient.
> If you think that you need to use the PipeReader/Writer directly, please ask us before!

### Reconfiguring a channel

A channel can be reconfigured to change its properties, and can even **change its communication type**.

```csharp
var tcpConfig = new TcpChannelConfiguration() { Host = "localhost", Port = 2101 };
using ICommunicationChannel channel = await CommunicationChannel.OpenAsync( monitor, tcpConfig );

tcpConfig.Port = 8080;
await channel.ReconfigureAsync( monitor, tcpConfig );

var serialConfig = new SerialChannelConfiguration() { PortName = "COM3" };
await channel.ReconfigureAsync( monitor, serialConfig );

var memoryConfig = new MemoryChannelConfiguration();
await channel.ReconfigureAsync( monitor, memoryConfig );
```

Note that channels do not expose their underlying type or configuration and this is intended.



