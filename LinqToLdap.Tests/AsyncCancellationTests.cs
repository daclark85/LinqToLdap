using LinqToLdap.Async;
using LinqToLdap.Logging;
using LinqToLdap.Mapping;
using LinqToLdap.Tests.TestSupport.ExtensionMethods;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpTestsEx;
using System;
using System.DirectoryServices.Protocols;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LinqToLdap.Tests
{
    [TestClass]
    public class AsyncCancellationTests
    {
        private LdapConfiguration _configuration;
        private IDirectoryContext _context;
        private const string ServerName = "localhost";

        [TestInitialize]
        public void SetUp()
        {
            _configuration = new LdapConfiguration()
                .AddMapping(new IntegrationUserTestMapping(), IntegrationUserTest.NamingContext, new[] { "user" })
                .AddMapping(new AttributeClassMap<IntegrationGroupTest>(), IntegrationGroupTest.NamingContext, new[] { "top", "group" }, true, "group")
                .AddMapping(new AttributeClassMap<PersonInheritanceTest>())
                .AddMapping(new AttributeClassMap<OrgPersonInheritanceTest>())
                .AddMapping(new AttributeClassMap<UserInheritanceTest>())
                .AddMapping(new AttributeClassMap<PersonCatchAllTest>())
                .AddMapping(new AttributeClassMap<OrgPersonCatchAllTest>())
                .MaxPageSizeIs(1000)
                .LogTo(new SimpleTextLogger(Console.Out));

            _configuration.ConfigurePooledFactory(ServerName)
                .AuthenticateBy(AuthType.Negotiate);

            _context = _configuration.CreateContext();
        }

        [TestCleanup]
        public void TearDown()
        {
            _context.Dispose();
        }


        [TestMethod]
        [TestCategory("Integration")]
        public void AnyAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();
            var predicate = "TestValue";

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .AnyAsync(x => x.CommonName.StartsWith(predicate), 
                    resultProcessing: LdapConfiguration.DefaultAsyncResultProcessing, 
                    cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .Any(x => x.CommonName.StartsWith(predicate));

            //assert
            resultWithToken.Should().Be.EqualTo(resultWithoutToken);
        }

        //[TestMethod]
        //[TestCategory("Integration")]
        //public async Task AnyAsync_WithPreCancelledToken_ThrowsOperationCancelledException()
        //{
        //    //arrange
        //    var cts = new CancellationTokenSource();
        //    cts.Cancel();

        //    //act & assert
        //    await Executing.This(async () => 
        //        await _context.Query<PersonInheritanceTest>()
        //            .AnyAsync(x => x.CommonName.StartsWith("Test"), cancellationToken: cts.Token))
        //        .Should()
        //        .Throw<OperationCanceledException>();
        //}

        [TestMethod]
        [TestCategory("Integration")]
        public void CountAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .CountAsync(x => Filter.StartsWith(x, "sn", "J", false), cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .Count(x => Filter.StartsWith(x, "sn", "J", false));

            //assert
            resultWithToken.Should().Be.EqualTo(resultWithoutToken);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void LongCountAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .LongCountAsync(x => Filter.StartsWith(x, "sn", "J", false), cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .LongCount(x => Filter.StartsWith(x, "sn", "J", false));

            //assert
            resultWithToken.Should().Be.EqualTo(resultWithoutToken);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void FirstAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .FirstAsync(x => x.CommonName != null, cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .First(x => x.CommonName != null);

            //assert
            resultWithToken.CommonName.Should().Be.EqualTo(resultWithoutToken.CommonName);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void FirstOrDefaultAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .FirstOrDefaultAsync(x => x.CommonName != null, cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .FirstOrDefault(x => x.CommonName != null);

            //assert
            resultWithToken.CommonName.Should().Be.EqualTo(resultWithoutToken.CommonName);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void SingleAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query(PersonInheritanceTest.NamingContext)
                .SingleAsync(g => Filter.Equal(g, "distinguishedName", PersonInheritanceTest.NamingContext, false), 
                    cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query(PersonInheritanceTest.NamingContext)
                .Single(g => Filter.Equal(g, "distinguishedName", PersonInheritanceTest.NamingContext, false));

            //assert
            resultWithToken.DistinguishedName.Should().Be.EqualTo(resultWithoutToken.DistinguishedName);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void SingleOrDefaultAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query(PersonInheritanceTest.NamingContext)
                .SingleOrDefaultAsync(g => Filter.Equal(g, "distinguishedName", PersonInheritanceTest.NamingContext, false), 
                    cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query(PersonInheritanceTest.NamingContext)
                .SingleOrDefault(g => Filter.Equal(g, "distinguishedName", PersonInheritanceTest.NamingContext, false));

            //assert
            resultWithToken.DistinguishedName.Should().Be.EqualTo(resultWithoutToken.DistinguishedName);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void ToListAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .Where(x => x.CommonName != null)
                .ToListAsync(cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .Where(x => x.CommonName != null)
                .ToList();

            //assert
            resultWithToken.Count.Should().Be.EqualTo(resultWithoutToken.Count);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void InPagesOfAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .Where(x => x.CommonName != null)
                .InPagesOfAsync(1000, cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .Where(x => x.CommonName != null)
                .InPagesOf(1000);

            //assert
            resultWithToken.Count.Should().Be.EqualTo(resultWithoutToken.Count);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void ListAttributesAsync_WithCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query(PersonInheritanceTest.NamingContext, SearchScope.Base)
                .ListAttributesAsync(new[] { "cn" }, cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query(PersonInheritanceTest.NamingContext, SearchScope.Base)
                .ListAttributes("cn");

            //assert
            resultWithToken.Should().Have.Count.EqualTo(1);
            resultWithoutToken.Should().Have.Count.EqualTo(1);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetByDNAsync_WithCancellationToken_Executes()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var result = _context.GetByDNAsync<PersonInheritanceTest>(
                PersonInheritanceTest.NamingContext,
                cancellationToken: cts.Token).Result;

            //assert
            result.DistinguishedName.Should().Be.EqualTo(PersonInheritanceTest.NamingContext);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void AnyAsync_WithComplexPredicate_AndCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .AnyAsync(x => x.CommonName.StartsWith("Test") && x.CommonName.Contains("User"), 
                    cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .Any(x => x.CommonName.StartsWith("Test") && x.CommonName.Contains("User"));

            //assert
            resultWithToken.Should().Be.EqualTo(resultWithoutToken);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void CountAsync_WithOrPredicate_AndCancellationToken_DoesNotAffectFilter()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .CountAsync(x => x.CommonName.StartsWith("A") || x.CommonName.StartsWith("B"), 
                    cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .Count(x => x.CommonName.StartsWith("A") || x.CommonName.StartsWith("B"));

            //assert
            resultWithToken.Should().Be.EqualTo(resultWithoutToken);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task MultipleAsyncOperations_WithSameCancellationToken_AllExecuteCorrectly()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act - create separate contexts for each operation to avoid concurrent connection use
            var anyTask = Task.Run(async () =>
            {
                using (var context = _configuration.CreateContext())
                {
                    return await context.Query<PersonInheritanceTest>()
                        .AnyAsync(x => x.CommonName != null, cancellationToken: cts.Token);
                }
            });
            
            var countTask = Task.Run(async () =>
            {
                using (var context = _configuration.CreateContext())
                {
                    return await context.Query<PersonInheritanceTest>()
                        .CountAsync(x => x.CommonName != null, cancellationToken: cts.Token);
                }
            });
            
            var firstTask = Task.Run(async () =>
            {
                using (var context = _configuration.CreateContext())
                {
                    return await context.Query<PersonInheritanceTest>()
                        .FirstOrDefaultAsync(x => x.CommonName != null, cancellationToken: cts.Token);
                }
            });

            await Task.WhenAll(anyTask, countTask, firstTask);

            //assert
            anyTask.Result.Should().Be.True();
            countTask.Result.Should().Be.GreaterThan(0);
            firstTask.Result.Should().Not.Be.Null();
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void AnyAsync_WithDefaultCancellationToken_Executes()
        {
            //act
            var result = _context.Query<PersonInheritanceTest>()
                .AnyAsync(x => x.CommonName != null, cancellationToken: default).Result;

            //assert
            result.Should().Be.True();
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void AnyAsync_WithoutPredicate_AndCancellationToken_Executes()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .AnyAsync(cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .Any();

            //assert
            resultWithToken.Should().Be.EqualTo(resultWithoutToken);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void CountAsync_WithoutPredicate_AndCancellationToken_Executes()
        {
            //arrange
            var cts = new CancellationTokenSource();

            //act
            var resultWithToken = _context.Query<PersonInheritanceTest>()
                .CountAsync(cancellationToken: cts.Token).Result;
            
            var resultWithoutToken = _context.Query<PersonInheritanceTest>()
                .Count();

            //assert
            resultWithToken.Should().Be.EqualTo(resultWithoutToken);
        }

    }
}
