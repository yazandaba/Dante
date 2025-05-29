using Dante.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.Z3;

namespace Dante.Extensions;

internal static class Z3Extension
{
    public static IntExpr MkNeg(this Context context, IntExpr expr, IOperation originalOperand)
    {
        var operandSize = originalOperand.Type!.PrimitiveTypeSizeInBits();
        return context.MkBV2Int(
            context.MkBVNot(context.MkInt2BV(operandSize, expr)),
            !originalOperand.Type!.IsUnsigned());
    }

    public static BoolExpr MkStringGt(this Context context, SeqExpr lhs, SeqExpr rhs)
    {
        return context.MkStringLt(rhs, lhs);
    }

    public static BoolExpr MkStringGe(this Context context, SeqExpr lhs, SeqExpr rhs)
    {
        return context.MkStringLe(rhs, lhs);
    }

    public static ArrayExpr MkOptimizedStore(this Context context, ArrayExpr array, int index, Expr assignee)
    {
        return index switch
        {
            0 => context.MkStore(array, Constant.Zero, assignee),
            1 => context.MkStore(array, Constant.One, assignee),
            2 => context.MkStore(array, Constant.Two, assignee),
            3 => context.MkStore(array, Constant.Three, assignee),
            4 => context.MkStore(array, Constant.Four, assignee),
            5 => context.MkStore(array, Constant.Five, assignee),
            6 => context.MkStore(array, Constant.Six, assignee),
            7 => context.MkStore(array, Constant.Seven, assignee),
            8 => context.MkStore(array, Constant.Eight, assignee),
            9 => context.MkStore(array, Constant.Nine, assignee),
            _ => context.MkStore(array, context.MkInt(index), assignee)
        };
    }

    public static Expr MkUniqueDefault(this Sort sort)
    {
        var genContext = GenerationContext.GetInstance();
        var sortPool = genContext.SortPool;
        var ctx = sort.Context;
        const string ulongUniqueVal = "18446744073709551616"; //ulong.MaxValue+1
        const string decimalUniqueVal = "79228162514264337593543950336"; //decimal.MaxValue+1;
        return sort switch
        {
            IntSort => ctx.MkInt(ulongUniqueVal),
            BoolSort => ctx.MkFalse(),
            FPSort fp32 when fp32.SBits + fp32.EBits + 1 == sizeof(float) * 8 => ctx.MkNumeral(decimalUniqueVal,
                sortPool.SingleSort),
            FPSort fp64 when fp64.SBits + fp64.EBits + 1 == sizeof(double) * 8 => ctx.MkNumeral(decimalUniqueVal,
                sortPool.DoubleSort),
            FPSort fp128 when fp128.SBits + fp128.EBits + 1 == sizeof(decimal) * 8 => ctx.MkNumeral(decimalUniqueVal,
                sortPool.DecimalSort),
            RealSort => ctx.MkReal(decimalUniqueVal),
            SeqSort { SortKind: Z3_sort_kind.Z3_CHAR_SORT } => ctx.MkString(
                Path.GetFileNameWithoutExtension(Path.GetRandomFileName())),
            _ => throw new NotSupportedException("complex types are not supported")
        };
    }

    public static Expr MkDefault(this Sort sort)
    {
        var genContext = GenerationContext.GetInstance();
        var sortPool = genContext.SortPool;
        var ctx = sort.Context;
        return sort switch
        {
            IntSort => ctx.MkInt(0),
            BoolSort => ctx.MkFalse(),
            FPSort => ctx.MkFP(0, sortPool.SingleSort),
            RealSort => ctx.MkReal(0),
            SeqSort { SortKind: Z3_sort_kind.Z3_CHAR_SORT } => ctx.MkString(string.Empty),
            _ => throw new NotSupportedException("complex types are not supported")
        };
    }

    public static bool HasSameParameters(this FuncDecl lhs, FuncDecl rhs)
    {
        var lhsParams = lhs.Domain;
        var rhsParams = rhs.Domain;
        if (lhsParams.Length != rhsParams.Length)
        {
            return false;
        }

        for (var i = 0; i < lhsParams.Length; i++)
        {
            var lhsSort = lhsParams[i];
            var rhsSort = rhsParams[i];
            if (lhsSort.Name != rhsSort.Name)
            {
                return false;
            }
        }

        return true;
    }
}