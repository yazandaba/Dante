using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Dante.Extensions;

internal static class MemberSymbolExtension
{
    public static bool WasSourceDeclared(this ISymbol method)
    {
        return method.DeclaringSyntaxReferences.Length is 0;
    }

    public static bool IsArrayLength(this IPropertyReferenceOperation property)
    {
        return property.Instance?.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Int32 } &&
               property.Property.Name is "Length";
    }
}