using System;
using System.Collections.Concurrent;
using System.DirectoryServices.Protocols;
using System.Threading;
using System.Threading.Tasks;
using LinqToLdap.Helpers;
using LinqToLdap.Logging;
using LinqToLdap.Tests.TestSupport.ExtensionMethods;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SharpTestsEx;

namespace LinqToLdap.Tests
{
    [TestClass]
    public class PooledConnectionFactoryTest
    {
        private PooledLdapConnectionFactory _factory;

        [TestCleanup]
        public void TearDown()
        {
            _factory?.Dispose();
        }

        [TestMethod]
        public void GetConnection_FirstTimeWithMinPoolSize_InitializesConnectionPool()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>().MinPoolSizeIs(2);

            //act
            var connection = _factory.GetConnection();

            //assert
            connection.Should().Not.Be.Null();
            var availableConnections = _factory.FieldValue("_availableConnections");
            var inUseConnections = _factory.FieldValue("_inUseConnections");
            
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(1);
            ((int)inUseConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(1);
            
            _factory.ReleaseConnection(connection);
        }

        [TestMethod]
        public void GetConnection_FirstTimeWithoutMinPoolSize_InitializesConnectionPool()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");

            //act
            var connection = _factory.GetConnection();

            //assert
            connection.Should().Not.Be.Null();
            var availableConnections = _factory.FieldValue("_availableConnections");
            var inUseConnections = _factory.FieldValue("_inUseConnections");
            
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
            ((int)inUseConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(1);
            
            _factory.ReleaseConnection(connection);
        }

        [TestMethod]
        public void ReleaseConnection_InUseConnection_RemovesFromInUseAndAddsToAvailable()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            var connection = _factory.GetConnection();

            //act
            _factory.ReleaseConnection(connection);

            //assert
            var availableConnections = _factory.FieldValue("_availableConnections");
            var inUseConnections = _factory.FieldValue("_inUseConnections");
            
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(1);
            ((int)inUseConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
        }

        [TestMethod]
        public void GetConnection_MaxAge_Exceeded_DisposesConnections()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>()
                .ScavengeIntervalIs(1000)
                .MaxConnectionAgeIs(TimeSpan.FromMilliseconds(500));
            
            var connection = _factory.GetConnection();
            connection.Should().Not.Be.Null();
            _factory.ReleaseConnection(connection);
            
            var same = _factory.GetConnection();
            same.Should().Be.SameInstanceAs(connection);
            _factory.ReleaseConnection(same);

            // Wait for connection to exceed max age
            Thread.Sleep(600);

            //act
            var connection2 = _factory.GetConnection();

            //assert
            connection2.Should().Not.Be.SameInstanceAs(connection);
            _factory.ReleaseConnection(connection2);
        }

        [TestMethod]
        public void ReleaseConnection_UnknownConnection_CallsDispose()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            var connection = new LdapConnection("localhost");

            //act
            _factory.ReleaseConnection(connection);

            //assert
            connection.FieldValueEx<bool>("_disposed").Should().Be.True();
            var availableConnections = _factory.FieldValue("_availableConnections");
            var inUseConnections = _factory.FieldValue("_inUseConnections");
            
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
            ((int)inUseConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
        }

        [TestMethod]
        public void ReleaseConnection_NullConnection_ThrowsArgumentNullException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");

            //act & assert
            Action act = () => _factory.ReleaseConnection(null);
            act.Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void ReleaseConnection_DisposedFactory_DisposesConnection()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            var connection = _factory.GetConnection();
            _factory.Dispose();

            //act
            _factory.ReleaseConnection(connection);

