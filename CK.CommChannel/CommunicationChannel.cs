using CK.Core;
using CK.PerfectEvent;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

/// <summary>
/// A CommunicationChannel is stable.
/// <para>
/// Channels are created by the factory method <see cref="CommunicationChannel.Create"/>.
/// </para>
/// <para>
/// Please read https://docs.microsoft.com/en-us/dotnet/standard/io/pipelines.
/// </para>
/// </summary>
public sealed partial class CommunicationChannel
{
    /// <summary>
    /// See <see cref="CommunicationChannelStatusChangedEvent.ErrorContext"/>.
    /// </summary>
    public const int ErrorContextRunningCount = 5;

    /// <summary>
    /// See <see cref="CommunicationChannelStatusChangedEvent.ErrorContext"/>.
    /// </summary>
    public const int ErrorContextMaxCount = 20;

    CommunicationChannelConfiguration _config;

    readonly StablePipeReader _reader;
    readonly Behavior _readerBehavior;
    readonly StablePipeWriter _writer;
    readonly Behavior _writerBehavior;
    // We don't dispose this semaphore because
    // we don't use the WaitHandle.
    readonly SemaphoreSlim _semaphore;
    readonly string _name;
    readonly ActivityMonitor _monitor;
    readonly LogCatcher _logCatcher;
    readonly PerfectEventSender<CommunicationChannelStatusChangedEvent> _statusChanged;

    ChannelImpl _channel;
    Reconnector? _reconnector;
    int _disposed;
    ConnectionAvailability _lastSentConnectStatus;
    ConnectionAvailability _connectStatus;

    static int _nameCount = 1;

    /// <summary>
    /// Gets the reader.
    /// </summary>
    public StablePipeReader Reader => _reader;

    /// <summary>
    /// Gets the writer.
    /// </summary>
    public StablePipeWriter Writer => _writer;

    /// <summary>
    /// Gets or sets whether the channel must attempt to automatically reconnect
    /// whenever a communication error occurs.
    /// Defaults to true.
    /// <para>
    /// This is initialized and set by non null <see cref="CommunicationChannelConfiguration.AutoReconnect"/> when
    /// <see cref="ReconfigureAsync(IActivityMonitor, CommunicationChannelConfiguration)"/> is called.
    /// </para>
    /// </summary>
    public bool AutoReconnect { get; set; }

    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    public ConnectionAvailability ConnectionStatus => _connectStatus;

    /// <summary>
    /// Raised whenever <see cref="ConnectionStatus"/> changes.
    /// </summary>
    public PerfectEvent<CommunicationChannelStatusChangedEvent> ConnectionStatusChanged => _statusChanged.PerfectEvent;

    /// <summary>
    /// Gets whether this channel is disposed.
    /// </summary>
    public bool IsDisposed => _disposed != 0;

    /// <summary>
    /// Gets a stable automatic numbered name for this channel.
    /// </summary>
    public string Name => _name;

    static CommunicationChannel() => CentralMonitor.StaticInitialization();

    internal static string GetNextChannelName() => $"CommunicationChannel n°{Interlocked.Increment( ref _nameCount )}";

    CommunicationChannel( ChannelImpl initialChannel,
                          CommunicationChannelConfiguration configuration,
                          string name )
    {
        Throw.DebugAssert( initialChannel != null );
        Throw.DebugAssert( configuration != null );
        Throw.DebugAssert( (initialChannel.Input == null) == (initialChannel.Output == null) );

        _name = name;
        _semaphore = new SemaphoreSlim( 1, 1 );
        _monitor = new ActivityMonitor( _name );
        _logCatcher = new LogCatcher( this, _monitor.Output );
        _channel = initialChannel;
        _readerBehavior = new Behavior( this, b => new StablePipeReader( b ), initialChannel.ReaderBehavior );
        _reader = (StablePipeReader)_readerBehavior.Pipe;
        _writerBehavior = new Behavior( this, b => new StablePipeWriter( b ), initialChannel.WriterBehavior );
        _writer = (StablePipeWriter)_writerBehavior.Pipe;

        AutoReconnect = true;
        ApplyConfiguration( _config = configuration );
        _statusChanged = new PerfectEventSender<CommunicationChannelStatusChangedEvent>();

        Throw.DebugAssert( _connectStatus == ConnectionAvailability.None && _lastSentConnectStatus == ConnectionAvailability.None ); 
        if( initialChannel.Input != null )
        {
            Throw.DebugAssert( initialChannel.Output != null );
            _connectStatus = _lastSentConnectStatus = ConnectionAvailability.Connected;
            _reader.SetReader( initialChannel.Input );
            _writer.SetWriter( initialChannel.Output );
        }
        else
        {
            _ = SafeInitialStartAsync();
        }
    }

