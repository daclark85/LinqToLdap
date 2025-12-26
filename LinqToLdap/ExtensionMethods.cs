using LinqToLdap.Logging;
using LinqToLdap.Mapping;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;

namespace LinqToLdap
{
    ///<summary>
    /// Class containing useful extension methods.
    ///</summary>
    public static class ExtensionMethods
    {
        /// <summary>
        /// Converts a <see cref="Guid"/> to a string octect.
        /// </summary>
        /// <param name="guid">Original <see cref="Guid"/></param>
        /// <returns></returns>
        public static string ToStringOctet(this Guid guid)
        {
            return guid.ToByteArray().ToStringOctet();
        }

        /// <summary>
        /// Converts a <see cref="Guid"/> to a string octect.
        /// </summary>
        /// <param name="bytes">Original <see cref="byte"/> array</param>
        /// <returns></returns>
        public static string ToStringOctet(this byte[] bytes)
        {
            Span<char> chars = stackalloc char[bytes.Length * 3];
            int pos = 0;

            foreach (var b in bytes)
            {
                chars[pos++] = '\\';
                b.TryFormat(chars[pos..], out int written, "x2");
                pos += written;
            }

            return new string(chars[..pos]);
        }

        #region DateTime Extensions

        internal const string LdapFormat = "yyyyMMddHHmmss.0Z";

        internal static DateTime FormatLdapDateTime(this object obj, string format)
        {
            var value = DateTimeOffset.ParseExact(obj.ToString(), format, DateTimeFormatInfo.InvariantInfo).DateTime;
            return value;
        }

        /// <summary>
        /// Converts a date time to a string..
        /// </summary>
        /// <param name="dateTime">The original date</param>
        /// <param name="format">The format of the date</param>
        /// <example>
        /// yyyyMMddHHmmss.0Z
        /// </example>
        /// <exception cref="FormatException">
        /// </exception>
        /// <returns></returns>
        public static string FormatLdapDateTime(this DateTime dateTime, string format)
        {
            var value = dateTime.ToString(format, DateTimeFormatInfo.InvariantInfo);
            return value;
        }

        #endregion DateTime Extensions


        /// <summary>
        /// Converts a dictionary to a <see cref="System.Collections.ObjectModel.ReadOnlyDictionary{K,V}"/>
        /// </summary>
        /// <typeparam name="TKey">Key type</typeparam>
        /// <typeparam name="TValue">Value type</typeparam>
        /// <param name="dictionary">The original dictionary</param>
        /// <returns></returns>
        public static System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dictionary)
        {
            return new System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>(dictionary);
        }


        /// <summary>
        /// Indicates if the <paramref name="type"/> is for an anonymous type.
        /// </summary>
        /// <param name="type">The type to check</param>
        /// <returns></returns>
        public static bool IsAnonymous(this Type type)
        {
            var isAnonymousType = type.Name.Contains("AnonymousType") &&
                type.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Any() &&
                type.IsSealed;

            return isAnonymousType;
        }

        internal static bool HasDirectorySchema(this Type type)
        {
            var attributes = type.GetCustomAttributes(typeof(DirectorySchemaAttribute), true);
            return attributes != null && attributes.Length > 0;
        }

        internal static bool IsNullOrEmpty(this string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }

        internal static void AssertSuccess(this DirectoryResponse response)
        {
            if (response == null)
            {
                throw new LdapException("Null response returned from server.");
            }
            if (response.ResultCode != ResultCode.Success)
            {
                throw new LdapException(response.ToLogString());
            }
        }

