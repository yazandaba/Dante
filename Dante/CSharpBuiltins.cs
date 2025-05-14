using Dante.Generator;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Dante;

internal static class CSharpBuiltinTypes
{
    public static INamedTypeSymbol GenericNullable()
    {
        var compilation = GenerationContext.GetInstance().Compilation;
        return compilation.GetTypeByMetadataName("System.Nullable`1")!;
    }

    public static INamedTypeSymbol Enumerable()
    {
        var compilation = GenerationContext.GetInstance().Compilation;
        return compilation.GetTypeByMetadataName("System.Collections.Enumerable")!;
    }

    public static INamedTypeSymbol EnumerableInterface()
    {
        var compilation = GenerationContext.GetInstance().Compilation;
        return compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1")!;
    }
}

internal static class CSharpBuiltinMethods
{
    public static IMethodSymbol NullableHasValue()
    {
        var nullable = CSharpBuiltinTypes.GenericNullable();
        var hasValueProperty = nullable.GetMembers("HasValue").FirstOrDefault() as IPropertySymbol;
        hasValueProperty.Should().NotBeNull().And.BeAssignableTo<IPropertySymbol>("System.Nullable<T>.HasValue " +
            "symbol does not exist, most probably mscorlib " +
            "wasn't imported properly");
        var hasValueGetter = hasValueProperty!.GetMethod;
        hasValueGetter.Should().NotBeNull("System.Nullable<T>.HasValue.get will get used to retrieve the value " +
                                          "of 'HasValue' property which indicated if nullable instance has value or not ");
        return hasValueGetter!;
    }
}