using Dante.Extensions;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Z3;

namespace Dante.Asserts;

public static class ExpressionAsserts
{
    private const string RequirementFormat = "expression '{0}' was not compiled as '{1}' expression";

    private static void Requires<T>(AST operand, CSharpSyntaxNode sourceRepresentation, string type)
    {
        operand.Should().BeOfType<T>(
            string.Format(RequirementFormat, sourceRepresentation.GetSyntaxNodeText(), type));
    }

    public static void RequiresBooleanOperand(Expr operand, CSharpSyntaxNode sourceRepresentation)
    {
        Requires<BoolExpr>(operand, sourceRepresentation, "Boolean");
    }
}