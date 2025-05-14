using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Dante.Utilities;

file static class ColorSelecter
{
    private static byte _usingColor;

    private static ConsoleColor[] AllowedColors =>
    [
        ConsoleColor.DarkCyan, ConsoleColor.DarkGreen, ConsoleColor.DarkMagenta, ConsoleColor.DarkGray,
        ConsoleColor.Magenta, ConsoleColor.Cyan, ConsoleColor.White, ConsoleColor.Yellow
    ];

    public static ConsoleColor SelectColor()
    {
        return AllowedColors[_usingColor++ & 0x07];
    }

    public static void ReleaseColor()
    {
        --_usingColor;
    }
}

internal class SExpressionHighlighter : ISyntaxVisitor
{
    public void Highlight(string code)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code)) return;

            var tree = new SParser(code).Parse();
            tree.Accept(this);
        }
        catch
        {
            Console.Error.WriteLine(">>>>>>>>>>>> Syntax highlighting failed");
            Console.WriteLine(code);
        }
    }

    private static void Print(ConsoleColor color, in Token token)
    {
        Console.ForegroundColor = color;
        Console.Write(token.Value);
        Console.ResetColor();
        Console.Write(token.AuxiliaryText);
    }

    public void Visit(SExprSyntax node)
    {
        try
        {
            var parenthesesColor = ColorSelecter.SelectColor();
            Print(parenthesesColor, node.Begin);
            node.Operation?.Accept(this);
            Print(parenthesesColor, node.End);
        }
        finally
        {
            ColorSelecter.ReleaseColor();
        }
    }

    public void Visit(SOperationSyntax node)
    {
        var id = node.Identifier;
        if (id is not null)
        {
            var identifier = id.Value.Value;
            var identifierType = id.Value.Type;
            ConsoleColor color;

            if (identifier.StartsWith("BB"))
                color = ConsoleColor.White;

            else if (identifier.StartsWith("Enumerable_"))
                color = ConsoleColor.Magenta;

            else if (identifierType is TokenType.Operator)
                color = ConsoleColor.White;

            else if (identifierType is TokenType.StringLiteral)
                color = ConsoleColor.DarkYellow;

            else
                color = identifier switch
                {
                    "model-add" => ConsoleColor.White,
                    "let" => ConsoleColor.White,
                    "ite" => ConsoleColor.White,
                    _ => ConsoleColor.DarkBlue
                };

            Print(color, id.Value);
        }

        foreach (var sExpression in node.Arguments) sExpression.Accept(this);
    }

    public void Visit(SFunctionDeclarationsSyntax node)
    {
        try
        {
            var parenthesesColor = ColorSelecter.SelectColor();
            Print(parenthesesColor, node.Begin);
            foreach (var function in node.Functions) function.Accept(this);
            Print(parenthesesColor, node.End);
            node.Bodies.Accept(this);
        }
        finally
        {
            ColorSelecter.ReleaseColor();
        }
    }

    public void Visit(SFunctionDeclarationSyntax node)
    {
        try
        {
            var parenthesesColor = ColorSelecter.SelectColor();
            Print(parenthesesColor, node.Begin);
            Print(ConsoleColor.DarkGreen, node.Identifier);
            node.Parameters.Accept(this);
            node.ReturnType.Accept(this);
            Print(parenthesesColor, node.End);
        }
        finally
        {
            ColorSelecter.ReleaseColor();
        }
    }

    public void Visit(SParameterListSyntax node)
    {
        try
        {
            var parenthesesColor = ColorSelecter.SelectColor();
            Print(parenthesesColor, node.Begin);
            foreach (var parameter in node.Parameters) parameter.Accept(this);
            Print(parenthesesColor, node.End);
        }
        finally
        {
            ColorSelecter.ReleaseColor();
        }
    }

    public void Visit(SParameterSyntax node)
    {
        try
        {
            var parenthesesColor = ColorSelecter.SelectColor();
            Print(parenthesesColor, node.Begin);
            Print(ConsoleColor.Gray, node.Identifier);
            node.Type.Accept(this);
            Print(parenthesesColor, node.End);
        }
        finally
        {
            ColorSelecter.ReleaseColor();
        }
    }

    public void Visit(STypeSyntax node)
    {
        try
        {
            if (node is SArrayTypeSyntax arrayType)
            {
                Visit(arrayType);
                return;
            }

            var parenthesesColor = ColorSelecter.SelectColor();
            Print(parenthesesColor, node.Begin);
            Print(ConsoleColor.DarkMagenta, node.Identifier);
            Print(parenthesesColor, node.End);
        }
        finally
        {
            ColorSelecter.ReleaseColor();
        }
    }

    public void Visit(SArrayTypeSyntax node)
    {
        try
        {
            var parenthesesColor = ColorSelecter.SelectColor();
            Print(parenthesesColor, node.Begin);
            Print(ConsoleColor.DarkBlue, node.Identifier);
            foreach (var sTypeSyntax in node.Domain) sTypeSyntax.Accept(this);
            node.Range.Accept(this);
            Print(parenthesesColor, node.End);
        }
        finally
        {
            ColorSelecter.ReleaseColor();
        }
    }

    public void Visit(SAlgebraicTypeDeclarationSyntax node)
    {
        try
        {
            var parenthesesColor = ColorSelecter.SelectColor();
            var typeIdParenthesesColor = ColorSelecter.SelectColor();
            Print(parenthesesColor, node.Begin);
            //print type name
            Print(typeIdParenthesesColor, node.TypeIdentifierBegin);
            Print(ConsoleColor.DarkMagenta, node.Identifier);
            var typeParamPlaceholderToken = node.TypeParameterPlaceholder;
            if (typeParamPlaceholderToken is not null) Print(ConsoleColor.Magenta, typeParamPlaceholderToken.Value);
            Print(typeIdParenthesesColor, node.TypeIdentifierEnd);
            Print(parenthesesColor, node.End);
            //print type ctor's
            node.Constructors.Accept(this);
        }
        finally
        {
            ColorSelecter.ReleaseColor();
        }
    }
}

