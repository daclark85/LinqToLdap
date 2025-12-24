using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace LinqToLdap.Visitors
{
    internal class SelectProjector : System.Linq.Expressions.ExpressionVisitor
    {
        protected readonly IDictionary<string, string> MappedProperties;
        protected readonly IDictionary<string, string> Properties;
        protected SelectProjection Projection;

        public SelectProjector(IDictionary<string, string> mappedProperties)
        {
            Properties = new Dictionary<string, string>();
            MappedProperties = mappedProperties;
        }

        public virtual SelectProjection ProjectProperties(LambdaExpression p)
        {
            Visit(p);
            Projection = Properties.Count == 0
                              ? new SelectProjection(MappedProperties, p)
                              : new SelectProjection(Properties, p);
            return Projection;
        }

        protected override Expression VisitMember(MemberExpression m)
        {
            if (m.Expression != null && m.Expression.NodeType is ExpressionType.Parameter or ExpressionType.TypeAs or ExpressionType.Convert)
            {
                Properties[m.Member.Name] = MappedProperties.TryGetValue(m.Member.Name, out string name) ? name : m.Member.Name;

                return m;
            }
            throw new NotSupportedException($"The member '{m.Member.Name}' is not supported");
        }
    }
}