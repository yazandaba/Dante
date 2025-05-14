using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Operations;

namespace Dante.Extensions;

internal static class BinaryOperationExtension
{
    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsRelationalExpression(this IBinaryOperation binaryExpression)
    {
        return binaryExpression.OperatorKind
            is BinaryOperatorKind.Equals
            or BinaryOperatorKind.NotEquals
            or BinaryOperatorKind.LessThan
            or BinaryOperatorKind.LessThanOrEqual
            or BinaryOperatorKind.GreaterThan
            or BinaryOperatorKind.GreaterThanOrEqual;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsEqualityExpression(this IBinaryOperation binaryExpression)
    {
        return binaryExpression.OperatorKind
            is BinaryOperatorKind.Equals
            or BinaryOperatorKind.NotEquals;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsLogicalExpression(this IBinaryOperation binaryExpression)
    {
        return binaryExpression.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsArithmeticOrBitwiseExpression(this IBinaryOperation binaryExpression)
    {
        return binaryExpression.OperatorKind
            is BinaryOperatorKind.Add
            or BinaryOperatorKind.Subtract
            or BinaryOperatorKind.Multiply
            or BinaryOperatorKind.Divide
            or BinaryOperatorKind.Remainder
            or BinaryOperatorKind.LeftShift
            or BinaryOperatorKind.RightShift
            or BinaryOperatorKind.UnsignedRightShift
            or BinaryOperatorKind.And
            or BinaryOperatorKind.Or
            or BinaryOperatorKind.ExclusiveOr;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsNullableEqualityCheck(this IBinaryOperation binaryExpression)
    {
        var lhs = binaryExpression.LeftOperand;
        var rhs = binaryExpression.RightOperand;
        return binaryExpression.IsEqualityExpression() &&
               (lhs.IsNullLiteralOperation() || rhs.IsNullLiteralOperation());
    }
}