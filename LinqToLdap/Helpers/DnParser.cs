using System;
using System.Collections.Generic;
using System.Linq;

namespace LinqToLdap.Helpers
{
    /// <summary>
    /// Static class for parsing distinguished name information
    /// </summary>
    public static class DnParser
    {
        /// <summary>
        /// Parses the first name of an entry without the RDN prefix (CN, OU, etc.) from <paramref name="distinguishedName"/> and returns that value.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name to parse.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="distinguishedName"/> is null, empty, white space, or not a valid distinguished name.</exception>
        /// <returns></returns>
        public static string ParseName(string distinguishedName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName, nameof(distinguishedName));

            var span = distinguishedName.AsSpan();
            int firstEquals = span.IndexOf('=');

            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(firstEquals, 0, nameof(distinguishedName));

            // Find the first unescaped comma after the equals sign
            for (int i = firstEquals + 1; i < span.Length; i++)
            {
                if (span[i] == '\\' && i + 1 < span.Length)
                {
                    i++; // Skip escaped character
                    continue;
                }

                if (span[i] == ',')
                {
                    // Found the end of the first RDN value
                    return span[(firstEquals + 1)..i].ToString();
                }
            }

            // No comma found - entire remaining string is the name
            return span[(firstEquals + 1)..].ToString();
        }

        /// <summary>
        /// Parses the first RDN attribute type.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name.</param>
        /// <example>
        /// OU=Test,DC=local returns OU
        /// </example>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="distinguishedName"/> is null, empty, white space, or has an invalid format.</exception>
        public static string ParseRDN(string distinguishedName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName, nameof(distinguishedName));

            var span = distinguishedName.AsSpan();
            int equalsIndex = span.IndexOf('=');

            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(equalsIndex, 0, nameof(distinguishedName));

