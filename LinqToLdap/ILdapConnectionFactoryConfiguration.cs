using System.DirectoryServices.Protocols;
using System.Net;

namespace LinqToLdap
{
    /// <summary>
    /// Interface for configuring a <see cref="ILdapConnectionFactory"/>.
    /// </summary>
    public interface ILdapConnectionFactoryConfiguration
    {
        /// <summary>
        /// Allows you to specify an authentication method for the
        /// connection.  If this method is not called,  the authentication method
        /// will be resolved by the <see cref="LdapConnection"/>.
        /// </summary>
        /// <param name="authType">
        /// The type of authentication to use.
        /// </param>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration AuthenticateBy(AuthType authType);

        /// <summary>
        /// Allows you to specify credentials for the connection to use.
        /// If this method is not called,  then the <see cref="LdapConnection"/>
        /// will use the credentials of the current user.
        /// </summary>
        /// <param name="credentials">
        /// The credentials to use.
        /// </param>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration AuthenticateAs(NetworkCredential credentials);

        /// <summary>
        /// Specifies the LDAP protocol version.  The default is 3.
        /// </summary>
        /// <param name="version">The protocol version</param>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration ProtocolVersion(int version);

        /// <summary>
        /// Sets the port manually for the LDAP server.  The default is 389.
        /// </summary>
        /// <param name="port">
        /// The port to use when communicating with the LDAP server.
        /// </param>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration UsePort(int port);

        /// <summary>
        /// Sets the connection timeout period in seconds.  The default is 30 seconds.
        /// </summary>
        /// <param name="seconds">The timeout period</param>
        /// <exception cref="System.ArgumentException">Thrown if <paramref name="seconds"/> is less than or equal to 0</exception>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration ConnectionTimeoutIn(double seconds);

        /// <summary>
        /// Turns on SSL and optionally you can set the SSL port.  Default is 636.
        /// </summary>
        /// <param name="port">The optional port to use.</param>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration UseSsl(int port = ConnectionFactoryBase.SslPort);

        /// <summary>
        /// If this option is called, the server name is a fully-qualified DNS host name.
        /// Otherwise the server name can be an IP address, a DNS domain or host name.
        /// </summary>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration ServerNameIsFullyQualified();

        /// <summary>
        /// Indicates that the connections will use UDP (User Datagram Protocol).
        /// </summary>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration UseUdp();

        /// <summary>
        /// Enables sealing (encryption) for the LDAP connection. This encrypts the data sent over the connection.
        /// Requires Kerberos or NTLM authentication.
        /// </summary>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration UseSealing();

        /// <summary>
        /// Enables signing (integrity protection) for the LDAP connection. This ensures data integrity.
        /// Requires Kerberos or NTLM authentication.
        /// </summary>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration UseSigning();

        /// <summary>
        /// Disables SSL/TLS certificate validation. This allows connections to servers with self-signed 
        /// or invalid certificates.
        /// WARNING: This should ONLY be used in development/testing environments as it makes 
        /// the connection vulnerable to man-in-the-middle attacks.
        /// </summary>
        /// <returns></returns>
        ILdapConnectionFactoryConfiguration IgnoreSslCertificateErrors();
    }
}