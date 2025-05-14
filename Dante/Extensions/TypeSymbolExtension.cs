using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Dante.Generator;
using Dante.Intrinsics;
using Microsoft.CodeAnalysis;
using Microsoft.Z3;
using Enumerable = Dante.Intrinsics.Enumerable;

namespace Dante.Extensions;

internal static class TypeSymbolExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static Sort AsSort(this ITypeSymbol typeSymbol, bool considerNullable = true)
    {
        if (considerNullable && typeSymbol.IsNullable()) return MaybeIntrinsics.CreateOrGet(typeSymbol);

        if (IsEnumerable(typeSymbol)) return Enumerable.CreateOrGet(typeSymbol);

        var genContext = GenerationContext.GetInstance();
        var context = genContext.SolverContext;
        var sortPool = genContext.SortPool;
        switch (typeSymbol)
        {
            case IArrayTypeSymbol arrayType:
            {
                var arrayElementSort = AsSort(arrayType.ElementType);
                if (arrayType.Rank is 1) return context.MkArraySort(context.IntSort, arrayElementSort);

                var domains = new Sort[arrayType.Rank];
                for (var i = 0; i < arrayType.Rank; ++i) domains[i] = context.IntSort;

                return context.MkArraySort(domains, arrayElementSort);
            }
        }

        return typeSymbol.SpecialType switch
        {
            SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_Int32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt16 or
                SpecialType.System_UInt32 or
                SpecialType.System_UInt64 => context.IntSort,
            SpecialType.System_Char => context.CharSort,
            SpecialType.System_String => context.StringSort,
            SpecialType.System_Single => sortPool.SingleSort,
            SpecialType.System_Double => sortPool.DoubleSort,
            SpecialType.System_Decimal => sortPool.DecimalSort,
            SpecialType.System_Boolean => context.BoolSort,
            _ => context.MkUninterpretedSort(typeSymbol.ToDisplayString())
        };
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsVoid(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType is SpecialType.System_Void;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsUnsigned(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType
            is SpecialType.System_Byte
            or SpecialType.System_UInt16
            or SpecialType.System_UInt32
            or SpecialType.System_UInt64;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsIntegral(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType
            is SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsFloatingPoint(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType
            is SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsString(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType is SpecialType.System_String;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static uint PrimitiveTypeSizeInBits(this ITypeSymbol typeSymbol)
    {
        var primitiveSize = typeSymbol.SpecialType switch
        {
            SpecialType.System_Boolean => sizeof(bool),
            SpecialType.System_Byte => sizeof(byte),
            SpecialType.System_Char => sizeof(char),
            SpecialType.System_Double => sizeof(double),
            SpecialType.System_Single => sizeof(float),
            SpecialType.System_Int32 => sizeof(int),
            SpecialType.System_Int64 => sizeof(long),
            SpecialType.System_SByte => sizeof(sbyte),
            SpecialType.System_Int16 => sizeof(short),
            SpecialType.System_UInt32 => sizeof(uint),
            SpecialType.System_UInt64 => sizeof(ulong),
            SpecialType.System_UInt16 => sizeof(ushort),
            _ => throw new UnreachableException()
        };

        return (uint)primitiveSize * 8;
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool IsSame(this ITypeSymbol typeSymbol, ITypeSymbol otherType)
    {
        return typeSymbol.Equals(otherType, SymbolEqualityComparer.Default);
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsNullable(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.NullableAnnotation is NullableAnnotation.Annotated ||
               typeSymbol.OriginalDefinition.IsSame(CSharpBuiltinTypes.GenericNullable());
    }

    [Pure]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsEnumerable(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.OriginalDefinition.IsSame(CSharpBuiltinTypes.Enumerable()) ||
               typeSymbol.OriginalDefinition.IsSame(CSharpBuiltinTypes.EnumerableInterface());
    }

    public static ITypeSymbol AsNonNullableType(this ITypeSymbol typeSymbol)
    {
        if (!typeSymbol.IsNullable()) return typeSymbol;
        var namedTypeSymbol = (INamedTypeSymbol)typeSymbol;
        //deduced type argument of System.Nullable<T>
        var typeArgument = namedTypeSymbol.TypeArguments[0];
        return typeArgument;
    }
}