using LinqToLdap.Helpers;
using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LinqToLdap
{
    internal class PooledLdapConnectionFactory : ConnectionFactoryBase, 
        IPooledConnectionFactoryConfiguration, IPooledLdapConnectionFactory, IDisposable
    {
        private readonly object _connectionLock = new();
        private readonly object _configLock = new();
        
        private Dictionary<LdapConnection, TwoTuple<DateTime, DateTime>> _availableConnections = new();
        private Dictionary<LdapConnection, DateTime> _inUseConnections = new();
        
        // Use volatile for flags checked outside locks
        private volatile bool _disposed;
        private volatile bool _isInitialized;
        
        // Configuration fields - protected by _configLock
        private int _maxPoolSize = 50;
        private int _minPoolSize;
        private double _connectionIdleTime = 1;
        private TimeSpan _maxConnectionAge = TimeSpan.FromMinutes(30);
        private double _scavengeInterval = 90000;
        
        // Replace System.Timers.Timer with PeriodicTimer
        private PeriodicTimer _scavengeTimer;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _scavengeTask;

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
            if (seconds <= 0) throw new ArgumentException("seconds must be greater than 0");
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
            if (size < 1) throw new ArgumentException("MaxPoolSize must be greater than zero.");
            lock (_configLock)
            {
                _maxPoolSize = size;
            }
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.MinPoolSizeIs(int size)
        {
            ThrowIfInitialized();
            if (size < 0) throw new ArgumentException("MinPoolSize cannot be negative.");
            lock (_configLock)
            {
                _minPoolSize = size;
            }
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.MaxConnectionAgeIs(TimeSpan timeSpan)
        {
            ThrowIfInitialized();
            lock (_configLock)
            {
                _maxConnectionAge = timeSpan;
            }
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.ConnectionIdleTimeIs(double idleTime)
        {
            ThrowIfInitialized();
            if (idleTime < 0) throw new ArgumentException("ConnectionIdleTime cannot be negative.");
            lock (_configLock)
            {
                _connectionIdleTime = idleTime;
            }
            return this;
        }

        IPooledConnectionFactoryConfiguration IPooledConnectionFactoryConfiguration.ScavengeIntervalIs(double interval)
        {
            ThrowIfInitialized();
            if (interval < 0) throw new ArgumentException("ScavengeInterval cannot be negative.");
            lock (_configLock)
            {
                _scavengeInterval = interval;
            }
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
            if (_isInitialized)
                throw new InvalidOperationException("Cannot modify configuration after the pool has been initialized.");
        }

        #endregion

        #region Connection Pool Methods

        public LdapConnection GetConnection()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            lock (_connectionLock)
            {
                // Double-check disposal inside lock
                ObjectDisposedException.ThrowIf(_disposed, this);

                try
                {
                    if (!_isInitialized)
                    {
                        InitializePool();
                    }

                    // Remove stale connections
                    RemoveStaleConnections();

                    // Try to get available connection
                    var pair = _availableConnections!.FirstOrDefault();

                    LdapConnection connection;
                    if (Equals(pair, default(KeyValuePair<LdapConnection, TwoTuple<DateTime, DateTime>>)))
                    {
                        // No available connections - create new one
                        if (Logger?.TraceEnabled == true)
                            Logger.Trace("Creating Connection For Use.");

                        int currentTotal = _inUseConnections!.Count + _availableConnections.Count + 1;
                        int maxSize;
                        lock (_configLock) { maxSize = _maxPoolSize; }

                        if (currentTotal > maxSize)
                            throw new InvalidOperationException($"LdapConnection pool limit of {maxSize} exceeded.");

                        connection = BuildConnection();
                        
                        // Validate connection before adding to pool
                        if (!ValidateConnection(connection))
                        {
                            connection.Dispose();
                            throw new InvalidOperationException("Failed to create valid LDAP connection.");
                        }

                        _inUseConnections.Add(connection, DateTime.UtcNow);
                    }
                    else
                    {
                        // Reuse available connection
                        if (Logger?.TraceEnabled == true)
                            Logger.Trace("Using Available Connection.");

                        connection = pair.Key;
                        
                        // Validate connection health before reusing
                        if (!ValidateConnection(connection))
                        {
                            if (Logger?.TraceEnabled == true)
                                Logger.Trace("Connection failed health check. Creating new connection.");
                            
                            _availableConnections.Remove(pair.Key);
                            pair.Key.Dispose();
                            
                            // Recursively try again
                            return GetConnection();
                        }

                        _inUseConnections.Add(pair.Key, pair.Value.Item1);
                        _availableConnections.Remove(pair.Key);
                    }

                    if (Logger?.TraceEnabled == true)
                    {
                        Logger.Trace($"In Use Connection Count: {_inUseConnections.Count}");
                        Logger.Trace($"Available Connection Count: {_availableConnections.Count}");
                    }

                    return connection;
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex);
                    throw;
                }
            }
        }

        public void ReleaseConnection(LdapConnection connection)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            lock (_connectionLock)
            {
                // Double-check disposal inside lock
                if (_disposed)
                {
                    connection.Dispose();
                    return;
                }

                if (_inUseConnections!.TryGetValue(connection, out DateTime createdDate))
                {
                    _inUseConnections.Remove(connection);

                    TimeSpan maxAge;
                    lock (_configLock) { maxAge = _maxConnectionAge; }

                    if (DateTime.UtcNow.Subtract(createdDate) < maxAge)
                    {
                        _availableConnections!.Add(connection, new TwoTuple<DateTime, DateTime>(createdDate, DateTime.UtcNow));
                        if (Logger?.TraceEnabled == true)
                            Logger.Trace("Connection Marked As Available");
                    }
                    else
                    {
                        connection.Dispose();
                        if (Logger?.TraceEnabled == true)
                            Logger.Trace("Connection Exceeds Max Age. Connection Disposed.");
                    }
                }
                else
                {
                    // Connection not tracked - dispose it
                    connection.Dispose();
                    if (Logger?.TraceEnabled == true)
                        Logger.Trace("Unknown connection disposed.");
                }
            }
        }

        public void Reinitialize()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            lock (_connectionLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (Logger?.TraceEnabled == true)
                    Logger.Trace("Reinitializing Connection Pool.");

                // Clear in-use connections
                _inUseConnections!.Clear();

                // Dispose available connections
                foreach (var availableConnection in _availableConnections!)
                {
                    try
                    {
                        availableConnection.Key.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex);
                    }
                }
                _availableConnections.Clear();

                _isInitialized = false;
            }
        }

        #endregion

        #region Private Helper Methods

        private void InitializePool()
        {
            // Must be called within _connectionLock

            if (Logger?.TraceEnabled == true)
                Logger.Trace("Initializing Connection Pool.");

            int minSize;
            lock (_configLock) { minSize = _minPoolSize; }

            for (int i = 0; i < minSize; i++)
            {
                try
                {
                    var connection = BuildConnection();
                    _availableConnections!.Add(connection, new TwoTuple<DateTime, DateTime>(DateTime.UtcNow, DateTime.UtcNow));
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "Failed to create connection during pool initialization.");
                    // Continue creating remaining connections
                }
            }

            _isInitialized = true;

            // Start scavenger
            StartScavenger();

            if (Logger?.TraceEnabled == true)
                Logger.Trace("Scavenge Timer Started.");
        }

        private void StartScavenger()
        {
            // Must be called within _connectionLock
            
            double interval;
            lock (_configLock) { interval = _scavengeInterval; }

            _cancellationTokenSource = new CancellationTokenSource();
            _scavengeTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
            _scavengeTask = Task.Run(async () => await RunScavengeLoopAsync());
        }

        private async Task RunScavengeLoopAsync()
        {
            try
            {
                while (await _scavengeTimer!.WaitForNextTickAsync(_cancellationTokenSource!.Token))
                {
                    if (_disposed) break;

                    lock (_connectionLock)
                    {
                        if (_disposed) break;

                        try
                        {
                            if (Logger?.TraceEnabled == true)
                            {
                                Logger.Trace($"Available Connections Before Scavenge: {_availableConnections!.Count}");
                                Logger.Trace("Scavenging Connections.");
                            }

                            ScavengeConnections();

                            if (Logger?.TraceEnabled == true)
                                Logger.Trace($"Available Connections After Scavenge: {_availableConnections!.Count}");
                        }
                        catch (Exception ex)
                        {
                            Logger?.Error(ex, "Error during connection scavenging.");
                        }
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

        private void ScavengeConnections()
        {
            // Must be called within _connectionLock

            int minSize;
            double idleTime;
            lock (_configLock)
            {
                minSize = _minPoolSize;
                idleTime = _connectionIdleTime;
            }

            int amountToScavenge = minSize == 0
                ? _availableConnections!.Count
                : (_availableConnections!.Count - minSize);

            if (amountToScavenge <= 0) return;

            DateTime now = DateTime.UtcNow;
            var expiredConnections = (from pair in _availableConnections
                                      where now.Subtract(pair.Value.Item2).TotalMinutes > idleTime
                                      select pair.Key).ToList();

            foreach (var expiredConnection in expiredConnections)
            {
                if (amountToScavenge == 0) break;

                _availableConnections.Remove(expiredConnection);
                try
                {
                    expiredConnection.Dispose();
                    if (Logger?.TraceEnabled == true)
                        Logger.Trace("Connection Scavenged.");
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "Error disposing scavenged connection.");
                }

                amountToScavenge--;
            }
        }

        private void RemoveStaleConnections()
        {
            // Must be called within _connectionLock

            TimeSpan maxAge;
            lock (_configLock) { maxAge = _maxConnectionAge; }

            DateTime now = DateTime.UtcNow;
            var staleConnections = _availableConnections!
                .Where(pair => now.Subtract(pair.Value.Item1) > maxAge)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var staleConnection in staleConnections)
            {
                _availableConnections.Remove(staleConnection);
                try
                {
                    staleConnection.Dispose();
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "Error disposing stale connection.");
                }
            }
        }

        private bool ValidateConnection(LdapConnection connection)
        {
            try
            {
                // Simple health check - search for RootDSE
                var request = new SearchRequest("", "(objectClass=*)", SearchScope.Base, "1.1");
                request.SizeLimit = 1;
                request.TimeLimit = TimeSpan.FromSeconds(2);
                
                var response = connection.SendRequest(request) as SearchResponse;
                return response?.ResultCode == ResultCode.Success;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;

            // Stop scavenger first
            try
            {
                _cancellationTokenSource?.Cancel();
                _scavengeTimer?.Dispose();
                _scavengeTask?.Wait(TimeSpan.FromSeconds(5)); // Give it time to stop
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Error stopping scavenger during disposal.");
            }

            lock (_connectionLock)
            {
                if (_disposed) return;

                _disposed = true;

                DisposeConnections();

                _availableConnections = null;
                _inUseConnections = null;
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _scavengeTimer = null;
            _scavengeTask = null;

            Logger = null;
            Credentials = null;

            GC.SuppressFinalize(this);
        }

        private void DisposeConnections()
        {
            // Must be called within _connectionLock

            if (_availableConnections != null)
            {
                foreach (var connection in _availableConnections)
                {
                    try
                    {
                        connection.Key.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex);
                    }
                }
                _availableConnections.Clear();
            }

            if (_inUseConnections != null)
            {
                foreach (var connection in _inUseConnections)
                {
                    try
                    {
                        connection.Key.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex);
                    }
                }
                _inUseConnections.Clear();
            }
        }

        #endregion
    }
}