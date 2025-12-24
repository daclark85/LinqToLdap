using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace LinqToLdap.Visitors
{
    internal class BooleanRewriter : System.Linq.Expressions.ExpressionVisitor
    {
        private HashSet<Expression> _candidates;

        private static bool CanBeEvaluatedLocally(Expression expression)
        {
            if (expression.NodeType == ExpressionType.Constant)
            {
                return !(((ConstantExpression)expression).Value is IQueryable);
            }
            if (expression.NodeType == ExpressionType.Conditional)
            {
                return true;
            }

            return expression.NodeType != ExpressionType.Parameter;
        }

        public Expression Rewrite(Expression expression)
        {
            _candidates = new Nominator(CanBeEvaluatedLocally).Nominate(expression);

            return Visit(expression);
        }

        public override Expression Visit(Expression exp)
        {
            if (exp == null)
            {
                return null;
            }
            return _candidates.Contains(exp)
                ? Evaluate(exp)
                : base.Visit(exp);
        }

        private static Expression Evaluate(Expression e)
        {
            return ReduceToBool(e, out _);
        }

        private static Expression ReduceToBool(Expression e, out bool canBeReduced)
        {
            switch (e.NodeType)
            {
                case ExpressionType.Constant:
                    canBeReduced = true;
                    return e;

                case ExpressionType.Lambda:
                    if (e is LambdaExpression lambdaExpr && lambdaExpr.Body.NodeType == ExpressionType.Conditional)
                    {
                        return ReduceToBool(lambdaExpr.Body, out canBeReduced);
                    }
                    canBeReduced = false;
                    return e;

                case ExpressionType.Quote:
                    return ReduceToBool(StripQuotes(e), out canBeReduced);

                case ExpressionType.ArrayLength:
                    canBeReduced = true;
                    return e;

                case ExpressionType.Not:
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                case ExpressionType.TypeAs:

                    ReduceToBool(((UnaryExpression)e).Operand, out canBeReduced);

                    if (canBeReduced)
                    {
                        var unaryLambda = Expression.Lambda(e);
                        var unaryFn = unaryLambda.Compile();
                        return Expression.Constant(unaryFn.DynamicInvoke(null), e.Type);
                    }
                    break;

                case ExpressionType.TypeIs:

                    ReduceToBool(((TypeBinaryExpression)e).Expression, out canBeReduced);

                    if (canBeReduced)
                    {
                        var typeIsLambda = Expression.Lambda(e);
                        var typeIsFn = typeIsLambda.Compile();
                        return Expression.Constant(typeIsFn.DynamicInvoke(null), e.Type);
                    }
                    break;

                case ExpressionType.Add:
                case ExpressionType.AddChecked:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.And:
                case ExpressionType.AndAlso:
                case ExpressionType.Or:
                case ExpressionType.OrElse:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.Coalesce:
                case ExpressionType.ArrayIndex:
                case ExpressionType.RightShift:
                case ExpressionType.LeftShift:
                case ExpressionType.ExclusiveOr:
                    var binary = (BinaryExpression)e;

                    ReduceToBool(binary.Left, out bool left);

                    if (left)
                    {
                        ReduceToBool(binary.Right, out bool right);
                        if (right)
                        {
                            canBeReduced = true;
                            var binaryLambda = Expression.Lambda(e);
                            var binaryFn = binaryLambda.Compile();
                            return Expression.Constant(binaryFn.DynamicInvoke(null), e.Type);
                        }
                    }
                    break;

                case ExpressionType.Conditional:
                    var conditional = (ConditionalExpression)e;

                    var constant = ReduceToBool(conditional.Test, out bool test) as ConstantExpression;

                    if (test && constant != null)
                    {
                        if (true.Equals(constant.Value))
                        {
                            return ReduceToBool(conditional.IfTrue, out canBeReduced);
                        }
                        if (false.Equals(constant.Value))
                        {
                            return ReduceToBool(conditional.IfFalse, out canBeReduced);
                        }
                    }
                    break;

                case ExpressionType.Call:
                    var methodCall = (MethodCallExpression)e;

                    // Only try to reduce if the call has no dependencies on query parameters
                    if (!ContainsParameterReference(methodCall))
                    {
                        try
                        {
                            canBeReduced = true;
                            var callLambda = Expression.Lambda(e);
                            var callFn = callLambda.Compile();
                            return Expression.Constant(callFn.DynamicInvoke(null), e.Type);
                        }
                        catch
                        {
                            // If compilation/invocation fails, can't reduce
                            canBeReduced = false;
                            return e;
                        }
                    }
                    canBeReduced = false;
                    return e;

                case ExpressionType.MemberAccess:
                    var member = (MemberExpression)e;

                    bool isNullable = member.Member.DeclaringType.Name != "Nullable`1";

                    if (member.Type == typeof(bool) && ((isNullable && member.Member.Name == "HasValue") || !isNullable))
                    {
                        canBeReduced = true;
                        var memberLambda = Expression.Lambda(e);
                        var memberFn = memberLambda.Compile();
                        return Expression.Constant(memberFn.DynamicInvoke(null), e.Type);
                    }
                    break;
            }

            canBeReduced = false;
            return e;
        }

        private static Expression StripQuotes(Expression e)
        {
            while (e.NodeType == ExpressionType.Quote)
            {
                e = ((UnaryExpression)e).Operand;
            }
            return e;
        }

        private static bool ContainsParameterReference(Expression expression)
        {
            if (expression == null)
                return false;

            if (expression.NodeType == ExpressionType.Parameter)
                return true;

            return expression switch
            {
                BinaryExpression binary => ContainsParameterReference(binary.Left) || ContainsParameterReference(binary.Right),
                UnaryExpression unary => ContainsParameterReference(unary.Operand),
                MethodCallExpression methodCall => (methodCall.Object != null && ContainsParameterReference(methodCall.Object)) || methodCall.Arguments.Any(ContainsParameterReference),
                MemberExpression member => ContainsParameterReference(member.Expression),
                LambdaExpression lambda => ContainsParameterReference(lambda.Body),
                _ => false
            };
        }
    }
}