            //assert
            connection.FieldValueEx<bool>("_disposed").Should().Be.True();
        }

        [TestMethod]
        public void ReInitializePool_Disposed_ThrowsException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.Dispose();

            //act
            Action work = () => _factory.Reinitialize();

            //assert
            work.Should().Throw<ObjectDisposedException>()
                .And.Exception.ObjectName.Should().Be.EqualTo(_factory.GetType().FullName);
        }

        [TestMethod]
        public void ReInitializePool_NotDisposed_ReInitializes()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>().MinPoolSizeIs(2);
            var logger = new Mock<ILinqToLdapLogger>();
            logger.SetupGet(l => l.TraceEnabled).Returns(true);
            _factory.Logger = logger.Object;
            
            var connection = _factory.GetConnection();
            _factory.ReleaseConnection(connection);

            //act
            _factory.Reinitialize();

            //assert
            logger.Verify(l => l.Trace("Reinitializing Connection Pool."), Times.Once());
            logger.Verify(l => l.Trace("Initializing Connection Pool."), Times.Once());
            
            var scavengeTimer = _factory.FieldValueEx<PeriodicTimer>("_scavengeTimer");
            scavengeTimer.Should().Not.Be.Null(); // Timer still exists
            
            var isInitialized = _factory.FieldValueEx<int>("_isInitialized");
            isInitialized.Should().Be.EqualTo(0); // Reset to uninitialized
            
            var availableConnections = _factory.FieldValue("_availableConnections");
            var inUseConnections = _factory.FieldValue("_inUseConnections");
            
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
            ((int)inUseConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
        }

        [TestMethod]
        public void GetConnection_ExceedsMaxPoolSize_ThrowsInvalidOperationException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>().MaxPoolSizeIs(2);
            
            var connection1 = _factory.GetConnection();
            var connection2 = _factory.GetConnection();

            //act
            Action act = () => _factory.GetConnection();

            //assert
            act.Should().Throw<InvalidOperationException>()
                .And.Exception.Message.Should().Contain("pool limit of 2 exceeded");
            
            // Cleanup
            _factory.ReleaseConnection(connection1);
            _factory.ReleaseConnection(connection2);
        }

        [TestMethod]
        public void GetConnection_AfterRelease_ReusesConnection()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            var connection1 = _factory.GetConnection();
            _factory.ReleaseConnection(connection1);

            //act
            var connection2 = _factory.GetConnection();

            //assert
            connection2.Should().Be.SameInstanceAs(connection1);
            _factory.ReleaseConnection(connection2);
        }

        [TestMethod]
        public void GetConnection_MultipleThreads_HandlesThreadSafety()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>().MaxPoolSizeIs(10).MinPoolSizeIs(2);
            
            var connections = new ConcurrentBag<LdapConnection>();
            var tasks = new Task[5];

            //act
            for (int i = 0; i < 5; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    var conn = _factory.GetConnection();
                    connections.Add(conn);
                    Thread.Sleep(50);
                    _factory.ReleaseConnection(conn);
                });
            }

            Task.WaitAll(tasks);

            //assert
            connections.Should().Have.Count.EqualTo(5);
            foreach (var c in connections)
            {
                c.Should().Not.Be.Null();
            }
        }

        [TestMethod]
        public void Scavenger_RemovesIdleConnections()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>()
                .MinPoolSizeIs(0)
                .ConnectionIdleTimeIs(0.01) // 0.01 minutes = 600ms
                .ScavengeIntervalIs(500); // Run scavenger every 500ms

            var connection = _factory.GetConnection();
            _factory.ReleaseConnection(connection);

            var availableConnections = _factory.FieldValue("_availableConnections");
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(1);

            //act - wait for scavenger to run and remove idle connection
            Thread.Sleep(1500);

            //assert
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
        }

        [TestMethod]
        public void Scavenger_RespectsMinPoolSize()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>()
                .MinPoolSizeIs(2)
                .ConnectionIdleTimeIs(0.01) // 0.01 minutes = 600ms
                .ScavengeIntervalIs(500);

            var connection1 = _factory.GetConnection();
            _factory.ReleaseConnection(connection1);
            
            var availableConnections = _factory.FieldValue("_availableConnections");
            int initialCount = availableConnections.PropertyValue<int>("Count");
            initialCount.Should().Be.GreaterThanOrEqualTo(1);

            //act - wait for scavenger to run
            Thread.Sleep(1500);

            //assert - should maintain at least minPoolSize
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.GreaterThanOrEqualTo(1);
        }

        [TestMethod]
        public void Configuration_AfterInitialization_ThrowsInvalidOperationException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            var connection = _factory.GetConnection(); // This initializes the pool
            _factory.ReleaseConnection(connection);

            //act & assert
            Action act = () => _factory.As<IPooledConnectionFactoryConfiguration>().MaxPoolSizeIs(10);
            act.Should().Throw<InvalidOperationException>()
                .And.Exception.Message.Should().Contain("Cannot modify configuration after the pool has been initialized");
        }

        [TestMethod]
        public void Configuration_MaxPoolSize_LessThanOne_ThrowsArgumentOutOfRangeException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");

            //act & assert
            Action act = () => _factory.As<IPooledConnectionFactoryConfiguration>().MaxPoolSizeIs(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void Configuration_MinPoolSize_Negative_ThrowsArgumentOutOfRangeException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");

            //act & assert
            Action act = () => _factory.As<IPooledConnectionFactoryConfiguration>().MinPoolSizeIs(-1);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void Configuration_ConnectionIdleTime_Negative_ThrowsArgumentOutOfRangeException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");

            //act & assert
            Action act = () => _factory.As<IPooledConnectionFactoryConfiguration>().ConnectionIdleTimeIs(-1);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void Configuration_ScavengeInterval_Negative_ThrowsArgumentOutOfRangeException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");

            //act & assert
            Action act = () => _factory.As<IPooledConnectionFactoryConfiguration>().ScavengeIntervalIs(-1);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void Configuration_ConnectionTimeout_ZeroOrLess_ThrowsArgumentOutOfRangeException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");

            //act & assert
            Action act = () => _factory.As<IPooledConnectionFactoryConfiguration>().ConnectionTimeoutIn(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void Dispose_Multiple_Times_DoesNotThrow()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            var connection = _factory.GetConnection();
            _factory.ReleaseConnection(connection);

            //act & assert
            Action act = () =>
            {
                _factory.Dispose();
                _factory.Dispose();
                _factory.Dispose();
            };
            
            act.Should().NotThrow();
        }

        [TestMethod]
        public async Task DisposeAsync_DisposesResourcesCorrectly()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>().MinPoolSizeIs(2);
            
            var connection = _factory.GetConnection();
            _factory.ReleaseConnection(connection);

            //act
            await _factory.DisposeAsync();

            //assert
            // After async disposal, calling GetConnection should throw ObjectDisposedException
            Action act = () => _factory.GetConnection();
            act.Should().Throw<ObjectDisposedException>();
        }

        [TestMethod]
        public void GetConnection_AfterDispose_ThrowsObjectDisposedException()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.Dispose();

            //act
            Action act = () => _factory.GetConnection();

            //assert
            act.Should().Throw<ObjectDisposedException>();
        }

        [TestMethod]
        public void ReleaseConnection_ConnectionExceedingMaxAge_DisposesAndReleasesPermit()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>()
                .MaxConnectionAgeIs(TimeSpan.FromMilliseconds(100));
            
            var connection = _factory.GetConnection();
            
            // Wait for connection to exceed max age
            Thread.Sleep(150);

            //act
            _factory.ReleaseConnection(connection);

            //assert
            var availableConnections = _factory.FieldValue("_availableConnections");
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0); // Should not be returned to pool
            
            connection.FieldValueEx<bool>("_disposed").Should().Be.True();
        }

        [TestMethod]
        public void Logger_TraceEnabled_LogsPoolStatistics()
        {
            //prepare
            var logger = new Mock<ILinqToLdapLogger>();
            logger.SetupGet(l => l.TraceEnabled).Returns(true);
            
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.Logger = logger.Object;

            //act
            var connection = _factory.GetConnection();
            _factory.ReleaseConnection(connection);

            //assert
            logger.Verify(l => l.Trace(It.Is<string>(s => s.Contains("Pool stats"))), Times.AtLeastOnce());
        }

        [TestMethod]
        public void Configuration_FluentInterface_ChainsCorrectly()
        {
            //prepare & act
            _factory = new PooledLdapConnectionFactory("localhost");
            
            var config = _factory.As<IPooledConnectionFactoryConfiguration>()
                .MaxPoolSizeIs(20)
                .MinPoolSizeIs(5)
                .ConnectionIdleTimeIs(2)
                .MaxConnectionAgeIs(TimeSpan.FromMinutes(10))
                .ScavengeIntervalIs(60000)
                .UseSealing()
                .UseSigning();

            //assert
            config.Should().Not.Be.Null();
            config.Should().Be.SameInstanceAs(_factory);
        }

        [TestMethod]
        public void GetConnection_ConcurrentAccess_MaintainsPoolIntegrity()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>().MaxPoolSizeIs(20).MinPoolSizeIs(5);
            
            var successCount = 0;
            var tasks = new Task[30];

            //act
            for (int i = 0; i < 30; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    try
                    {
                        var conn = _factory.GetConnection();
                        Interlocked.Increment(ref successCount);
                        Thread.Sleep(10);
                        _factory.ReleaseConnection(conn);
                    }
                    catch (InvalidOperationException)
                    {
                        // Expected when pool limit is exceeded
                    }
                });
            }

            Task.WaitAll(tasks);

            //assert
            successCount.Should().Be.LessThanOrEqualTo(20); // Should not exceed max pool size
            
            var availableConnections = _factory.FieldValue("_availableConnections");
            var inUseConnections = _factory.FieldValue("_inUseConnections");
            
            ((int)inUseConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0); // All connections should be released
        }

        [TestMethod]
        public void Reinitialize_WithActiveConnections_ClearsAllConnections()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>().MinPoolSizeIs(3);
            
            var connection1 = _factory.GetConnection();
            var connection2 = _factory.GetConnection();
            _factory.ReleaseConnection(connection2);

            //act
            _factory.Reinitialize();

            //assert
            var availableConnections = _factory.FieldValue("_availableConnections");
            var inUseConnections = _factory.FieldValue("_inUseConnections");
            
            ((int)availableConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
            ((int)inUseConnections.PropertyValue<int>("Count")).Should().Be.EqualTo(0);
            
            // Original connections should be disposed
            connection1.FieldValueEx<bool>("_disposed").Should().Be.True();
            connection2.FieldValueEx<bool>("_disposed").Should().Be.True();
        }

        [TestMethod]
        public void GetConnection_AfterReinitialize_CreatesNewPool()
        {
            //prepare
            _factory = new PooledLdapConnectionFactory("localhost");
            _factory.As<IPooledConnectionFactoryConfiguration>().MinPoolSizeIs(2);
            
            var connection1 = _factory.GetConnection();
            _factory.ReleaseConnection(connection1);
            
            _factory.Reinitialize();

            //act
            var connection2 = _factory.GetConnection();

            //assert
            connection2.Should().Not.Be.Null();
            connection2.Should().Not.Be.SameInstanceAs(connection1);
            
            _factory.ReleaseConnection(connection2);
        }
    }
}