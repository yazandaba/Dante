using Dante.Extensions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Z3;

namespace Dante.Asserts;

internal static class GenerationAsserts
{
    public static void RequireValidExpression(object? node, IOperation sourceOperation)
    {
#if DEBUG
        node.Should().NotBeNull(
            $"solver expression could not be generated out of '{sourceOperation.GetSyntaxNodeText()}'");

        node.Should().BeAssignableTo<Expr>(
            $"expression should have been generated instead of '{node!.GetType().Name}'" +
            $"syntax node");
#endif
    }

    public static void RequireValidExpression<TExpr>(object? node, IOperation sourceOperation) where TExpr : Expr
    {
#if DEBUG
        node.Should().NotBeNull(
            $"solver expression could not be generated out of '{sourceOperation.GetSyntaxNodeText()}'");

        node.Should().BeAssignableTo<TExpr>(
            $"expression should have been generated instead of '{node!.GetType().Name}'" +
            $"syntax node");
#endif
    }
}