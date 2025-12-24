using LinqToLdap.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace LinqToLdap.Visitors
{
    internal class DynamicSelectProjector : SelectProjector
    {
        public DynamicSelectProjector(IDictionary<string, string> mappedProperties) : base(mappedProperties)
        {
        }

        protected override Expression VisitMember(MemberExpression m)
        {
            if (m.Expression is MethodCallExpression methodCall)
            {
                if (methodCall.Method.DeclaringType == typeof(IDirectoryAttributes))
                {
                    VisitMethodCall(methodCall);
                }
            }
            else if (m.Member.Name == "DistinguishedName")
            {
                Properties["DistinguishedName"] = "DistinguishedName";
            }
            return m;
        }

        protected override Expression VisitMethodCall(MethodCallExpression m)
        {
            if (m.Method.DeclaringType == typeof(IDirectoryAttributes) && 
                m.Method.Name.StartsWith("Get") &&
                m.Arguments.Count > 0 && 
                m.Arguments[0] is ConstantExpression constantArg)
            {
                var value = constantArg.Value.ToString();
                Properties[value] = value;
                return m;
            }

            return base.VisitMethodCall(m);
        }
    }
}