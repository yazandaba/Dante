using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dante;

internal sealed class CallableDeclarationAdapter
{
    private readonly MethodDeclarationSyntax? _method;
    private readonly AnonymousFunctionExpressionSyntax? _anonymousFunction;
    public IMethodSymbol UnderlyingSymbol { get; }
    public SyntaxTree SyntaxTree => _method?.SyntaxTree ?? _anonymousFunction!.SyntaxTree;
    public ITypeSymbol ReturnType => UnderlyingSymbol.ReturnType;
    public ImmutableArray<IParameterSymbol> Parameters => UnderlyingSymbol.Parameters;

    public CallableDeclarationAdapter(MethodDeclarationSyntax method, IMethodSymbol underlyingSymbol)
    {
        _method = method;
        _anonymousFunction = null;
        UnderlyingSymbol = underlyingSymbol;
    }

    public CallableDeclarationAdapter(AnonymousFunctionExpressionSyntax anonymousFunction,
        IMethodSymbol underlyingSymbol)
    {
        _method = null;
        _anonymousFunction = anonymousFunction;
        UnderlyingSymbol = underlyingSymbol;
    }

    public bool IsAnonymousLambda =>
        _anonymousFunction is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax; 
}