        // Pre-compute the search logic for hardware acceleration (SIMD)
        private static readonly SearchValues<char> SpecialChars = SearchValues.Create("\\*()&:|~! \0");
        /// <summary>
        /// Cleans special characters for an LDAP filter.  This method cannot clean a distinguished name.
        /// </summary>
        /// <param name="value">The value to clean</param>
        /// <returns></returns>
        public static string CleanFilterValue(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            ReadOnlySpan<char> span = value.AsSpan();
            int firstIndex = span.IndexOfAny(SpecialChars);

            // FAST PATH: If no special characters found, return original string (Zero Allocation)
            if (firstIndex == -1) return value;

            // SLOW PATH: We need to escape. 
            // Estimate buffer size: worst case is 3x the original length.
            int initialSize = value.Length * 3;

            // Use stackalloc for strings up to ~256 chars to avoid heap allocation
            char[]? arrayFromPool = null;
            Span<char> buffer = initialSize <= 512
                ? stackalloc char[512]
                : (arrayFromPool = ArrayPool<char>.Shared.Rent(initialSize));

            try
            {
                int pos = 0;

                // Copy the clean part before the first special character
                span[..firstIndex].CopyTo(buffer);
                pos = firstIndex;

                // Process the rest
                for (int i = firstIndex; i < span.Length; i++)
                {
                    char c = span[i];
                    string? replacement = c switch
                    {
                        '\\' => "\\5c",
                        '*' => "\\2a",
                        '(' => "\\28",
                        ')' => "\\29",
                        '&' => "\\26",
                        ':' => "\\3a",
                        '|' => "\\7c",
                        '~' => "\\7e",
                        '!' => "\\21",
                        '\0' => "\\00",
                        _ => null
                    };

                    if (replacement != null)
                    {
                        replacement.AsSpan().CopyTo(buffer[pos..]);
                        pos += 3;
                    }
                    else
                    {
                        buffer[pos++] = c;
                    }
                }

                return new string(buffer[..pos]);
            }
            finally
            {
                if (arrayFromPool != null) ArrayPool<char>.Shared.Return(arrayFromPool);
            }
        }

        /// <summary>
        /// Attempts to convert the object from a .Net type to an LDAP string or byte[].
        /// If <paramref name="obj"/> is null or <see cref="String.Empty"/> then no value is added to the <see cref="DirectoryAttributeModification"/>.
        /// </summary>
        /// <param name="obj">The value to convert.</param>
        /// <param name="attributeName">The name of the attribute.</param>
        /// <param name="operation">The type of <see cref="DirectoryAttributeOperation"/>.</param>
        /// <returns></returns>
        public static DirectoryAttributeModification ToDirectoryModification(this object obj, string attributeName, DirectoryAttributeOperation operation)
        {
            var modification = new DirectoryAttributeModification { Name = attributeName, Operation = operation };

            if (obj == null || string.Empty.Equals(obj)) return modification;

            if (obj is string)
            {
                modification.Add(obj as string);
                return modification;
            }

            if (obj is IEnumerable<string>)
            {
                foreach (var s in obj as IEnumerable<string>)
                {
                    modification.Add(s);
                }

                return modification;
            }
            if (obj is byte[])
            {
                modification.Add(obj as byte[]);
                return modification;
            }
            if (obj is X509Certificate)
            {
                modification.Add((obj as X509Certificate).GetRawCertData());
                return modification;
            }
            if (obj is IEnumerable<byte>)
            {
                modification.Add((obj as IEnumerable<byte>).ToArray());
                return modification;
            }
            if (obj is SecurityIdentifier)
            {
                var sid = obj as SecurityIdentifier;
                var bytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(bytes, 0);
                modification.Add(bytes);
                return modification;
            }
            if (obj is IEnumerable<byte[]>)
            {
                foreach (var b in (obj as IEnumerable<byte[]>).Where(b => b != null))
                {
                    modification.Add(b);
                }
                return modification;
            }
            if (obj is IEnumerable<X509Certificate>)
            {
                foreach (var b in (obj as IEnumerable<X509Certificate>).Where(c => c != null))
                {
                    modification.Add(b.GetRawCertData());
                }
                return modification;
            }
            if (obj is IEnumerable)
            {
                foreach (var s in (from object item in (obj as IEnumerable) select item.ToString()))
                {
                    modification.Add(s);
                }
                return modification;
            }
            if (obj is Guid)
            {
                modification.Add(((Guid)obj).ToByteArray());
                return modification;
            }
            if (obj is bool boolean)
            {
                modification.Add(boolean ? "TRUE" : "FALSE");

                return modification;
            }

            modification.Add(obj.ToString());

            return modification;
        }

        internal static DirectoryAttribute ToDirectoryAttribute(this object obj, string attributeName)
        {
            return ToDirectoryModification(obj, attributeName, DirectoryAttributeOperation.Replace);
        }

        internal static IEnumerable<SearchResultEntry> GetRange(this SearchResultEntryCollection collection)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                yield return collection[i];
            }
        }
    }
}