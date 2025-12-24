using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace LinqToLdap
{
    internal class SelectProjection
    {
        public SelectProjection(IDictionary<string, string> selectedProperties, LambdaExpression projection)
        {
            SelectedProperties = selectedProperties;

            Projection = projection.Compile();

            ReturnType = projection.ReturnType;
        }

        public IDictionary<string, string> SelectedProperties { get; private set; }
        public Delegate Projection { get; private set; }
        public Type ReturnType { get; private set; }
    }
}