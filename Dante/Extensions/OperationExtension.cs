using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Dante.Extensions;

internal static class OperationExtension
{
    public static string GetSyntaxNodeText(this IOperation operation)
    {
        return operation.Syntax.AsCSharpSyntaxNode().GetSyntaxNodeText();
    }

    public static bool IsPrefixOrPostfixUnaryOperation(this IOperation operation)
    {
        return operation is IUnaryOperation or IIncrementOrDecrementOperation;
    }

    public static bool IsNullLiteralOperation(this IOperation operation)
    {
        return (operation is IConversionOperation { Operand: ILiteralOperation literal } &&
                literal.IsNullableLiteral()) ||
               (operation is ILiteralOperation literalOperation && literalOperation.IsNullableLiteral());
    }
}