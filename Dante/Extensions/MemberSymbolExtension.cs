using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Dante.Extensions;

internal static class MemberSymbolExtension
{
    public static bool WasSourceDeclared(this ISymbol method)
    {
        return method.DeclaringSyntaxReferences.Length > 0;
    }

    public static bool IsArrayLength(this IPropertyReferenceOperation property)
    {
        return property.Instance?.Type is IArrayTypeSymbol
               {
                   ElementType.SpecialType: SpecialType.System_Int32 or SpecialType.System_Int64
               } &&
               property.Property.Name is "Length";
    }

    private static CSharpSyntaxNode GetMemberDeclaration(ISymbol method)
    {
        return (CSharpSyntaxNode)method.DeclaringSyntaxReferences[0].GetSyntax();
    }

    public static MethodDeclarationSyntax GetDeclaration(this IMethodSymbol method)
    {
        return (MethodDeclarationSyntax)GetMemberDeclaration(method);
    }
}