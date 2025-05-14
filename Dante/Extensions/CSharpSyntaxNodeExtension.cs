using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Dante.Extensions;

internal static class CSharpSyntaxNodeExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static CSharpSyntaxNode AsCSharpSyntaxNode(this SyntaxNode node)
    {
        return (CSharpSyntaxNode)node;
    }

    public static string GetSyntaxNodeText(this CSharpSyntaxNode node)
    {
        return node.WithoutTrivia().GetText().ToString();
    }
}