using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Z3;

namespace Dante;

internal record Scope
{
    public Scope? PredecessorScope { get; init; }
    private readonly Dictionary<ISymbol, SymbolInfo> _scopeSymbols = new(SymbolEqualityComparer.Default);

    public void AddSymbol(ISymbol symbol, SymbolInfo symbolInfo)
    {
        _scopeSymbols.Should().NotContainKey(symbol, $"duplicate symbols '{symbol}' in same scope violate original " +
                                                     "C# semantic");
        _scopeSymbols[symbol] = symbolInfo;
    }

    public bool Fetch(ISymbol symbol, [MaybeNullWhen(false)] out SymbolInfo symbolInfo)
    {
        return _scopeSymbols.TryGetValue(symbol, out symbolInfo);
    }

    public IEnumerable<SymbolInfo> Symbols => _scopeSymbols.Values;
}

internal class SymbolInfo
{
    public required FuncDecl Declaration { get; init; }
    public Expr? Value { get; set; }

    /// <summary>
    ///     the value that was used before or while updating 'Value', This property only applies when its associated
    ///     operation is post-increment or post-decrement.
    /// </summary>
    public Expr? TemporaryValue { get; set; }
}

internal class SymbolEvaluationTable
{
    private readonly Scope _recentScope = new();

    public void TryAdd(ISymbol symbol, FuncDecl declaredSymbol)
    {
        if (!_recentScope.Fetch(symbol, out _))
            _recentScope.AddSymbol(symbol, new SymbolInfo
            {
                Declaration = declaredSymbol
            });
    }

    public void Bind(ISymbol symbol, Expr valueExprTree, Expr? temporaryValue = null)
    {
        var fetched = TryFetch(symbol, out SymbolInfo? symbolInfo);
        fetched.Should().BeTrue($"trying to bind value expression tree to non existent symbol '{symbol}'");
        symbolInfo!.Value = valueExprTree;
        symbolInfo.TemporaryValue = temporaryValue;
    }

    public void AddThenBind(ISymbol symbol, FuncDecl declaredSymbol, Expr valueExprTree)
    {
        _recentScope.AddSymbol(symbol, new SymbolInfo
        {
            Declaration = declaredSymbol,
            Value = valueExprTree
        });
    }

    public void InvalidateTemporaries()
    {
        foreach (var symbol in _recentScope.Symbols) symbol.TemporaryValue = null;
    }

    public bool TryFetch(ISymbol symbol, [MaybeNullWhen(false)] out FuncDecl declaredSymbol)
    {
        var status = TryFetch(symbol, out SymbolInfo? symbolInfo);
        declaredSymbol = symbolInfo?.Declaration;
        return status;
    }

    public bool TryFetch(ISymbol symbol, [MaybeNullWhen(false)] out Expr value)
    {
        var status = TryFetch(symbol, out SymbolInfo? symbolInfo);
        value = GetValue(symbolInfo);
        return status && value is not null;
    }

    private bool TryFetch(ISymbol symbol, [MaybeNullWhen(false)] out SymbolInfo declaredSymbol)
    {
        var visitingScope = _recentScope;
        while (visitingScope is not null)
        {
            if (visitingScope.Fetch(symbol, out declaredSymbol)) return true;

            visitingScope = visitingScope.PredecessorScope;
        }

        declaredSymbol = null;
        return false;
    }

    private Expr? GetValue(SymbolInfo? symbolInfo)
    {
        if (symbolInfo is null) return default;

        return symbolInfo.TemporaryValue ?? symbolInfo.Value;
    }
}















