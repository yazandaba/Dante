using System.Runtime.CompilerServices;
using Dante.Extensions;
using Dante.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Z3;

namespace Dante.Intrinsics;

internal sealed class Maybe
{
    public DatatypeSort MaybeSort { get; }

    private Maybe(DatatypeSort maybeSort)
    {
        MaybeSort = maybeSort;
    }

    public static Maybe Create(ITypeSymbol typeParameter)
    {
        var genContext = GenerationContext.GetInstance();
        var context = genContext.SolverContext;
        var typeParamSort = typeParameter.AsSort(false);
        var none = context.MkConstructor("None", "IsNone");
        var some = context.MkConstructor("Some", "HasValue", ["value"], [typeParamSort]);
        var maybeSort = context.MkDatatypeSort($"Maybe{typeParamSort.Name}", [none, some]);
        return new Maybe(maybeSort);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator DatatypeSort(Maybe maybe)
    {
        return maybe.MaybeSort;
    }
}

internal class MaybeExpr
{
    private readonly DatatypeExpr _datatypeExpr;
    public Maybe Sort { get; }

    public MaybeExpr(Maybe sort, DatatypeExpr datatypeExpr)
    {
        Sort = sort;
        _datatypeExpr = datatypeExpr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator DatatypeExpr(MaybeExpr expr)
    {
        return expr._datatypeExpr;
    }
}

internal static class MaybeIntrinsics
{
    private static readonly Dictionary<ITypeSymbol, Maybe> InstantiationMap = new(SymbolEqualityComparer.Default);

    public static Maybe CreateOrGet(ITypeSymbol typeParameter)
    {
        if (InstantiationMap.TryGetValue(typeParameter, out var maybe))
        {
            return maybe;
        }

        maybe = Maybe.Create(typeParameter);
        InstantiationMap[typeParameter] = maybe;
        return maybe;
    }

    public static MaybeExpr Default(Maybe maybe)
    {
        var none = maybe.MaybeSort.Constructors[0];
        var noneInstance = none.Apply();
        noneInstance.Should().BeOfType<DatatypeExpr>("applying 'None' constructor from type 'Maybe' must yield" +
                                                     "a 'Maybe' expr");
        return new MaybeExpr(maybe, (DatatypeExpr)noneInstance);
    }

    public static MaybeExpr Some(Maybe maybe, Expr value)
    {
        var some = maybe.MaybeSort.Constructors[1];
        var maybeInstance = some.Apply(value);
        maybeInstance.Should().BeOfType<DatatypeExpr>("applying 'Some' constructor from type 'Maybe' must yield" +
                                                      "a 'Maybe' expr");
        return new MaybeExpr(maybe, (DatatypeExpr)maybeInstance);
    }

    public static Expr Value(MaybeExpr maybeExpr)
    {
        return Value((DatatypeExpr)maybeExpr);
    }

    public static Expr Value(DatatypeExpr maybeExpr)
    {
        UnderlyingType.IsMaybe(maybeExpr).Should()
            .BeTrue("cannot get value of expression '{0}' out of non underlying maybe type object",
                maybeExpr.Sort.Name);
        var someValueAccessor = ((DatatypeSort)maybeExpr.Sort).Accessors[1][0];
        var value = someValueAccessor.Apply(maybeExpr);
        value.Should().BeAssignableTo<Expr>();
        return value!;
    }

    public static BoolExpr HasValue(MaybeExpr maybeExpr)
    {
        return HasValue((DatatypeExpr)maybeExpr);
    }

    public static BoolExpr HasValue(DatatypeExpr maybeExpr)
    {
        UnderlyingType.IsMaybe(maybeExpr).Should()
            .BeTrue("cannot create '{0}' out of non underlying maybe type", maybeExpr.Sort.Name);
        var isNone = ((DatatypeSort)maybeExpr.Sort).Recognizers[1];
        return (BoolExpr)isNone.Apply(maybeExpr);
    }

    public static BoolExpr IsNull(DatatypeExpr maybeExpr)
    {
        UnderlyingType.IsMaybe(maybeExpr).Should()
            .BeTrue("cannot create '{0}' out of non underlying maybe type", maybeExpr.Sort.Name);
        var isNone = ((DatatypeSort)maybeExpr.Sort).Recognizers[0];
        return (BoolExpr)isNone.Apply(maybeExpr);
    }
}