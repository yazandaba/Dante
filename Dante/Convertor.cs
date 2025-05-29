using System.Diagnostics;
using Dante.Intrinsics;
using Microsoft.CodeAnalysis;
using Microsoft.Z3;

namespace Dante;

internal static class Convertor
{
    public static ArithExpr AsArithmeticExpression(Expr expression)
    {
        var context = expression.Context;
        if (UnderlyingType.IsMaybe(expression))
        {
            expression = MaybeIntrinsics.Value((DatatypeExpr)expression);
        }

        return expression switch
        {
            ArithExpr arithmeticExpr => arithmeticExpr,
            FPExpr fpExpr => context.MkFPToReal(fpExpr),
            BitVecExpr bitVecExpr => context.MkBV2Int(bitVecExpr, true),
            _ => throw new UnreachableException()
        };
    }

    public static FPExpr AsFloatExpression(Expr expression, FPSort fpSort, SortPool? sortPool = null)
    {
        var context = expression.Context;
        var ieee754Round = sortPool?.IEEE754Rounding ?? context.MkFPRoundNearestTiesToEven();
        if (UnderlyingType.IsMaybe(expression))
        {
            expression = MaybeIntrinsics.Value((DatatypeExpr)expression);
        }

        return expression switch
        {
            FPExpr fpExpr => fpExpr,
            IntExpr intExpr => context.MkFPToFP(ieee754Round, context.MkInt2Real(intExpr), fpSort),
            RealExpr realExpr => context.MkFPToFP(ieee754Round, realExpr, fpSort),
            _ => throw new UnreachableException()
        };
    }

    public static FPExpr AsFloatExpression(Expr expression,
        ITypeSymbol originalExpressionType,
        SortPool? sortPool = null)
    {
        var context = expression.Context;
        var ieee754Round = sortPool?.IEEE754Rounding ?? context.MkFPRoundNearestTiesToEven();
        if (UnderlyingType.IsMaybe(expression))
        {
            expression = MaybeIntrinsics.Value((DatatypeExpr)expression);
        }

        var fpSort = originalExpressionType.SpecialType switch
        {
            SpecialType.System_Single => sortPool?.SingleSort ?? context.MkFPSort32(),
            SpecialType.System_Double => sortPool?.DoubleSort ?? context.MkFPSort64(),
            SpecialType.System_Decimal => sortPool?.DoubleSort ?? context.MkFPSort128(),
            _ => throw new InvalidOperationException("trying to create floating point expression out of non floating " +
                                                     $"point operation with type '{originalExpressionType.Name}'")
        };
        return expression switch
        {
            FPExpr fpExpr => fpExpr,
            IntExpr intExpr => context.MkFPToFP(ieee754Round, context.MkInt2Real(intExpr), fpSort),
            RealExpr realExpr => context.MkFPToFP(ieee754Round, realExpr, fpSort),
            _ => throw new UnreachableException()
        };
    }

    public static IntExpr AsIntegerExpression(Expr expression)
    {
        var context = expression.Context;
        if (UnderlyingType.IsMaybe(expression))
        {
            expression = MaybeIntrinsics.Value((DatatypeExpr)expression);
        }

        return expression switch
        {
            IntExpr intExpr => intExpr,
            RealExpr realExpr => context.MkReal2Int(realExpr),
            FPExpr fpExpr => context.MkReal2Int(context.MkFPToReal(fpExpr)),
            BitVecExpr bitVecExpr => context.MkBV2Int(bitVecExpr, false),
            _ => throw new UnreachableException()
        };
    }

    public static SeqExpr AsStringExpression(Expr expression)
    {
        var context = expression.Context;
        if (UnderlyingType.IsMaybe(expression))
        {
            expression = MaybeIntrinsics.Value((DatatypeExpr)expression);
        }

        return expression switch
        {
            SeqExpr seqExpr => seqExpr,
            IntExpr intExpr => context.IntToString(intExpr),
            RealExpr realExpr => context.SbvToString(realExpr),
            FPExpr fpExpr => context.SbvToString(AsArithmeticExpression(fpExpr)),
            _ => context.MkEmptySeq(context.StringSort)
        };
    }
}