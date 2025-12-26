using LinqToLdap.Helpers;
using System;
using System.Collections.Concurrent;
using System.DirectoryServices.Protocols;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LinqToLdap
{
    internal sealed class PooledLdapConnectionFactory : ConnectionFactoryBase, 
        IPooledConnectionFactoryConfiguration, IPooledLdapConnectionFactory, IDisposable, IAsyncDisposable
    {
        // Thread-safe collections replace lock-protected dictionaries
        private ConcurrentBag<PooledConnection> _availableConnections = new();
        private ConcurrentDictionary<LdapConnection, PooledConnection> _inUseConnections = new();
        
        // Immutable configuration after initialization
        private volatile bool _disposed;
        private int _isInitialized; // Changed from bool to int for Interlocked operations
        
        // Configuration fields - now readonly after initialization
        private int _maxPoolSize = 50;
        private int _minPoolSize;
        private double _connectionIdleTime = 1;
        private TimeSpan _maxConnectionAge = TimeSpan.FromMinutes(30);
        private double _scavengeInterval = 90000;
        
        // Modern async primitives
        private PeriodicTimer _scavengeTimer;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _scavengeTask;
        
        // Semaphore for pool size limit enforcement
        private SemaphoreSlim _poolSemaphore;

        public PooledLdapConnectionFactory(string serverName) : base(serverName)
        {
        }

        #region Configuration Methods (Thread-Safe)

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.ProtocolVersion(int version)
        {
            ThrowIfInitialized();
            LdapProtocolVersion = version;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.UsePort(int port)
        {
            ThrowIfInitialized();
            UsesSsl = false;
            Port = port;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.UseSsl(int port)
        {
            ThrowIfInitialized();
            UsesSsl = true;
            Port = port;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.UseSsl()
        {
            ThrowIfInitialized();
            UsesSsl = true;
            Port = SslPort;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.ConnectionTimeoutIn(double seconds)
        {
            ThrowIfInitialized();
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(seconds, 0, nameof(seconds));
            Timeout = TimeSpan.FromSeconds(seconds);
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.ServerNameIsFullyQualified()
        {
            ThrowIfInitialized();
            FullyQualifiedDnsHostName = true;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.UseUdp()
        {
            ThrowIfInitialized();
            IsConnectionless = true;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.AuthenticateBy(AuthType authType)
        {
            ThrowIfInitialized();
            AuthType = authType;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.AuthenticateAs(NetworkCredential credentials)
        {
            ThrowIfInitialized();
            Credentials = credentials;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.MaxPoolSizeIs(int size)
        {
            ThrowIfInitialized();
            ArgumentOutOfRangeException.ThrowIfLessThan(size, 1, nameof(size));
            _maxPoolSize = size;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.MinPoolSizeIs(int size)
        {
            ThrowIfInitialized();
            ArgumentOutOfRangeException.ThrowIfNegative(size, nameof(size));
            _minPoolSize = size;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.MaxConnectionAgeIs(TimeSpan timeSpan)
        {
            ThrowIfInitialized();
            _maxConnectionAge = timeSpan;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.ConnectionIdleTimeIs(double idleTime)
        {
            ThrowIfInitialized();
            ArgumentOutOfRangeException.ThrowIfNegative(idleTime, nameof(idleTime));
            _connectionIdleTime = idleTime;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.ScavengeIntervalIs(double interval)
        {
            ThrowIfInitialized();
            ArgumentOutOfRangeException.ThrowIfNegative(interval, nameof(interval));
            _scavengeInterval = interval;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.UseSealing()
        {
            ThrowIfInitialized();
            Sealing = true;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.UseSigning()
        {
            ThrowIfInitialized();
            Signing = true;
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.IgnoreSslCertificateErrors()
        {
            ThrowIfInitialized();
            _IgnoreSslCertificateErrors = true;
            return this;
        }

        private void ThrowIfInitialized()
        {
            if (_isInitialized == 1)
                throw new InvalidOperationException("Cannot modify configuration after the pool has been initialized.");
        }

        #endregion

        #region Connection Pool Methods

        public LdapConnection GetConnection()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Initialize pool on first access (thread-safe)
            if (_isInitialized == 0)
            {
                InitializePool();
            }

            // Try to get available connection first
            while (_availableConnections.TryTake(out var pooledConnection))
            {
                // Check if connection is still valid
                if (IsConnectionValid(pooledConnection))
                {
                    // Move to in-use
                    if (_inUseConnections.TryAdd(pooledConnection.Connection, pooledConnection))
                    {
                        Logger?.Trace("Reusing available connection.");
                        LogPoolStats();
                        return pooledConnection.Connection;
                    }
                }
                
                // Connection is stale or invalid - dispose it
                pooledConnection.Connection.Dispose();
                Logger?.Trace("Stale connection discarded.");
            }

            // No available connections - create new one if under limit
            if (!_poolSemaphore.Wait(0)) // Non-blocking check
            {
                throw new InvalidOperationException($"LdapConnection pool limit of {_maxPoolSize} exceeded.");
            }

            try
            {
                var connection = BuildConnection();
                
                // Validate new connection
                if (!ValidateConnection(connection))
                {
                    connection.Dispose();
                    throw new InvalidOperationException("Failed to create valid LDAP connection.");
                }

                var pooledConnection = new PooledConnection(connection, DateTime.UtcNow);
                if (!_inUseConnections.TryAdd(connection, pooledConnection))
                {
                    connection.Dispose();
                    throw new InvalidOperationException("Failed to track new connection.");
                }

                Logger?.Trace("Created new connection.");
                LogPoolStats();
                return connection;
            }
            catch
            {
                _poolSemaphore.Release();
                throw;
            }
        }

        public void ReleaseConnection(LdapConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            
            if (_disposed)
            {
                connection.Dispose();
                return;
            }

            if (_inUseConnections.TryRemove(connection, out var pooledConnection))
            {
                // Check if connection exceeded max age
                if (DateTime.UtcNow - pooledConnection.CreatedAt < _maxConnectionAge)
                {
                    // Return to pool
                    pooledConnection.LastUsedAt = DateTime.UtcNow;
                    _availableConnections.Add(pooledConnection);
                    Logger?.Trace("Connection returned to pool.");
                }
                else
                {
                    // Connection too old - dispose it
                    connection.Dispose();
                    _poolSemaphore.Release();
                    Logger?.Trace("Connection exceeded max age and was disposed.");
                }
            }
            else
            {
                // Unknown connection - dispose it
                connection.Dispose();
                Logger?.Trace("Unknown connection disposed.");
            }
        }

        public void Reinitialize()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            Logger?.Trace("Reinitializing Connection Pool.");

            // Clear in-use connections
            foreach (var kvp in _inUseConnections)
            {
                if (_inUseConnections.TryRemove(kvp.Key, out var pooledConnection))
                {
                    pooledConnection.Connection.Dispose();
                }
            }

            // Clear available connections
            while (_availableConnections.TryTake(out var pooledConnection))
            {
                try
                {
                    pooledConnection.Connection.Dispose();
                    _poolSemaphore.Release();
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex);
                }
            }

            Interlocked.Exchange(ref _isInitialized, 0);
        }

        #endregion

        #region Private Helper Methods

        private void InitializePool()
        {
            // Use Interlocked for thread-safe initialization
            if (Interlocked.CompareExchange(ref _isInitialized, 1, 0) != 0)
                return;

            Logger?.Trace("Initializing Connection Pool.");

            // Initialize semaphore
            _poolSemaphore = new SemaphoreSlim(_maxPoolSize, _maxPoolSize);

            // Pre-create minimum connections
            for (int i = 0; i < _minPoolSize; i++)
            {
                try
                {
                    if (_poolSemaphore.Wait(0))
                    {
                        var connection = BuildConnection();
                        var pooledConnection = new PooledConnection(connection, DateTime.UtcNow);
                        _availableConnections.Add(pooledConnection);
                    }
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "Failed to create connection during pool initialization.");
                }
            }

            // Start scavenger
            StartScavenger();
            
            Logger?.Trace("Connection pool initialized.");
            LogPoolStats();
        }

        private void StartScavenger()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _scavengeTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_scavengeInterval));
            _scavengeTask = Task.Run(RunScavengeLoopAsync, _cancellationTokenSource.Token);
        }

        private async Task RunScavengeLoopAsync()
        {
            try
            {
                while (await _scavengeTimer.WaitForNextTickAsync(_cancellationTokenSource.Token))
                {
                    if (_disposed) break;

                    try
                    {
                        Logger?.Trace($"Scavenging connections. Available: {_availableConnections.Count}");
                        
                        await ScavengeConnectionsAsync();
                        
                        Logger?.Trace($"Scavenge complete. Available: {_availableConnections.Count}");
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex, "Error during connection scavenging.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when disposed
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Unexpected error in scavenge loop.");
            }
        }

        private async Task ScavengeConnectionsAsync()
        {
            int amountToScavenge = _minPoolSize == 0
                ? _availableConnections.Count
                : Math.Max(0, _availableConnections.Count - _minPoolSize);

            if (amountToScavenge <= 0) return;

            var now = DateTime.UtcNow;
            var connectionsToCheck = _availableConnections.Count;
            var scavenged = 0;

            for (int i = 0; i < connectionsToCheck && scavenged < amountToScavenge; i++)
            {
                if (_availableConnections.TryTake(out var pooledConnection))
                {
                    // Check if connection is idle too long
                    if ((now - pooledConnection.LastUsedAt).TotalMinutes > _connectionIdleTime)
                    {
                        try
                        {
                            pooledConnection.Connection.Dispose();
                            _poolSemaphore.Release();
                            scavenged++;
                            Logger?.Trace("Connection scavenged due to idle time.");
                        }
                        catch (Exception ex)
                        {
                            Logger?.Error(ex, "Error disposing scavenged connection.");
                        }
                    }
                    else
                    {
                        // Return to pool
                        _availableConnections.Add(pooledConnection);
                    }
                }
            }

            await Task.CompletedTask; // Allow for future async scavenging logic
        }

        private bool IsConnectionValid(PooledConnection pooledConnection)
        {
            // Check max age
            if (DateTime.UtcNow - pooledConnection.CreatedAt > _maxConnectionAge)
            {
                Logger?.Trace("Connection exceeded max age.");
                return false;
            }

            // Validate health
            if (!ValidateConnection(pooledConnection.Connection))
            {
                Logger?.Trace("Connection failed health check.");
                return false;
            }

            return true;
        }

        private bool ValidateConnection(LdapConnection connection)
        {
            try
            {
                // Quick health check - search for RootDSE
                var request = new SearchRequest("", "(objectClass=*)", SearchScope.Base, "1.1")
                {
                    SizeLimit = 1,
                    TimeLimit = TimeSpan.FromSeconds(2)
                };
                
                var response = connection.SendRequest(request) as SearchResponse;
                return response?.ResultCode == ResultCode.Success;
            }
            catch
            {
                return false;
            }
        }

        private void LogPoolStats()
        {
            if (Logger?.TraceEnabled == true)
            {
                Logger.Trace($"Pool stats - In use: {_inUseConnections.Count}, Available: {_availableConnections.Count}, Total: {_maxPoolSize - _poolSemaphore.CurrentCount}");
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop scavenger
            _cancellationTokenSource?.Cancel();
            
            try
            {
                _scavengeTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // Expected
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Error stopping scavenger during disposal.");
            }

            DisposeConnections();

            _scavengeTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
            _poolSemaphore?.Dispose();

            Logger = null;
            Credentials = null;

            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop scavenger
            _cancellationTokenSource?.Cancel();

            if (_scavengeTask != null)
            {
                try
                {
                    await _scavengeTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException ex)
                {
                    Logger?.Error(ex, "Scavenger task did not complete in time during async disposal.");
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            DisposeConnections();

            _scavengeTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
            _poolSemaphore?.Dispose();

            Logger = null;
            Credentials = null;

            GC.SuppressFinalize(this);
        }

        private void DisposeConnections()
        {
            // Dispose in-use connections
            foreach (var kvp in _inUseConnections)
            {
                try
                {
                    if (_inUseConnections.TryRemove(kvp.Key, out var pooledConnection))
                    {
                        pooledConnection.Connection.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex);
                }
            }

            // Dispose available connections
            while (_availableConnections.TryTake(out var pooledConnection))
            {
                try
                {
                    pooledConnection.Connection.Dispose();
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex);
                }
            }
        }

        #endregion

        #region Nested Types

        private sealed class PooledConnection
        {
            public LdapConnection Connection { get; }
            public DateTime CreatedAt { get; }
            public DateTime LastUsedAt { get; set; }

            public PooledConnection(LdapConnection connection, DateTime createdAt)
            {
                Connection = connection;
                CreatedAt = createdAt;
                LastUsedAt = createdAt;
            }
        }

        #endregion
    }
}