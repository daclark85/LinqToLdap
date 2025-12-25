using LinqToLdap.Logging;
using LinqToLdap.Mapping;
using LinqToLdap.QueryCommands;
using LinqToLdap.Visitors;
using System;
using System.DirectoryServices.Protocols;
using System.Linq.Expressions;

namespace LinqToLdap
{
    internal class DirectoryQueryProvider : QueryProvider, IDisposable
    {
        private bool _disposed;
        private IObjectMapping _mapping;
        private readonly SearchScope _scope;

        private WeakReference<LdapConnection> _connection;

        private readonly bool _pagingEnabled;

        public DirectoryQueryProvider(LdapConnection connection, SearchScope scope, IObjectMapping mapping, bool pagingEnabled)
        {
            ArgumentNullException.ThrowIfNull(connection, nameof(connection));
            ArgumentNullException.ThrowIfNull(mapping, nameof(mapping));

            _scope = scope;

            _connection = new WeakReference<LdapConnection>(connection);

            _mapping = mapping;
            _pagingEnabled = pagingEnabled;
        }

        public ILinqToLdapLogger Log { private get; set; }

        public bool IsDynamic { private get; set; }

        public int MaxPageSize { get; set; }

        public string NamingContext { get; set; }

        private IQueryCommand TranslateExpression(Expression expression)
        {
            if (Log != null && Log.TraceEnabled) Log.Trace("Expression: " + expression);

            var translator = new QueryTranslator(_mapping) { IsDynamic = IsDynamic };
            return translator.Translate(expression);
        }

        public override string GetQueryText(Expression expression)
        {
            var translated = TranslateExpression(expression);

            return translated.ToString();
        }

        public override object Execute(Expression expression)
        {
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var command = TranslateExpression(expression);

                LdapConnection connection;

                if (!_connection.TryGetTarget(out connection))
                {
                    throw new ObjectDisposedException("_connection", "The LdapConnection associated with this provider has been disposed.");
                }

                return command.Execute(connection, _scope, MaxPageSize, _pagingEnabled, Log, NamingContext);
            }
            catch (Exception ex)
            {
                if (Log != null) Log.Error(ex);
                throw;
            }
        }

        public override async System.Threading.Tasks.Task<object> ExecuteAsync(Expression expression)
        {
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var command = TranslateExpression(expression);

                LdapConnection connection;
                if (!_connection.TryGetTarget(out connection))
                {
                    throw new ObjectDisposedException("_connection", "The LdapConnection associated with this provider has been disposed.");
                }
                return await command.ExecuteAsync(connection, _scope, MaxPageSize, _pagingEnabled, Log, NamingContext).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (Log != null) Log.Error(ex);
                throw;
            }
        }

        ~DirectoryQueryProvider()
        {
            Dispose(false);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            _disposed = true;
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            _mapping = null;
            _connection = null;
            Log = null;
        }
    }
}