    // This NEVER throws.
    async Task SafeInitialStartAsync()
    {
        try
        {
            await _semaphore.WaitAsync().ConfigureAwait( false );
            // We MAY be already connected. If it's the case we have nothing to do.
            // We MAY already have a reconnector: we also have nothing to do.
            if( _connectStatus != ConnectionAvailability.Connected || _reconnector != null )
            {
                using( _monitor.OpenInfo( $"New {_name} starting initial connection." ) )
                {
                    await DoReopenAsync( _monitor );
                    _lastSentConnectStatus = _connectStatus;
                    _monitor.CloseGroup( _connectStatus.ToString() );
                }
            }
            _semaphore.Release();
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( ActivityMonitor.Tags.ToBeInvestigated, $"Initial start of {_name} unexpected error.", ex );
        }
    }

    /// <summary>
    /// Creates a new channel based on its configuration.
    /// <para>
    /// There is absolutely no fancy protection here: if anything goes wrong, exceptions are thrown.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>A ready to use channel.</returns>
    public static CommunicationChannel Create( IActivityMonitor monitor, CommunicationChannelConfiguration configuration )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( configuration );
        if( !configuration.CheckValid( monitor ) )
        {
            Throw.ArgumentException( nameof( configuration ), $"Configuration {configuration.GetType().Name} is invalid. See logs for details." );
        }
        var communicationChannelName = GetNextChannelName();
        using( monitor.OpenTrace( $"Creating initial implementation channel for {communicationChannelName}." ) )
        {
            var impl = configuration.CreateChannelImpl( monitor, canOpenConnection: true );
            return new CommunicationChannel( impl, configuration, communicationChannelName );
        }
    }

    void ApplyConfiguration( CommunicationChannelConfiguration configuration )
    {
        if( configuration.DefaultReadTimeout.HasValue ) _reader.DefaultTimeout = configuration.DefaultReadTimeout.Value;
        if( configuration.DefaultWriteTimeout.HasValue ) _writer.DefaultTimeout = configuration.DefaultWriteTimeout.Value;
        if( configuration.DefaultRetryWriteCount.HasValue ) _writer.RetryWriteCount = configuration.DefaultRetryWriteCount.Value;
        if( configuration.AutoReconnect.HasValue ) AutoReconnect = configuration.AutoReconnect.Value;
    }

    /// <summary>
    /// Reconfigures this channel.
    /// This can be called concurrently (an <see cref="AsyncLock"/> lock is used).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The new configuration to apply.</param>
    /// <returns>The awaitable.</returns>
    public async ValueTask ReconfigureAsync( IActivityMonitor monitor, CommunicationChannelConfiguration configuration )
    {
        Throw.CheckNotNullArgument( configuration );
        if( !await EnterLockAsync().ConfigureAwait( false ) )
        {
            // In a concurrent world, simply returns instead of throwing a ObjectDisposedException.
            return;
        }

        try
        {
            using( monitor.OpenInfo( $"Reconfiguring {_name}." ) )
            {
                monitor.Debug( $"Previous: {_config}." );
                monitor.Debug( $"New: {configuration}." );
                // Even if nothing changed, we update the reference to the new configuration object
                // and update the reader/writer configurable properties.
                ApplyConfiguration( configuration );
                switch( _config.CanDynamicReconfigureWith( configuration ) )
                {
                    case null:
                        monitor.CloseGroup( "No actual changes. Skipped." );
                        break;
                    case true:
                        monitor.Info( "Applying dynamic reconfiguration." );
                        _config = configuration;
                        await _channel.DynamicReconfigureAsync( monitor, configuration ).ConfigureAwait( false );
                        break;
                    case false:
                        monitor.Info( "Reconfiguration requires to close and reopen the inner channel." );
                        _config = configuration;
                        // Whether we are reconnecting or not, we must clear the current
                        // ChannelImpl provided by the previous configuration.
                        await DoCloseAsync( monitor ).ConfigureAwait( false );
                        // If we are reconnecting, we have nothing to do.
                        if( _reconnector == null )
                        {
                            // Even if AutoReconnect is false, we attempt to connect to
                            // challenge the new configuration.
                            await DoReopenAsync( monitor ).ConfigureAwait( false );
                        }
                        break;
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    void SetConnectStatus( ConnectionAvailability status, bool silentMode )
    {
        _connectStatus = status;
        if( !silentMode && _lastSentConnectStatus != status )
        {
            var logs = _logCatcher.OnConnectionChange( status );
            _lastSentConnectStatus = status;
            CentralMonitor.Send( new CommunicationChannelStatusChangedEvent( this, status, logs ) );
        }
    }

    async ValueTask<bool> EnterLockAsync()
    {
        if( _disposed == 0 )
        {
            await _semaphore.WaitAsync().ConfigureAwait( false );
            if( _disposed == 0 )
            {
                return true;
            }
            _semaphore.Release();
        }
        return false;
    }

    // Fired & Forgot: this MUST never throw.
    // The Reconnector handles reentrancy.
    async Task OnReconnectorCallAsync( int attemptCount )
    {
        try
        {
            Throw.DebugAssert( attemptCount > 0 );
            // When disposed, DisposeAsync did the job to dispose the reconnector.
            if( await EnterLockAsync().ConfigureAwait( false ) )
            {
                // If we have no more reconnector, we have nothing to do.
                if( _reconnector != null )
                {
                    using( _monitor.OpenInfo( $"{_name} reconnect attempt n°{attemptCount}." ) )
                    {
                        // If we are not AutoReconnecting or we are connected, we stop
                        // the reconnection.
                        if( !AutoReconnect || _connectStatus == ConnectionAvailability.Connected )
                        {
                            _reconnector.Dispose();
                            _reconnector = null;
                            _monitor.Trace( AutoReconnect ? "Reconnector disposed (AutoReconnect: false)." : "Reconnector disposed." );
                        }
                        else
                        {
                            await DoReopenAsync( _monitor, fromReconnector: true ).ConfigureAwait( false );
                        }
                        _monitor.CloseGroup( _connectStatus.ToString() );
                    }
                }
                // We leave the lock here and not in a finally: there should be no
                // exception in this block and if it happens, the state of the channel may
                // be corrupted: the ToBeInvestigated error log is our sole safety net.
                _semaphore.Release();
            }
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( ActivityMonitor.Tags.ToBeInvestigated, $"Unexpected error while auto reconnecting {Name}.", ex );
        }
    }

    // Fired & Forgot: this MUST never throw.
    async Task OnPipeErrorAsync( IStablePipe culprit, Exception error )
    {
        try
        {
            if( await EnterLockAsync().ConfigureAwait( false ) )
            {
                _monitor.Error( $"{Name} {(culprit == _reader ? "read" : "writ")}er pipe error.", error );
                await DoReopenAsync( _monitor );
                // We leave the lock here and not in a finally: there should be no
                // exception in this block and if it happens, the state of the channel may
                // be corrupted: the ToBeInvestigated error log is our sole safety net.
                _semaphore.Release();
            }
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( ActivityMonitor.Tags.ToBeInvestigated, $"Unexpected error while handling pipe error for {Name}.", ex );
        }
    }

    // Fired & Forgot: this MUST never throw.
    async Task OnInnerCompletedAsync( IStablePipe culprit, bool autoReconnect )
    {
        try
        {
            if( await EnterLockAsync().ConfigureAwait( false ) )
            {
                _monitor.Error( $"{Name} {(culprit == _reader ? "read" : "writ")}er inner pipe completed. AutoReconnect is {autoReconnect}." );
                if( autoReconnect )
                {
                    await DoReopenAsync( _monitor );
                }
                else
                {
                    await DoCloseAsync( _monitor );
                }
                // We leave the lock here and not in a finally: there should be no
                // exception in this block and if it happens, the state of the channel may
                // be corrupted: the ToBeInvestigated error log is our sole safety net.
                _semaphore.Release();
            }
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( ActivityMonitor.Tags.ToBeInvestigated, $"Unexpected error while handling inner pipe completed {Name}.", ex );
        }
    }

    async Task DoReopenAsync( IActivityMonitor monitor, bool silentMode = false, bool fromReconnector = false )
    {
        Throw.DebugAssert( "The lock is held.", _semaphore.CurrentCount == 0 );
        if( _channel.Input != null )
        {
            // Close without completing the current pipes (but since DoCloseAsync disposes the
            // current channel implementation, the pipes are de facto closed).
            // Since we don't complete, a new ChannelImpl is created (with false canOpenConnection).
            await DoCloseAsync( monitor, silentMode ).ConfigureAwait( false );
        }
        // Here we are necessarily disconnected.
        Throw.DebugAssert( _channel.Input == null );
        Throw.DebugAssert( _connectStatus < ConnectionAvailability.Connected );
        // Attempts to open the initial connection.
        if( !await _channel.InitialOpenAsync( monitor, _name, _config ).ConfigureAwait( false ) )
        {
            // On initial failure, don't try to reuse this channel impl.
            await DoCloseAsync( monitor, silentMode ).ConfigureAwait( false );

            // Each failed attempt to connect decrease the availabilty:
            // since we are at most in Low, a failed attempt leads to at most DangerZone.
            if( _connectStatus != ConnectionAvailability.None )
            {
                SetConnectStatus( _connectStatus - 1, silentMode );
            }
            if( _reconnector != null )
            {
                // If we are not called from the reconnector, let
                // the reconnector live its own parallel life. 
                if( fromReconnector )
                {
                    // Currently, reconnector never gives up but this
                    // may change by introducing a MaxAutoReconnectCount.
                    if( !_reconnector.PlanifyNext( monitor ) )
                    {
                        _reconnector.Dispose();
                        _reconnector = null;
                    }
                }
            }
            else if( AutoReconnect )
            {
                monitor.Info( $"Creating Reconnector for {Name}. First attempt in 50 ms." );
                _reconnector = new Reconnector( this, 50 );
            }
            return;
        }
        Throw.DebugAssert( _channel.Input != null && _channel.Output != null );
        // Read & Write behaviors may have been updated by InitialOpenAsync.
        _readerBehavior.SetImplBehavior( _channel.ReaderBehavior );
        _writerBehavior.SetImplBehavior( _channel.WriterBehavior );
        _reader.SetReader( _channel.Input );
        _writer.SetWriter( _channel.Output );
        // We don't need the recoonector anymore.
        if( _reconnector != null )
        {
            _reconnector.Dispose();
            _reconnector = null;
        }
        // As soon as we have opened a connection, we are connected.
        SetConnectStatus( ConnectionAvailability.Connected, silentMode );
    }

    async Task DoCloseAsync( IActivityMonitor monitor, bool silentMode = false, bool complete = false )
    {
        Throw.DebugAssert( "The lock is held.", _semaphore.CurrentCount == 0 );
        // First, close the pipes. Any error will be routed to
        // OnSwallowed behavior.
        _reader.Close( complete );
        _writer.Close( complete );
        // At least "Connected" to "Low" transition.
        if( _connectStatus == ConnectionAvailability.Connected )
        {
            SetConnectStatus( ConnectionAvailability.Low, silentMode );
        }
        // Second, dispose the current channel and if we are not completing,
        // allocate a new one.
        try
        {
            await _channel.DisposeAsync( monitor ).ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Warn( $"Error while disposing the current channel implementation of {ToString()}. Continuing.", ex );
        }
        if( !complete )
        {
            using( monitor.OpenTrace( $"Creating new implementation channel for {_name}." ) )
            {
                _channel = _config.CreateChannelImpl( monitor, canOpenConnection: false );
            }
        }
    }

    /// <summary>
    /// Disposes this channel.
    /// </summary>
    /// <returns>The awaitable.</returns>
    public async ValueTask DisposeAsync()
    {
        if( Interlocked.CompareExchange( ref _disposed, 1, 0 ) == 0 )
        {
            // Do not use EnterLock since we are disposed.
            await _semaphore.WaitAsync().ConfigureAwait( false );
            if( _reconnector != null )
            {
                _reconnector.Dispose();
                _reconnector = null;
            }
            await DoCloseAsync( _monitor, silentMode: true, complete: true ).ConfigureAwait( false );
            SetConnectStatus( ConnectionAvailability.None, silentMode: false );
            _monitor.MonitorEnd( $"{Name} disposed." );
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Overridden to return this name and configuration's ToString().
    /// </summary>
    /// <returns>The configuration's string.</returns>
    public override string? ToString() => $"{_name}: {_config}";

}
