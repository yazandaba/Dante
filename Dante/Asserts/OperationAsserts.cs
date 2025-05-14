using Dante.Extensions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Dante.Asserts;

internal static class OperationAsserts
{
    private const string RequirementFormat = "expression '{0}' is not '{1}' expression";

    public static void RequiresRelationalExpression(IBinaryOperation binaryExpression)
    {
        binaryExpression.IsRelationalExpression().Should()
            .BeTrue(string.Format(RequirementFormat, binaryExpression.GetSyntaxNodeText(), "relational"));
    }

    public static void RequiresLogicalExpression(IBinaryOperation binaryExpression)
    {
        binaryExpression.IsLogicalExpression().Should()
            .BeTrue(string.Format(RequirementFormat, binaryExpression.GetSyntaxNodeText(), "logical"));
    }

    public static void RequirePrefixOrPostfixExpression(IOperation expression)
    {
        expression.IsPrefixOrPostfixUnaryOperation().Should()
            .BeTrue(string.Format(RequirementFormat, expression.GetSyntaxNodeText(), "prefix nor postfix"));
    }
}