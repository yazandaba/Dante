namespace Dante.Utilities;

internal interface ISyntaxVisitor
{
    void Visit(SExprSyntax node);
    void Visit(SOperationSyntax node);
    void Visit(SFunctionDeclarationsSyntax node);
    void Visit(SFunctionDeclarationSyntax node);
    void Visit(SParameterListSyntax node);
    void Visit(SParameterSyntax node);
    void Visit(STypeSyntax node);
    void Visit(SArrayTypeSyntax node);
    void Visit(SAlgebraicTypeDeclarationSyntax node);
}

internal sealed class SSyntaxTree
{
    private readonly List<SExprSyntax> _sExpressions = [];

    public void AddExpression(SExprSyntax expr)
    {
        _sExpressions.Add(expr);
    }

    public void Accept(ISyntaxVisitor visitor)
    {
        foreach (var sExpression in _sExpressions) sExpression.Accept(visitor);
    }
}

internal abstract class SNodeSyntax
{
    public Token Begin { get; init; }
    public Token End { get; init; }
    public abstract void Accept(ISyntaxVisitor visitor);
}

internal abstract class SExprBaseSyntax : SNodeSyntax
{
}

internal sealed class SExprSyntax : SExprBaseSyntax
{
    public static SExprSyntax DefaultSExprSyntax { get; } = new() { Operation = new SOperationSyntax() };
    public required SOperationSyntax? Operation { get; init; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}

internal sealed class SOperationSyntax : SExprBaseSyntax
{
    public Token? Identifier { get; init; }
    public IReadOnlyList<SExprBaseSyntax> Arguments { get; init; } = [];

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}

internal sealed class SFunctionDeclarationsSyntax : SExprBaseSyntax
{
    public required IReadOnlyList<SFunctionDeclarationSyntax> Functions { get; init; } = [];
    public required SExprSyntax Bodies { get; init; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}

internal sealed class SFunctionDeclarationSyntax : SExprBaseSyntax
{
    public required Token Identifier { get; init; }
    public required STypeSyntax ReturnType { get; init; }
    public required SParameterListSyntax Parameters { get; init; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}

internal sealed class SParameterListSyntax : SNodeSyntax
{
    public required IReadOnlyList<SParameterSyntax> Parameters { get; init; } = [];

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}

internal sealed class SParameterSyntax : SNodeSyntax
{
    public required Token Identifier { get; init; }
    public required STypeSyntax Type { get; init; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}

internal class STypeSyntax : SExprBaseSyntax
{
    public required Token Identifier { get; init; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}

internal sealed class SArrayTypeSyntax : STypeSyntax
{
    public required IReadOnlyList<STypeSyntax> Domain { get; init; } = [];
    public required STypeSyntax Range { get; init; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}

internal sealed class SAlgebraicTypeDeclarationSyntax : SExprBaseSyntax
{
    public Token TypeIdentifierBegin { get; init; }
    public Token TypeIdentifierEnd { get; init; }
    public required Token Identifier { get; init; }
    public required SExprSyntax Constructors { get; init; }
    public Token? TypeParameterPlaceholder { get; init; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }
}