internal enum TokenType
{
    None,
    LeftParenthesis,
    RightParenthesis,
    Identifier,
    StringLiteral,
    NumberLiteral,
    DeclareFunKeyword,
    DefineFunsRecKeyword,
    DeclareDataTypesKeyword,
    ArrayKeyword,
    Operator,
    EndOfFile
}

internal readonly struct Token
{
    public Token()
    {
    }

    public TokenType Type { get; init; } = TokenType.None;
    public string Value { get; init; } = string.Empty;
    public string AuxiliaryText { get; init; } = string.Empty;
}

internal class SLexer
{
    private readonly List<Token> _tokens = [];
    private readonly string _code;
    private readonly Token _eof = new() { Type = TokenType.EndOfFile };
    private int _pos;

    public SLexer(string code)
    {
        _code = code;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Token CurrentToken()
    {
        return IsEndOfTokens(_pos) ? _eof : _tokens[_pos];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Token NextToken()
    {
        return IsEndOfTokens(++_pos) ? _eof : _tokens[_pos];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Token PeekToken()
    {
        return IsEndOfFile(_pos + 1) ? _eof : _tokens[_pos + 1];
    }

    public void Lex()
    {
        var pos = 0;
        while (!IsEndOfFile(pos))
        {
            var ch = _code[pos];
            if (IsAuxiliaryChar(ch))
            {
                var auxiliaryText = ScanAuxiliary(pos);
                var currentToken = _tokens[^1];
                //attach any auxiliary text to the last processed token
                _tokens[^1] = currentToken with { AuxiliaryText = auxiliaryText };
                pos += auxiliaryText.Length;
            }
            else if (ch is '(')
            {
                _tokens.Add(new Token { Type = TokenType.LeftParenthesis, Value = ch.ToString() });
                ++pos;
            }
            else if (ch is ')')
            {
                _tokens.Add(new Token { Type = TokenType.RightParenthesis, Value = ch.ToString() });
                ++pos;
            }
            else if (ch is '\"')
            {
                var stringToken = TokenizeStringLiteral(pos);
                _tokens.Add(stringToken);
                pos += stringToken.Value.Length;
            }
            else if (char.IsDigit(ch))
            {
                var numberToken = TokenizeNumber(pos);
                _tokens.Add(numberToken);
                pos += numberToken.Value.Length;
            }
            else if (IsIdentifierLegalChar(ch))
            {
                var identifierToken = TokenizeIdentifier(pos);
                _tokens.Add(identifierToken);
                pos += identifierToken.Value.Length;
            }
            else if (IsOperatorChar(ch))
            {
                var specialToken = new Token { Type = TokenType.Operator, Value = ch.ToString() };
                _tokens.Add(specialToken);
                ++pos;
            }
            else
            {
                ++pos;
            }
        }
    }

    private Token TokenizeStringLiteral(int i)
    {
        var strBuilder = new StringBuilder();
        var ch = _code[i];
        do
        {
            strBuilder.Append(ch);
            ch = _code[++i];
            if (ch is '\"')
            {
                strBuilder.Append(ch);
                break;
            }
        } while (!IsEndOfFile(i));

        return new Token { Type = TokenType.StringLiteral, Value = strBuilder.ToString() };
    }

    private Token TokenizeNumber(int i)
    {
        var numberBuilder = new StringBuilder();
        do
        {
            var ch = _code[i++];
            numberBuilder.Append(ch);
        } while (!IsEndOfFile(i) && (char.IsNumber(_code[i]) || _code[i] is '.'));

        return new Token { Type = TokenType.NumberLiteral, Value = numberBuilder.ToString() };
    }

    private Token TokenizeIdentifier(int i)
    {
        var identifierBuilder = new StringBuilder();
        do
        {
            var ch = _code[i++];
            identifierBuilder.Append(ch);
        } while (!IsEndOfFile(i) && IsIdentifierLegalChar(_code[i]));

        var identifier = identifierBuilder.ToString();
        switch (identifier)
        {
            case "define-funs-rec":
                return new Token { Type = TokenType.DefineFunsRecKeyword, Value = identifier };
            case "declare-fun":
                return new Token { Type = TokenType.DeclareFunKeyword, Value = identifier };
            case "declare-datatypes":
                return new Token { Type = TokenType.DeclareDataTypesKeyword, Value = identifier };
            case "Array":
                return new Token { Type = TokenType.ArrayKeyword, Value = identifier };
        }

        return new Token { Type = TokenType.Identifier, Value = identifierBuilder.ToString() };
    }

    private string ScanAuxiliary(int i)
    {
        var auxiliaryBuilder = new StringBuilder();
        do
        {
            var ch = _code[i++];
            auxiliaryBuilder.Append(ch);
        } while (!IsEndOfFile(i) && IsAuxiliaryChar(_code[i]));

        return auxiliaryBuilder.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierLegalChar(char ch)
    {
        return char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '-' or '!';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsOperatorChar(char ch)
    {
        return ch is '+' or '-' or '/' or '*' or '<' or '>' or '=' or '!';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAuxiliaryChar(char ch)
    {
        return char.IsWhiteSpace(ch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsEndOfFile(int position)
    {
        return position >= _code.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsEndOfTokens(int position)
    {
        return position >= _tokens.Count;
    }
}

internal class SParser
{
    private readonly SLexer _lexer;

    public SParser(string code)
    {
        _lexer = new SLexer(code);
    }

    public SSyntaxTree Parse()
    {
        _lexer.Lex();
        return ParseSExpressions();
    }

    private SSyntaxTree ParseSExpressions()
    {
        var token = _lexer.CurrentToken();
        var tree = new SSyntaxTree();
        while (token.Type is TokenType.LeftParenthesis)
        {
            tree.AddExpression(ParseSExpression());
            token = _lexer.CurrentToken();
        }

        return tree;
    }

    private SExprSyntax ParseSExpression()
    {
        var token = _lexer.CurrentToken();
        switch (token.Type)
        {
            case TokenType.LeftParenthesis:
            {
                var begin = token;
                _lexer.NextToken();
                var op = ParseSOperation();
                var end = _lexer.CurrentToken();
                _lexer.NextToken();
                return new SExprSyntax { Begin = begin, End = end, Operation = op };
            }
            case TokenType.StringLiteral:
            case TokenType.Operator:
            case TokenType.Identifier:
            case TokenType.NumberLiteral:
                return new SExprSyntax { Operation = ParseSOperation() };
            case TokenType.RightParenthesis:
                return SExprSyntax.DefaultSExprSyntax;
            default:
                throw new UnreachableException();
        }
    }

    private SOperationSyntax? ParseSOperation()
    {
        var opId = _lexer.CurrentToken();
        switch (opId.Type)
        {
            case TokenType.DefineFunsRecKeyword:
                _lexer.NextToken(); //skip define-funs-rec keyword
                return new SOperationSyntax { Identifier = opId, Arguments = [ParseSFunctionDeclarations()] };
            case TokenType.DeclareFunKeyword:
                _lexer.NextToken(); //skip declare-fun keyword
                return new SOperationSyntax { Identifier = opId, Arguments = [ParseSFunctionDeclaration()] };
            case TokenType.DeclareDataTypesKeyword:
                _lexer.NextToken(); //skip declare-data-types keyword
                return new SOperationSyntax { Identifier = opId, Arguments = [ParseSAlgebraicTypeDeclaration()] };
            case TokenType.ArrayKeyword:
                return new SOperationSyntax { Arguments = [ParseSType()] };
            case TokenType.StringLiteral:
                _lexer.NextToken();
                return new SOperationSyntax { Identifier = opId };
            case TokenType.LeftParenthesis:
            {
                var arguments = new List<SExprSyntax>();
                Token token;
                do
                {
                    var sExpr = ParseSExpression();
                    arguments.Add(sExpr);
                    token = _lexer.CurrentToken();
                } while (token.Type is not TokenType.RightParenthesis and not TokenType.EndOfFile);

                return new SOperationSyntax { Arguments = arguments };
            }
            case TokenType.Operator:
            case TokenType.NumberLiteral:
            case TokenType.Identifier:
            {
                _lexer.NextToken(); //skip operator,placeholder,identifier
                var arguments = new List<SExprSyntax>();
                Token token;
                do
                {
                    var sExpr = ParseSExpression();
                    arguments.Add(sExpr);
                    token = _lexer.CurrentToken();
                } while (token.Type is not TokenType.RightParenthesis and not TokenType.EndOfFile);

                return new SOperationSyntax { Identifier = opId, Arguments = arguments /*arguments*/ };
            }
            case TokenType.RightParenthesis: //caller SOperation has an empty nested argument list of SExpressions
                return default;
            default:
                throw new UnreachableException();
        }
    }

    private SFunctionDeclarationsSyntax ParseSFunctionDeclarations()
    {
        var begin = _lexer.CurrentToken();
        var token = _lexer.NextToken();
        var funcDecls = new List<SFunctionDeclarationSyntax>();
        while (token.Type is TokenType.LeftParenthesis)
        {
            funcDecls.Add(ParseSFunctionDeclaration());
            token = _lexer.CurrentToken();
        }

        var end = _lexer.CurrentToken();
        _lexer.NextToken();
        var bodies = ParseSExpression();
        return new SFunctionDeclarationsSyntax { Begin = begin, End = end, Functions = funcDecls, Bodies = bodies };
    }

    private SFunctionDeclarationSyntax ParseSFunctionDeclaration()
    {
        var optBegin = _lexer.CurrentToken();
        var funcId = optBegin.Type is TokenType.LeftParenthesis ? _lexer.NextToken() : optBegin;
        _lexer.NextToken(); //skip identifier to start of a parameter list 
        var parameters = ParseSParameters();
        var returnType = ParseSType();
        if (optBegin.Type is not TokenType.LeftParenthesis)
            return new SFunctionDeclarationSyntax
                { Identifier = funcId, Parameters = parameters, ReturnType = returnType };

        var end = _lexer.CurrentToken();
        _lexer.NextToken();
        return new SFunctionDeclarationSyntax
            { Begin = optBegin, End = end, Identifier = funcId, Parameters = parameters, ReturnType = returnType };
    }

    private SParameterListSyntax ParseSParameters()
    {
        var begin = _lexer.CurrentToken();
        var token = _lexer.NextToken();
        var parameters = new List<SParameterSyntax>();
        while (token.Type is TokenType.LeftParenthesis)
        {
            parameters.Add(ParseSParameter());
            token = _lexer.CurrentToken();
        }

        var end = _lexer.CurrentToken();
        _lexer.NextToken();
        return new SParameterListSyntax { Begin = begin, End = end, Parameters = parameters };
    }

    private SParameterSyntax ParseSParameter()
    {
        var begin = _lexer.CurrentToken();
        var paramId = _lexer.NextToken();
        _lexer.NextToken();
        var type = ParseSType();
        var end = _lexer.CurrentToken();
        _lexer.NextToken();
        return new SParameterSyntax { Begin = begin, End = end, Type = type, Identifier = paramId };
    }

    private STypeSyntax ParseSType()
    {
        var token = _lexer.CurrentToken();
        Token? optBegin = default;
        if (token.Type is TokenType.LeftParenthesis)
        {
            optBegin = token;
            token = _lexer.NextToken();
        }

        switch (token.Type)
        {
            case TokenType.ArrayKeyword:
            {
                var array = token;
                var firstDomain = new STypeSyntax { Identifier = _lexer.NextToken() };
                var domains = new List<STypeSyntax> { firstDomain };
                token = _lexer.NextToken(); //either another domain or range
                while (token.Type is TokenType.Identifier)
                {
                    token = _lexer.PeekToken();
                    if (token.Type is TokenType.RightParenthesis) break;
                    domains.Add(new STypeSyntax { Identifier = token });
                    token = _lexer.NextToken();
                }

                var range = new STypeSyntax { Identifier = _lexer.CurrentToken() };
                if (optBegin is not null)
                {
                    var begin = optBegin.Value;
                    var end = _lexer.NextToken();
                    _lexer.NextToken();
                    return new SArrayTypeSyntax
                        { Begin = begin, End = end, Identifier = array, Domain = domains, Range = range };
                }

                _lexer.NextToken();
                return new SArrayTypeSyntax { Identifier = array, Domain = domains, Range = range };
            }

            case TokenType.Identifier:
                _lexer.NextToken();
                return new STypeSyntax { Identifier = token };

            default:
                throw new InvalidOperationException();
        }
    }

    private SAlgebraicTypeDeclarationSyntax ParseSAlgebraicTypeDeclaration()
    {
        var begin = _lexer.CurrentToken();
        var type = _lexer.NextToken();
        switch (type.Type)
        {
            case TokenType.Identifier:
            {
                var end = _lexer.NextToken();
                _lexer.NextToken(); //skip to type constructors list
                return new SAlgebraicTypeDeclarationSyntax
                {
                    Begin = begin,
                    End = end,
                    Identifier = type,
                    Constructors = ParseSExpression()
                };
            }
            case TokenType.LeftParenthesis:
            {
                var typeIdBegin = _lexer.CurrentToken();
                var typeId = _lexer.NextToken();
                var placeholder = _lexer.NextToken();
                var typeIdEnd = _lexer.NextToken();
                var end = _lexer.NextToken();
                _lexer.NextToken(); //skip to type constructors list
                return new SAlgebraicTypeDeclarationSyntax
                {
                    Begin = begin,
                    End = end,
                    TypeIdentifierBegin = typeIdBegin,
                    TypeIdentifierEnd = typeIdEnd,
                    Identifier = typeId,
                    Constructors = ParseSExpression(),
                    TypeParameterPlaceholder = placeholder
                };
            }
            default:
                throw new UnreachableException();
        }
    }
}