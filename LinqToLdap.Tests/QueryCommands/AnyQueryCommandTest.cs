using System.DirectoryServices.Protocols;
using System.Xml;
using LinqToLdap.Mapping;
using LinqToLdap.QueryCommands;
using LinqToLdap.QueryCommands.Options;
using LinqToLdap.Tests.TestSupport;
using LinqToLdap.Tests.TestSupport.ExtensionMethods;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpTestsEx;

namespace LinqToLdap.Tests.QueryCommands
{
    [TestClass]
    public class AnyQueryCommandTest
    {
        [TestMethod]
        public void Execute_NoEntries_ReturnsFalse()
        {
            //prepare
            var options = new Mock<IQueryCommandOptions>();
            var connection = new MockLdapConnection();
            var xmlNode = new XmlDocument();
            var mapping = new Mock<IObjectMapping>();
            mapping.Setup(m => m.NamingContext)
                .Returns("nm");
            var command = new AnyQueryCommand(options.Object, mapping.Object);
            object[] parameters;

            parameters = new object[] { "", new System.DirectoryServices.Protocols.DirectoryControl[0], System.DirectoryServices.Protocols.ResultCode.Success, "", new System.Uri[0] };

            var response = typeof(SearchResponse).Create<SearchResponse>(parameters);

            connection.RequestResponses.Add(typeof(SearchRequest), response);

            //act
            var result = command.Execute(connection, SearchScope.Subtree, 1, true);

            //assert
            var request = connection.SentRequests[0];
            request.Controls[0].As<PageResultRequestControl>().PageSize.Should().Be.EqualTo(1);
            request.As<SearchRequest>().Scope.Should().Be.EqualTo(SearchScope.Subtree);
            request.As<SearchRequest>().Attributes.Count.Should().Be.EqualTo(1);
            request.As<SearchRequest>().Attributes.Contains("cn").Should().Be.True();
            request.As<SearchRequest>().TypesOnly.Should().Be.True();

            result.Should().Be.EqualTo(false);
        }

        [TestMethod]
        public void Execute_PagingDisabled_HasNoPageResultRequestControl()
        {
            //prepare
            var options = new Mock<IQueryCommandOptions>();
            var connection = new MockLdapConnection();
            var xmlNode = new XmlDocument();
            var mapping = new Mock<IObjectMapping>();
            mapping.Setup(m => m.NamingContext)
                .Returns("nm");
            var command = new AnyQueryCommand(options.Object, mapping.Object);
            object[] parameters;

            parameters = new object[] { "", new System.DirectoryServices.Protocols.DirectoryControl[0], System.DirectoryServices.Protocols.ResultCode.Success, "", new System.Uri[0] };

            var response = typeof(SearchResponse).Create<SearchResponse>(parameters);

            connection.RequestResponses.Add(typeof(SearchRequest), response);

            //act
            var result = command.Execute(connection, SearchScope.Subtree, 1, false);

            //assert
            var request = connection.SentRequests[0];
            request.Controls.Count.Should().Be.EqualTo(0);
            request.As<SearchRequest>().Scope.Should().Be.EqualTo(SearchScope.Subtree);
            request.As<SearchRequest>().Attributes.Count.Should().Be.EqualTo(1);
            request.As<SearchRequest>().Attributes.Contains("cn").Should().Be.True();
            request.As<SearchRequest>().TypesOnly.Should().Be.True();

            result.Should().Be.EqualTo(false);
        }

        [TestMethod]
        public void Execute_OneEntry_ReturnsTrue()
        {
            //prepare
            var options = new Mock<IQueryCommandOptions>();
            var connection = new MockLdapConnection();
            var xmlNode = new XmlDocument();
            var mapping = new Mock<IObjectMapping>();
            mapping.Setup(m => m.NamingContext)
                .Returns("nm");
            var command = new AnyQueryCommand(options.Object, mapping.Object);
            object[] parameters;

            parameters = new object[] { "", new System.DirectoryServices.Protocols.DirectoryControl[0], System.DirectoryServices.Protocols.ResultCode.Success, "", new System.Uri[0] };

            var response = typeof(SearchResponse).Create<SearchResponse>(parameters);

            connection.RequestResponses.Add(typeof(SearchRequest), response);
            var collection =
                typeof(SearchResultEntryCollection).Create<SearchResultEntryCollection>();
            var entry = typeof(SearchResultEntry).Create<SearchResultEntry>("dn");
            collection.Call("Add", entry);

            response.Call("set_Entries", collection);

            //act
            var result = command.Execute(connection, SearchScope.Subtree, 1, true);

            //assert
            var request = connection.SentRequests[0];
            request.Controls[0].As<PageResultRequestControl>().PageSize.Should().Be.EqualTo(1);
            request.As<SearchRequest>().Scope.Should().Be.EqualTo(SearchScope.Subtree);
            request.As<SearchRequest>().Attributes.Count.Should().Be.EqualTo(1);
            request.As<SearchRequest>().Attributes.Contains("cn").Should().Be.True();
            request.As<SearchRequest>().TypesOnly.Should().Be.True();

            result.Should().Be.EqualTo(true);
        }

        [TestMethod]
        public void Execute_OneEntryAndCustomNamingContext_ReturnsTrue()
        {
            //prepare
            var options = new Mock<IQueryCommandOptions>();
            var connection = new MockLdapConnection();
            var xmlNode = new XmlDocument();
            var mapping = new Mock<IObjectMapping>();
            var command = new AnyQueryCommand(options.Object, mapping.Object);
            object[] parameters;

            parameters = new object[] { "", new System.DirectoryServices.Protocols.DirectoryControl[0], System.DirectoryServices.Protocols.ResultCode.Success, "", new System.Uri[0] };

            var response = typeof(SearchResponse).Create<SearchResponse>(parameters);
            connection.RequestResponses.Add(typeof(SearchRequest), response);
            var collection =
                typeof(SearchResultEntryCollection).Create<SearchResultEntryCollection>();
            var entry = typeof(SearchResultEntry).Create<SearchResultEntry>("dn");
            collection.Call("Add", entry);

            response.Call("set_Entries", collection);

            //act
            var result = command.Execute(connection, SearchScope.Subtree, 1, true, null, "nc");

            //assert
            var request = connection.SentRequests[0];
            command.FieldValueEx<SearchRequest>("SearchRequest").DistinguishedName.Should().Be.EqualTo("nc");
            request.Controls[0].As<PageResultRequestControl>().PageSize.Should().Be.EqualTo(1);
            request.As<SearchRequest>().Scope.Should().Be.EqualTo(SearchScope.Subtree);
            request.As<SearchRequest>().Attributes.Count.Should().Be.EqualTo(1);
            request.As<SearchRequest>().Attributes.Contains("cn").Should().Be.True();
            request.As<SearchRequest>().TypesOnly.Should().Be.True();

            result.Should().Be.EqualTo(true);
        }
    }
}