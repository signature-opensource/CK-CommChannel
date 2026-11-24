Stable communication channels on System.IO.Pipelines: a CommunicationChannel that can be disconnected, reconnected or reconfigured without disposing its reader and writer, with TCP and in-memory implementations.

Main types: CommunicationChannel, CommunicationChannelConfiguration (TcpChannelConfiguration, MemoryChannelConfiguration), StablePipeReader, StablePipeWriter, and the message readers, writers and handlers that parse and write messages with timeouts and cancellation.
