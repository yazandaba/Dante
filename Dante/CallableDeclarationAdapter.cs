using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dante;

internal sealed class CallableDeclarationAdapter
{
    private readonly MethodDeclarationSyntax? _method;
    private readonly AnonymousFunctionExpressionSyntax? _anonymousFunction;
    private readonly IMethodSymbol _underlyingSymbol;
    public SyntaxTree SyntaxTree => _method?.SyntaxTree ?? _anonymousFunction!.SyntaxTree;
    public ITypeSymbol ReturnType => _underlyingSymbol.ReturnType;
    public ImmutableArray<IParameterSymbol> Parameters => _underlyingSymbol.Parameters;

    public CallableDeclarationAdapter(MethodDeclarationSyntax method, IMethodSymbol underlyingSymbol)
    {
        _method = method;
        _anonymousFunction = null;
        _underlyingSymbol = underlyingSymbol;
    }

    public CallableDeclarationAdapter(AnonymousFunctionExpressionSyntax anonymousFunction,
        IMethodSymbol underlyingSymbol)
    {
        _method = null;
        _anonymousFunction = anonymousFunction;
        _underlyingSymbol = underlyingSymbol;
    }

    public bool IsAnonymousLambda =>
        _anonymousFunction is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax;
}