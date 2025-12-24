using System.Linq;
using System.Linq.Expressions;

namespace LinqToLdap
{
    internal interface IAsyncQueryProvider : IQueryProvider
    {

        System.Threading.Tasks.Task<object> ExecuteAsync(Expression expression);

        System.Threading.Tasks.Task<TResult> ExecuteAsync<TResult>(Expression expression);

    }
}