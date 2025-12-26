using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LinqToLdap.Async
{
    /// <summary>
    /// Async extension methods for <see cref="IDirectoryContext"/>.
    /// </summary>
    public static class QueryableAsyncExtensions
    {
        // ✅ OPTIMIZED: Single enumeration with efficient dictionary lookup
        private static readonly Dictionary<string, MethodInfo> _methodCache = InitializeMethodCache();

        private static Dictionary<string, MethodInfo> InitializeMethodCache()
        {
            // Get all public static methods once
            var methods = typeof(QueryableAsyncExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static);
            
            // Pre-allocate dictionary with exact capacity (16 methods)
            var cache = new Dictionary<string, MethodInfo>(16, StringComparer.Ordinal);

            // Group methods by name for faster lookup
            var methodGroups = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
            
            foreach (var method in methods)
            {
                if (!methodGroups.TryGetValue(method.Name, out var group))
                {
                    group = new List<MethodInfo>(2); // Most methods have 1-2 overloads
                    methodGroups[method.Name] = group;
                }
                group.Add(method);
            }

            // Build cache with composite keys: "MethodName_ParamCount"
            foreach (var kvp in methodGroups)
            {
                foreach (var method in kvp.Value)
                {
                    var paramCount = method.GetParameters().Length;
                    var key = $"{kvp.Key}_{paramCount}";
                    
                    // Store only the first match (methods are unique by name+paramCount in this class)
                    if (!cache.ContainsKey(key))
                    {
                        cache[key] = method;
                    }
                }
            }

            return cache;
        }

        // Helper method for cleaner lookups
        internal static MethodInfo GetCachedMethod(string methodName, int parameterCount)
        {
            var key = $"{methodName}_{parameterCount}";
            if (_methodCache.TryGetValue(key, out var method))
            {
                return method;
            }
            
            // This should never happen if cache is initialized correctly
            throw new InvalidOperationException(
                $"Method '{methodName}' with {parameterCount} parameters not found in cache. " +
                "This indicates a bug in QueryableAsyncExtensions initialization.");
        }

        // ✅ Replace all 16 static field initializers with efficient cache lookups
        private static readonly MethodInfo AnyAsyncMethod = GetCachedMethod("AnyAsync", 3);
        private static readonly MethodInfo AnyPredicateAsyncMethod = GetCachedMethod("AnyAsync", 4);
        private static readonly MethodInfo ToListAsyncMethod = GetCachedMethod("ToListAsync", 3);
        private static readonly MethodInfo CountAsyncMethod = GetCachedMethod("CountAsync", 3);
        private static readonly MethodInfo CountPredicateAsyncMethod = GetCachedMethod("CountAsync", 4);
        private static readonly MethodInfo LongCountAsyncMethod = GetCachedMethod("LongCountAsync", 3);
        private static readonly MethodInfo LongCountPredicateAsyncMethod = GetCachedMethod("LongCountAsync", 4);
        private static readonly MethodInfo FirstAsyncMethod = GetCachedMethod("FirstAsync", 3);
        private static readonly MethodInfo FirstPredicateAsyncMethod = GetCachedMethod("FirstAsync", 4);
        private static readonly MethodInfo FirstOrDefaultAsyncMethod = GetCachedMethod("FirstOrDefaultAsync", 3);
        private static readonly MethodInfo FirstOrDefaultPredicateAsyncMethod = GetCachedMethod("FirstOrDefaultAsync", 4);
        private static readonly MethodInfo SingleAsyncMethod = GetCachedMethod("SingleAsync", 3);
        private static readonly MethodInfo SinglePredicateAsyncMethod = GetCachedMethod("SingleAsync", 4);
        private static readonly MethodInfo SingleOrDefaultAsyncMethod = GetCachedMethod("SingleOrDefaultAsync", 3);
        private static readonly MethodInfo SingleOrDefaultPredicateAsyncMethod = GetCachedMethod("SingleOrDefaultAsync", 4);
        private static readonly MethodInfo ListAttributesAsyncMethod = GetCachedMethod("ListAttributesAsync", 4);
        private static readonly MethodInfo InPagesOfAsyncMethod = GetCachedMethod("InPagesOfAsync", 4);

        /// <summary>
        /// Executes Any on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <param name="source">The query.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <typeparam name="TSource">The element type to return.</typeparam>
        /// <returns></returns>
        public static async Task<bool> AnyAsync<TSource>(this IQueryable<TSource> source, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<bool>(
                    Expression.Call(null, AnyAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.Any();
        }

        /// <summary>
        /// Executes Any on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <param name="source">The query.</param>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <typeparam name="TSource">The element type to return.</typeparam>
        /// <returns></returns>
        public static async Task<bool> AnyAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<bool>(
                    Expression.Call(null, AnyPredicateAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, predicate, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.Any(predicate);
        }

        /// <summary>
        /// Executes <see cref="QueryableExtensions.ToList{TSource}"/> on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<List<TSource>>(
                    Expression.Call(null, ToListAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.ToList();
        }

        /// <summary>
        /// Executes Count on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<int> CountAsync<TSource>(this IQueryable<TSource> source, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<int>(
                    Expression.Call(null, CountAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.Count();
        }

        /// <summary>
        /// Executes Count on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="predicate">The condition by which to filter.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<int> CountAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<int>(
                    Expression.Call(null, CountPredicateAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, predicate, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.Count(predicate);
        }

        /// <summary>
        /// Executes LongCount on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<long> LongCountAsync<TSource>(this IQueryable<TSource> source, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<long>(
                    Expression.Call(null, LongCountAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.LongCount();
        }

        /// <summary>
        /// Executes LongCount on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="predicate">The condition by which to filter.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<long> LongCountAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<long>(
                    Expression.Call(null, LongCountPredicateAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, predicate, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.LongCount(predicate);
        }

        /// <summary>
        /// Executes FirstOrDefault on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <param name="source">The query.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <typeparam name="TSource">The element type to return.</typeparam>
        /// <returns></returns>
        public static async Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<TSource>(
                    Expression.Call(null, FirstOrDefaultAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.FirstOrDefault();
        }

        /// <summary>
        /// Executes FirstOrDefault on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <param name="source">The query</param>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <typeparam name="TSource">The element type to return.</typeparam>
        /// <returns></returns>
        public static async Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<TSource>(
                    Expression.Call(null, FirstOrDefaultPredicateAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, predicate, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.FirstOrDefault(predicate);
        }

        /// <summary>
        /// Executes First on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<TSource> FirstAsync<TSource>(this IQueryable<TSource> source, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<TSource>(
                    Expression.Call(null, FirstAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.First();
        }

        /// <summary>
        /// Executes First on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="predicate">The condition by which to filter.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<TSource> FirstAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<TSource>(
                    Expression.Call(null, FirstPredicateAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, predicate, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.First(predicate);
        }

        /// <summary>
        /// Executes Single on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<TSource> SingleAsync<TSource>(this IQueryable<TSource> source, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<TSource>(
                    Expression.Call(null, SingleAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.Single();
        }

        /// <summary>
        /// Executes Single on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="predicate">The condition by which to filter.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<TSource> SingleAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<TSource>(
                    Expression.Call(null, SinglePredicateAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, predicate, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.Single(predicate);
        }

        /// <summary>
        /// Executes SingleOrDefault on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <param name="source">The query</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <typeparam name="TSource"></typeparam>
        /// <returns></returns>
        public static async Task<TSource> SingleOrDefaultAsync<TSource>(this IQueryable<TSource> source, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<TSource>(
                    Expression.Call(null, SingleOrDefaultAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.SingleOrDefault();
        }

        /// <summary>
        /// Executes SingleOrDefault on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="predicate">The condition by which to filter.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<TSource> SingleOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<TSource>(
                    Expression.Call(null, SingleOrDefaultPredicateAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, predicate, Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.SingleOrDefault(predicate);
        }

        /// <summary>
        /// Executes <see cref="QueryableExtensions.ListAttributes{TSource}"/> on <paramref name="source"/> in a <see cref="Task"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The query.</param>
        /// <param name="attributes">The attributes to load.</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public static async Task<IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<string, object>>>>> ListAttributesAsync<TSource>(this IQueryable<TSource> source, string[] attributes = null, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<string, object>>>>>(
                    Expression.Call(null, ListAttributesAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(attributes ?? new string[0]), Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }
            return source.ListAttributes();
        }

        /// <summary>
        /// Pages through all the results and returns them in a <see cref="List{T}"/>.
        /// </summary>
        /// <param name="source">The query</param>
        /// <param name="pageSize">The size of each page</param>
        /// <param name="resultProcessing">How the async results are processed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <typeparam name="TSource">The type to query against</typeparam>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="source"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="pageSize"/> is not greater than 0</exception>
        /// <returns></returns>
        public static async Task<List<TSource>> InPagesOfAsync<TSource>(this IQueryable<TSource> source, int pageSize, 
            PartialResultProcessing resultProcessing = LdapConfiguration.DefaultAsyncResultProcessing,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));
            if (pageSize < 1) throw new ArgumentException("pageSize must be greater than 0");

            if (source.Provider is IAsyncQueryProvider asyncProvider)
            {
                return await asyncProvider.ExecuteAsync<List<TSource>>(
                    Expression.Call(null, InPagesOfAsyncMethod.MakeGenericMethod(
                        new[] { typeof(TSource) }),
                        new[] { source.Expression, Expression.Constant(pageSize), Expression.Constant(resultProcessing), Expression.Constant(cancellationToken) })).ConfigureAwait(false);
            }

            return source.InPagesOf(pageSize);
        }
    }
}