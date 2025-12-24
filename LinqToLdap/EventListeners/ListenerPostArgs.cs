using System;
using System.DirectoryServices.Protocols;

namespace LinqToLdap.EventListeners
{
    /// <summary>
    /// Arguments passed for <see cref="IEventListener"/>s. The arguments used by this class are <see cref="WeakReference"/>s.
    /// </summary>
    /// <typeparam name="TObject">The instance for the event.</typeparam>
    /// <typeparam name="TResponse">The response from the Directory.</typeparam>
    /// <typeparam name="TRequest">The full request that was sent to the server.</typeparam>
    public class ListenerPostArgs<TObject, TRequest, TResponse>
        where TRequest : DirectoryRequest
        where TResponse : DirectoryResponse
        where TObject : class
    {

        private readonly WeakReference<LdapConnection> _connection;
        private readonly WeakReference<TObject> _entry;

        internal ListenerPostArgs(TObject entry, TRequest request, TResponse response, LdapConnection connection)
        {

            _entry = new WeakReference<TObject>(entry);
            _connection = new WeakReference<LdapConnection>(connection);

            Request = request;
            Response = response;
        }

        /// <summary>
        /// The entry.
        /// </summary>
        public TObject Entry
        {
            get
            {
                TObject target;
                return _entry.TryGetTarget(out target) ? target : default;
            }
        }

        /// <summary>
        /// The <see cref="DirectoryRequest"/>.
        /// </summary>
        public TRequest Request { get; private set; }

        /// <summary>
        /// The <see cref="DirectoryResponse"/>.
        /// </summary>
        public TResponse Response { get; private set; }

        /// <summary>
        /// The connection that will be used when sending the <see cref="Request"/>.
        /// </summary>
        public LdapConnection Connection
        {
            get
            {
                LdapConnection target;
                return _connection.TryGetTarget(out target) ? target : null;
            }
        }
    }
}