            return span[..equalsIndex].ToString();
        }

        /// <summary>
        /// Extracts the complete first RDN component (including attribute type and value) from a distinguished name.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name to parse.</param>
        /// <returns>
        /// The first RDN component of the distinguished name (e.g., "CN=John Doe"). If the DN has only one component,
        /// returns the entire <paramref name="distinguishedName"/> unchanged.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="distinguishedName"/> is null, empty, white space, or has an invalid format.
        /// </exception>
        /// <remarks>
        /// This method returns the full first RDN including the attribute type (CN=, OU=, etc.), unlike <see cref="ParseName"/>
        /// which returns only the value portion. It identifies the boundary by finding the comma that separates the first
        /// RDN from subsequent components.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Extract first RDN from a user DN
        /// var entryName1 = DnHelper.GetEntryName("CN=John Doe,OU=Users,DC=example,DC=com");
        /// // Returns: "CN=John Doe"
        /// 
        /// // Extract first RDN from an OU
        /// var entryName2 = DnHelper.GetEntryName("OU=Marketing,OU=Departments,DC=example,DC=com");
        /// // Returns: "OU=Marketing"
        /// 
        /// // Single component DN
        /// var entryName3 = DnHelper.GetEntryName("DC=com");
        /// // Returns: "DC=com"
        /// 
        /// // Compare with ParseName (returns value only)
        /// var name = DnHelper.ParseName("CN=John Doe,OU=Users,DC=example,DC=com");
        /// // Returns: "John Doe" (without "CN=")
        /// </code>
        /// </example>
        public static string GetEntryName(string distinguishedName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName, nameof(distinguishedName));

            int firstEquals = distinguishedName.IndexOf('=');

            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(firstEquals, 0, nameof(distinguishedName));

            int secondEquals = distinguishedName.IndexOf('=', firstEquals + 1);

            if (secondEquals <= 0)
            {
                return distinguishedName;
            }

            string sub = distinguishedName.Substring(firstEquals, secondEquals);
            int lastComma = sub.LastIndexOf(',');

            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lastComma, 0, nameof(distinguishedName));

            return distinguishedName.Substring(0, firstEquals + lastComma);
        }

        /// <summary>
        /// Extracts the parent container portion of a distinguished name (everything after the first RDN component).
        /// </summary>
        /// <param name="distinguishedName">The distinguished name to parse.</param>
        /// <returns>
        /// The container portion of the distinguished name. If the DN has only one component (no parent container),
        /// returns the entire <paramref name="distinguishedName"/> unchanged.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="distinguishedName"/> is null, empty, white space, or has an invalid format.
        /// </exception>
        /// <remarks>
        /// This method extracts the parent container by finding the first comma that separates the entry's RDN
        /// from its parent container path. It properly handles multi-component DNs by identifying where the
        /// first RDN ends and the container begins.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Extract parent container from a user DN
        /// var container1 = DnParser.GetEntryContainer("CN=John Doe,OU=Users,DC=example,DC=com");
        /// // Returns: "OU=Users,DC=example,DC=com"
        /// 
        /// // Extract parent container from an OU
        /// var container2 = DnParser.GetEntryContainer("OU=Marketing,OU=Departments,DC=example,DC=com");
        /// // Returns: "OU=Departments,DC=example,DC=com"
        /// 
        /// // Single component DN (no container)
        /// var container3 = DnParser.GetEntryContainer("DC=com");
        /// // Returns: "DC=com"
        /// </code>
        /// </example>
        public static string GetEntryContainer(string distinguishedName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName, nameof(distinguishedName));

            if (!distinguishedName.Contains('='))
            {
                throw new ArgumentException($"Common name could not be parsed from distinguished name '{distinguishedName}'.", nameof(distinguishedName));
            }

            var parts = Split(distinguishedName);

            if (parts.Count <= 1)
            {
                return distinguishedName;
            }

            // Join all parts except the first (the entry RDN)
            return string.Join(",", parts.Skip(1));
        }

        /// <summary>
        /// Formats a name by prepending the RDN attribute type from a reference distinguished name if not already present.
        /// </summary>
        /// <param name="name">The name to format. Can be a simple name (e.g., "John Doe") or a full RDN (e.g., "CN=John Doe").</param>
        /// <param name="currentDistinguishedName">The reference distinguished name to extract the RDN attribute type from.</param>
        /// <returns>
        /// Returns <paramref name="name"/> unchanged if it already contains an equals sign (=), indicating it's already formatted.
        /// Otherwise, returns the name prefixed with the RDN attribute type from <paramref name="currentDistinguishedName"/>.
        /// </returns>
        /// <remarks>
        /// This method is useful for ensuring a name value has the proper RDN prefix (CN=, OU=, etc.) by copying
        /// the attribute type from an existing distinguished name.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Name already has RDN prefix - returned unchanged
        /// var result1 = DnParser.FormatName("CN=John Doe", "CN=Jane Smith,OU=Users,DC=example,DC=com");
        /// // Returns: "CN=John Doe"
        /// 
        /// // Name without prefix - gets CN= from reference DN
        /// var result2 = DnParser.FormatName("John Doe", "CN=Jane Smith,OU=Users,DC=example,DC=com");
        /// // Returns: "CN=John Doe"
        /// 
        /// // Name without prefix - gets OU= from reference DN
        /// var result3 = DnParser.FormatName("Marketing", "OU=Sales,OU=Departments,DC=example,DC=com");
        /// // Returns: "OU=Marketing"
        /// </code>
        /// </example>
        public static string FormatName(string name, string currentDistinguishedName)
        {
            if (name.Contains('='))
                return name;

            int index = currentDistinguishedName.IndexOf('=');
            if (index < 0)
                return name;

            int prefixLength = index + 1;
            return string.Create(prefixLength + name.Length, (currentDistinguishedName, name, prefixLength),
                (span, state) =>
                {
                    state.currentDistinguishedName.AsSpan(0, state.prefixLength).CopyTo(span);
                    state.name.AsSpan().CopyTo(span.Slice(state.prefixLength));
                });
        }

        /// <summary>
        /// Splits a distinguished name into its component parts, respecting LDAP escape sequences.
        /// </summary>
        /// <param name="distinguishedName">The distinguished name to split.</param>
        /// <returns>A list of DN components. Returns an empty list if the input is null or empty.</returns>
        /// <remarks>
        /// This method properly handles LDAP escape sequences where a backslash (\) escapes the following character.
        /// For example, "CN=Doe\, John,OU=Users,DC=example,DC=com" is split into:
        /// ["CN=Doe\, John", "OU=Users", "DC=example", "DC=com"]
        /// </remarks>
        /// <example>
        /// <code>
        /// var parts = DnParser.Split("CN=John Doe,OU=Users,DC=example,DC=com");
        /// // Returns: ["CN=John Doe", "OU=Users", "DC=example", "DC=com"]
        /// 
        /// var escaped = DnParser.Split("CN=Doe\\, John,OU=Users");
        /// // Returns: ["CN=Doe\, John", "OU=Users"]
        /// </code>
        /// </example>
        public static List<string> Split(string distinguishedName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName, nameof(distinguishedName));

            var span = distinguishedName.AsSpan();
            var result = new List<string>(8);
            int start = 0;

            for (int i = 0; i < span.Length; i++)
            {
                char c = span[i];

                if (c == '\\' && i + 1 < span.Length)
                {
                    i++; // Skip next character
                }
                else if (c == ',')
                {
                    result.Add(span[start..i].ToString());
                    start = i + 1;
                }
            }

            // Add remaining segment
            if (start <= span.Length)
            {
                result.Add(span[start..].ToString());
            }

            return result;
        }
    }
}