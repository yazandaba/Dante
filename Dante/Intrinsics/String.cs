using Dante.Asserts;
using Dante.Extensions;
using Dante.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Z3;

namespace Dante.Intrinsics;

internal static class StringIntrinsics
{
    private static FuncDecl? _toLowerFunc;
    private static FuncDecl? _toUpperFunc;
    private static FuncDecl? _trimBeginFunc;
    private static FuncDecl? _trimEndFunc;
    private static FuncDecl? _trimFunc;
    private static FuncDecl? _lastIndexOfFunc;
    private static FuncDecl? _splitFunc;
    private static readonly ILogger Logger = LoggerFactory.Create(typeof(StringIntrinsics));

    public static Expr AsStringMethodCall(IInvocationOperation invocation,
        ExpressionGenerator callingExpressionGenerator)
    {
        var generationContext = GenerationContext.GetInstance();
        var solverContext = generationContext.SolverContext;
        var instance = invocation.Instance;
        var dotnetString = invocation.SemanticModel!.Compilation.GetTypeByMetadataName("System.String");
        SeqExpr thisArg;
        if (instance is not null)
        {
            instance.Type!.SpecialType.Should().Be(SpecialType.System_String, "string intrinsics must " +
                                                                              "be used with string expression only");

            var gen = instance.Accept(callingExpressionGenerator, generationContext);
            GenerationAsserts.RequireValidExpression<SeqExpr>(gen, instance);
            thisArg = (SeqExpr)gen!;
        }
        else // string static member
        {
            invocation.TargetMethod.ContainingType.Should().Be(dotnetString, SymbolEqualityComparer.Default,
                "string intrinsics must be used with string expression only");

            thisArg = Empty();
        }

        var sortPool = generationContext.SortPool;
        var charArraySort = sortPool.CharArraySort;
        var invokedMethod = invocation.TargetMethod;
        var invokedMethodName = invocation.TargetMethod.Name;
        switch (invokedMethodName)
        {
            case "CompareTo" when invokedMethod.Parameters is [{ } valueParam]:
            {
                if (valueParam.Type.Equals(dotnetString, SymbolEqualityComparer.Default))
                {
                    var argumentOperation = invocation.Arguments[0];
                    var arg = argumentOperation.Accept(callingExpressionGenerator, generationContext);
                    GenerationAsserts.RequireValidExpression(arg, argumentOperation);
                    return CompareTo(thisArg, (Expr)arg!);
                }

                break;
            }

            case "Compare" when invokedMethod.Parameters is [{ } strA, { } strB, { } ignoreCase]:
            {
                if (strA.Type.SpecialType is SpecialType.System_String &&
                    strB.Type.SpecialType is SpecialType.System_String &&
                    ignoreCase.Type.SpecialType is SpecialType.System_Boolean)
                {
                    var strAArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    var strBArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[1]);
                    var ignoreCaseArg = GenerateThenValidateArgument<BoolExpr>(invocation.Arguments[1]);
                    return Compare(strAArg, strBArg, ignoreCaseArg);
                }

                break;
            }

            case "Compare" when invokedMethod.Parameters is [{ } strA, { } strB]:
            {
                if (strA.Type.SpecialType is SpecialType.System_String &&
                    strB.Type.SpecialType is SpecialType.System_String)
                {
                    var strAArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    var strBArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[1]);
                    var ignoreCaseArg = GenerateThenValidateArgument<BoolExpr>(invocation.Arguments[1]);
                    return Compare(strAArg, strBArg, ignoreCaseArg);
                }

                break;
            }

            case "Compare" when invokedMethod.Parameters is
                [{ } strA, { } indexA, { } strB, { } indexB, { } length, { } ignoreCase]:
            {
                if (strA.Type.SpecialType is SpecialType.System_String &&
                    indexA.Type.SpecialType is SpecialType.System_Int32 &&
                    strB.Type.SpecialType is SpecialType.System_String &&
                    indexB.Type.SpecialType is SpecialType.System_Int32 &&
                    length.Type.SpecialType is SpecialType.System_Int32 &&
                    ignoreCase.Type.SpecialType is SpecialType.System_Boolean)
                {
                    var strAArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    var indexAArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[1]);
                    var strBArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[2]);
                    var indexBArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[3]);
                    var lengthArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[4]);
                    var ignoreCaseArg = GenerateThenValidateArgument<BoolExpr>(invocation.Arguments[5]);
                    return Compare(strAArg, indexAArg, strBArg, indexBArg, lengthArg, ignoreCaseArg);
                }

                break;
            }

            case "Compare" when invokedMethod.Parameters is [{ } strA, { } indexA, { } strB, { } indexB, { } length]:
            {
                if (strA.Type.SpecialType is SpecialType.System_String &&
                    indexA.Type.SpecialType is SpecialType.System_Int32 &&
                    strB.Type.SpecialType is SpecialType.System_String &&
                    indexB.Type.SpecialType is SpecialType.System_Int32 &&
                    length.Type.SpecialType is SpecialType.System_Int32)
                {
                    var strAArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    var indexAArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[1]);
                    var strBArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[2]);
                    var indexBArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[3]);
                    var lengthArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[4]);
                    return Compare(strAArg, indexAArg, strBArg, indexBArg, lengthArg);
                }

                break;
            }

            case "Substring" when invokedMethod.Parameters is [{ } index, { } length]:
            {
                if (index.Type.SpecialType is SpecialType.System_Int32 &&
                    length.Type.SpecialType is SpecialType.System_Int32)
                {
                    var indexArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[0]);
                    var lengthArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[1]);
                    return Substring(thisArg, indexArg, lengthArg);
                }

                break;
            }

            case "Substring" when invokedMethod.Parameters is [{ } index]:
            {
                if (index.Type.SpecialType is SpecialType.System_Int32)
                {
                    var indexArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[0]);
                    return Substring(thisArg, indexArg);
                }

                break;
            }

            case "Insert" when invokedMethod.Parameters is [{ } str, { } index]:
            {
                if (str.Type.SpecialType is SpecialType.System_String &&
                    index.Type.SpecialType is SpecialType.System_Int32)
                {
                    var strArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    var indexArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[1]);
                    return Insert(thisArg, strArg, indexArg);
                }

                break;
            }

            case "Remove" when invokedMethod.Parameters is [{ } index, { } count]:
            {
                if (index.Type.SpecialType is SpecialType.System_Int32 &&
                    count.Type.SpecialType is SpecialType.System_Int32)
                {
                    var indexArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[0]);
                    var countArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[1]);
                    return Remove(thisArg, indexArg, countArg);
                }

                break;
            }

            case "Remove" when invokedMethod.Parameters is [{ } index]:
            {
                if (index.Type.SpecialType is SpecialType.System_Int32)
                {
                    var indexArg = GenerateThenValidateArgument<IntExpr>(invocation.Arguments[0]);
                    return Remove(thisArg, indexArg);
                }

                break;
            }

            case "Trim" when invokedMethod.Parameters is [not null]:
            {
                if (invokedMethod.Parameters.First().Type.SpecialType is SpecialType.System_Char)
                {
                    var argumentOperation = invocation.Arguments[0];
                    var arg = argumentOperation.Accept(callingExpressionGenerator, generationContext);
                    GenerationAsserts.RequireValidExpression<SeqExpr>(arg, argumentOperation);
                    var charArg = (SeqExpr)arg!;
                    return Trim(thisArg, solverContext.MkConstArray(charArraySort, charArg));
                }

                if (invokedMethod.Parameters.First().Type is IArrayTypeSymbol arrayType)
                {
                    arrayType.ElementType.SpecialType.Should().Be(SpecialType.System_Char,
                        "cannot use 'Trim' intrinsic " +
                        "with non char array");
                    var argumentOperation = invocation.Arguments[0];
                    var arg = argumentOperation.Accept(callingExpressionGenerator, generationContext);
                    GenerationAsserts.RequireValidExpression<ArrayExpr>(arg, argumentOperation);
                    var charArrayArg = (ArrayExpr)arg!;
                    return Trim(thisArg, charArrayArg);
                }

                break;
            }

            case "TrimBegin" when invokedMethod.Parameters is [{ } ch]:
            {
                if (ch.Type.SpecialType is SpecialType.System_Char)
                {
                    var argumentOperation = invocation.Arguments[0];
                    var arg = argumentOperation.Accept(callingExpressionGenerator, generationContext);
                    GenerationAsserts.RequireValidExpression<SeqExpr>(arg, argumentOperation);
                    var charArg = (SeqExpr)arg!;
                    return TrimBegin(thisArg, solverContext.MkConstArray(charArraySort, charArg));
                }

                if (ch.Type is IArrayTypeSymbol arrayType)
                {
                    arrayType.ElementType.SpecialType.Should().Be(SpecialType.System_Char,
                        "cannot use 'Trim' intrinsic " +
                        "with non char array");
                    var argumentOperation = invocation.Arguments[0];
                    var arg = argumentOperation.Accept(callingExpressionGenerator, generationContext);
                    GenerationAsserts.RequireValidExpression<ArrayExpr>(arg, argumentOperation);
                    var charArrayArg = (ArrayExpr)arg!;
                    return TrimBegin(thisArg, charArrayArg);
                }

                break;
            }

            case "TrimEnd" when invokedMethod.Parameters is [{ } ch]:
            {
                if (ch.Type.SpecialType is SpecialType.System_Char)
                {
                    var argumentOperation = invocation.Arguments[0];
                    var arg = argumentOperation.Accept(callingExpressionGenerator, generationContext);
                    GenerationAsserts.RequireValidExpression<SeqExpr>(arg, argumentOperation);
                    var charArg = (SeqExpr)arg!;
                    return TrimEnd(thisArg, solverContext.MkConstArray(charArraySort, charArg));
                }

                if (ch.Type is IArrayTypeSymbol arrayType)
                {
                    arrayType.ElementType.SpecialType.Should().Be(SpecialType.System_Char,
                        "cannot use 'Trim' intrinsic " +
                        "with non char array");
                    var argumentOperation = invocation.Arguments[0];
                    var arg = argumentOperation.Accept(callingExpressionGenerator, generationContext);
                    GenerationAsserts.RequireValidExpression<ArrayExpr>(arg, argumentOperation);
                    var charArrayArg = (ArrayExpr)arg!;
                    return TrimEnd(thisArg, charArrayArg);
                }

                break;
            }

            case "IndexOf" when invokedMethod.Parameters is [{ } str]:
            {
                if (str.Type.SpecialType is SpecialType.System_String)
                {
                    var strArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    return IndexOf(thisArg, strArg);
                }

                break;
            }

            case "LastIndexOf" when invokedMethod.Parameters is [{ } str]:
            {
                if (str.Type.SpecialType is SpecialType.System_String)
                {
                    var strArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    return LastIndexOf(thisArg, strArg);
                }

                break;
            }

            case "ToLower":
            {
                return ToLower(thisArg);
            }

            case "ToUpper":
            {
                return ToUpper(thisArg);
            }

            case "Contains" when invokedMethod.Parameters is [{ } str]:
            {
                if (str.Type.SpecialType is SpecialType.System_String or SpecialType.System_Char)
                {
                    var strArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    return Contains(thisArg, strArg);
                }

                break;
            }

            case "StartWith" when invokedMethod.Parameters is [{ } str]:
            {
                if (str.Type.SpecialType is SpecialType.System_String or SpecialType.System_Char)
                {
                    var strArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    return StartWith(thisArg, strArg);
                }

                break;
            }

            case "EndWith" when invokedMethod.Parameters is [{ } str]:
            {
                if (str.Type.SpecialType is SpecialType.System_String or SpecialType.System_Char)
                {
                    var strArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    return EndWith(thisArg, strArg);
                }

                break;
            }

            case "Replace" when invokedMethod.Parameters is [{ } oldString, { } with]:
            {
                if (oldString.Type.SpecialType is SpecialType.System_String or SpecialType.System_Char &&
                    with.Type.SpecialType is SpecialType.System_String or SpecialType.System_Char)
                {
                    var oldStringArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    var withArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[1]);
                    return Replace(thisArg, oldStringArg, withArg);
                }

                break;
            }

            case "Split" when invokedMethod.Parameters is [{ } separators]:
            {
                if (separators.Type.SpecialType is SpecialType.System_String or SpecialType.System_Char)
                {
                    var separatorArg = GenerateThenValidateArgument<SeqExpr>(invocation.Arguments[0]);
                    return Split(thisArg, solverContext.MkConstArray(charArraySort, separatorArg));
                }

                if (separators.Type is IArrayTypeSymbol arrayType)
                {
                    arrayType.ElementType.SpecialType.Should().Be(SpecialType.System_Char,
                        "cannot use 'split' intrinsic " +
                        "with non char array");

                    var separatorArg = GenerateThenValidateArgument<ArrayExpr>(invocation.Arguments[0]);
                    return Split(thisArg, separatorArg);
                }

                break;
            }
        }

        Logger.LogTrace("invocation to method '{method}' could not be resolved as string intrinsic, it well " +
                        "get generated as abstract method", invocation.TargetMethod);


        return Empty();

        T GenerateThenValidateArgument<T>(IArgumentOperation argumentOperation) where T : Expr
        {
            var arg = argumentOperation.Accept(callingExpressionGenerator, generationContext);
            GenerationAsserts.RequireValidExpression<T>(arg, argumentOperation);
            return (T)arg!;
        }
    }

    private static SeqExpr Empty()
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkEmptySeq(solverContext.StringSort);
    }

    private static BoolExpr IsIdentity(SeqExpr source)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkEq(source, Empty());
    }

    private static IntExpr Length(SeqExpr source)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkLength(source);
    }

    private static IntExpr Compare(SeqExpr lhs, SeqExpr rhs)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return (IntExpr)solverContext.MkITE(solverContext.MkEq(lhs, rhs), Constant.Zero,
            solverContext.MkITE(solverContext.MkStringLt(lhs, rhs), Constant.SOne, Constant.One));
    }

    private static IntExpr Compare(SeqExpr lhs, SeqExpr rhs, BoolExpr ignoreCase)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        var zero = Constant.Zero;
        var one = Constant.One;
        var sOne = Constant.SOne;
        var caseSensitiveComparison = solverContext.MkITE(solverContext.MkEq(lhs, rhs), zero,
            solverContext.MkITE(solverContext.MkStringLt(lhs, rhs), sOne, one));

        var loweredLhs = ToLower(lhs);
        var loweredRhs = ToLower(rhs);
        var caseInsensitiveComparison = solverContext.MkITE(solverContext.MkEq(loweredLhs, loweredRhs), zero,
            solverContext.MkITE(solverContext.MkStringLt(loweredLhs, loweredRhs), sOne, one));

        return (IntExpr)solverContext.MkITE(ignoreCase, caseInsensitiveComparison, caseSensitiveComparison);
    }

    private static IntExpr Compare(SeqExpr lhs, IntExpr lhsIndex, SeqExpr rhs, IntExpr rhsIndex, IntExpr length)
    {
        var lhsSubStr = Substring(lhs, lhsIndex, length);
        var rhsSubStr = Substring(rhs, rhsIndex, length);
        return Compare(lhsSubStr, rhsSubStr);
    }

    private static IntExpr Compare(SeqExpr lhs,
        IntExpr lhsIndex,
        SeqExpr rhs,
        IntExpr rhsIndex,
        IntExpr length,
        BoolExpr ignoreCase)
    {
        var lhsSubStr = Substring(lhs, lhsIndex, length);
        var rhsSubStr = Substring(rhs, rhsIndex, length);
        return Compare(lhsSubStr, rhsSubStr, ignoreCase);
    }

    private static IntExpr CompareTo(SeqExpr lhs, Expr rhs)
    {
        var rhsAsString = Convertor.AsStringExpression(rhs);
        return Compare(lhs, rhsAsString);
    }

    private static SeqExpr Substring(SeqExpr source, IntExpr index)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkExtract(source, index, (IntExpr)(solverContext.MkLength(source) - index - Constant.One));
    }

    private static SeqExpr Substring(SeqExpr source, IntExpr index, IntExpr length)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkExtract(source, index, length);
    }

    private static SeqExpr Insert(SeqExpr source, SeqExpr str, IntExpr index)
    {
        var lowerBound = Substring(source, Constant.Zero, index);
        var upperBound = Substring(source, index);
        var newString = Concat(lowerBound, str, upperBound);
        return newString;
    }

    private static SeqExpr Remove(SeqExpr source, IntExpr index)
    {
        return Substring(source, index);
    }

    private static SeqExpr Remove(SeqExpr source, IntExpr index, IntExpr count)
    {
        return Substring(source, index, count);
    }

    private static SeqExpr Concat(params SeqExpr[] strings)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkConcat(strings);
    }

    private static SeqExpr ToLower(SeqExpr str)
    {
        return (SeqExpr)DeclareOrGetToLowerFunc().Apply(str);
    }

    private static SeqExpr ToUpper(SeqExpr str)
    {
        return (SeqExpr)DeclareOrGetToUpperFunc().Apply(str);
    }

    private static SeqExpr Trim(SeqExpr source, ArrayExpr chars)
    {
        return (SeqExpr)DeclareOrGetTrimFunc().Apply(source, chars);
    }

    private static SeqExpr TrimBegin(SeqExpr source, ArrayExpr chars)
    {
        return (SeqExpr)DeclareOrGetTrimBeginFunc().Apply(source, chars);
    }

    private static SeqExpr TrimEnd(SeqExpr source, ArrayExpr chars)
    {
        return (SeqExpr)DeclareOrGetTrimEndFunc().Apply(source, chars);
    }

    private static IntExpr IndexOf(SeqExpr source, SeqExpr subString)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkIndexOf(source, subString, Constant.Zero);
    }

    private static IntExpr LastIndexOf(SeqExpr source, SeqExpr subString)
    {
        return (IntExpr)DeclareOrGetLastIndexOf().Apply(source, subString);
    }

    private static BoolExpr Contains(SeqExpr source, SeqExpr str)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkContains(source, str);
    }

    private static BoolExpr StartWith(SeqExpr source, SeqExpr prefix)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkPrefixOf(prefix, source);
    }

    private static BoolExpr EndWith(SeqExpr source, SeqExpr postfix)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkSuffixOf(postfix, source);
    }

    private static SeqExpr Replace(SeqExpr source, SeqExpr oldString, SeqExpr newString)
    {
        var solverContext = GenerationContext.GetInstance().SolverContext;
        return solverContext.MkReplace(source, oldString, newString);
    }

    private static ArrayExpr Split(SeqExpr source, ArrayExpr separators)
    {
        return (ArrayExpr)DeclareOrGetSplit().Apply(source, separators);
    }


    private static FuncDecl DeclareOrGetToLowerFunc()
    {
        if (_toLowerFunc is not null)
        {
            return _toLowerFunc;
        }

        var solverContext = GenerationContext.GetInstance().SolverContext;
        var stringSort = solverContext.StringSort;
        var intSort = solverContext.IntSort;
        //string ToLower(string source) => ToLowerCore(source,0);
        var toLowerFunc = solverContext.MkRecFuncDecl("ToLower", [stringSort], stringSort);
        //string ToLowerCore(string source,int index)
        var toLowerCoreFunc = solverContext.MkRecFuncDecl("ToLowerCore", [stringSort, intSort], stringSort);
        var sourceArg = (SeqExpr)solverContext.MkConstDecl("source", stringSort).Apply();
        var indexArg = solverContext.MkIntConst("index");
        //if (index < source.Length)
        //{
        //index < source.Length
        var lessThanLength = solverContext.MkLt(indexArg, solverContext.MkLength(sourceArg));
        //var ch = source[index]
        var ch = solverContext.MkAt(sourceArg, indexArg);
        //if (ch >= 'A' && ch <= 'Z')
        //{
        //ch >= 'A' && ch <= 'Z'
        var isCapitalExpression = solverContext.MkAnd(
            solverContext.MkStringGe(ch, solverContext.MkString("A")),
            solverContext.MkStringLe(ch, solverContext.MkString("Z"))
        );
        //return (char)(ch+32) + ToLowerCore(source,index+1)
        //}
        var lowerChar = solverContext.IntToString(solverContext.CharToInt(ch) + solverContext.MkInt(32));
        var incrementIndex = indexArg + Constant.One;
        var concatLoweredAndContinue =
            solverContext.MkConcat(lowerChar, (SeqExpr)toLowerCoreFunc.Apply(sourceArg, incrementIndex));
        //else
        //{
        //  return ch + ToLowerCore(source, index + 1);
        //}
        var concatAndContinue = solverContext.MkConcat(ch, (SeqExpr)toLowerCoreFunc.Apply(sourceArg, incrementIndex));
        var lowerOrTakeThenContinue =
            solverContext.MkITE(isCapitalExpression, concatLoweredAndContinue, concatAndContinue);
        //}
        //return string.Empty;
        var emptyString = solverContext.MkEmptySeq(solverContext.StringSort);
        var toLowerCoreFuncBody = solverContext.MkITE(lessThanLength, lowerOrTakeThenContinue, emptyString);
        solverContext.AddRecDef(toLowerCoreFunc, [sourceArg, indexArg], toLowerCoreFuncBody);
        var toLowerFuncBody = toLowerCoreFunc.Apply(sourceArg, Constant.Zero);
        solverContext.AddRecDef(toLowerFunc, [sourceArg], toLowerFuncBody);
        _toLowerFunc = toLowerFunc;

        return _toLowerFunc;
    }

    private static FuncDecl DeclareOrGetToUpperFunc()
    {
        if (_toUpperFunc is not null)
        {
            return _toUpperFunc;
        }

        var solverContext = GenerationContext.GetInstance().SolverContext;
        var stringSort = solverContext.StringSort;
        var intSort = solverContext.IntSort;
        //string ToUpper(string source) => ToUpperCore(source,0);
        var toUpperFunc = solverContext.MkRecFuncDecl("ToUpper", [stringSort], stringSort);
        //string ToUpperCore(string source,int index)
        var toUpperCoreFunc = solverContext.MkRecFuncDecl("ToUpperCore", [stringSort, intSort], stringSort);
        var sourceArg = (SeqExpr)solverContext.MkConstDecl("source", stringSort).Apply();
        var indexArg = solverContext.MkIntConst("index");
        //if (index < source.Length)
        //{
        //index < source.Length
        var lessThanLength = solverContext.MkLt(indexArg, solverContext.MkLength(sourceArg));
        //var ch = source[index]
        var ch = solverContext.MkAt(sourceArg, indexArg);
        //if (ch >= 'a' && ch <= 'z')
        //{
        //ch >= 'a' && ch <= 'z'
        var isSmallExpression = solverContext.MkAnd(
            solverContext.MkStringGe(ch, solverContext.MkString("a")),
            solverContext.MkStringLe(ch, solverContext.MkString("z"))
        );
        //return (char)(ch+32) + ToUpperCore(source,index+1)
        //}
        var upperChar = solverContext.IntToString(solverContext.CharToInt(ch) - solverContext.MkInt(32));
        var incrementIndex = indexArg + Constant.One;
        var concatCapitalizedAndContinue =
            solverContext.MkConcat(upperChar, (SeqExpr)toUpperCoreFunc.Apply(sourceArg, incrementIndex));
        //else
        //{
        //  return ch + ToUpperCore(source, index + 1);
        //}
        var concatAndContinue = solverContext.MkConcat(ch, (SeqExpr)toUpperCoreFunc.Apply(sourceArg, incrementIndex));
        var upperOrTakeThenContinue =
            solverContext.MkITE(isSmallExpression, concatCapitalizedAndContinue, concatAndContinue);
        //}
        //return string.Empty;
        var emptyString = solverContext.MkEmptySeq(solverContext.StringSort);
        var toUpperCoreFuncBody = solverContext.MkITE(lessThanLength, upperOrTakeThenContinue, emptyString);
        solverContext.AddRecDef(toUpperCoreFunc, [sourceArg, indexArg], toUpperCoreFuncBody);
        var toUpperFuncBody = toUpperCoreFunc.Apply(sourceArg, Constant.Zero);
        solverContext.AddRecDef(toUpperFunc, [sourceArg], toUpperFuncBody);
        _toUpperFunc = toUpperFunc;

        return _toUpperFunc;
    }

    private static FuncDecl DeclareOrGetTrimBeginFunc()
    {
        if (_trimBeginFunc is not null)
        {
            return _trimBeginFunc;
        }

        var solverContext = GenerationContext.GetInstance().SolverContext;
        var intSort = solverContext.IntSort;
        var stringSort = solverContext.StringSort;
        var arraySort = solverContext.MkArraySort(intSort, stringSort);
        //string TrimBegin(string source,char[] chars) => TrimBeginCore(source,chars,0);
        var trimBeginFunc = solverContext.MkFuncDecl("TrimBegin", [stringSort, arraySort], stringSort);
        //string TrimBeginCore(string source,char[] chars,int index);
        var trimBeginCoreFunc = solverContext.MkFuncDecl("TrimBeginCore", [stringSort, arraySort], stringSort);
        var sourceArg = (SeqExpr)solverContext.MkConstDecl("source", stringSort).Apply();
        var charsArg = (ArrayExpr)solverContext.MkConstDecl("chars", arraySort).Apply();
        var indexArg = solverContext.MkIntConst("index");
        /*
         * return chars.Contains(source[index]) && index < source.Length ?
         *       TrimBeginCore(source, chars, index + 1) :
         *       source.Substring(index);
         */

        //source[index]
        var targetedChar = solverContext.MkAt(sourceArg, Constant.One);
        //chars.Contains(source[index])
        var isTargetedChar = solverContext.MkSetMembership(targetedChar, charsArg);
        //index < source.Length
        var isInRange = solverContext.MkLt(indexArg, solverContext.MkLength(sourceArg));
        //chars.Contains(source[index]) && index < source.Length
        var checkTrim = solverContext.MkAnd(isTargetedChar, isInRange);
        //TrimBeginCore(source, chars, index + 1)
        var continueTrim = trimBeginCoreFunc.Apply(sourceArg, charsArg, indexArg + Constant.One);
        //source.Substring(index);
        var substrLength = solverContext.MkLength(sourceArg) - indexArg - Constant.One;
        var subStr = Substring(sourceArg, indexArg, (IntExpr)substrLength);
        //return ...
        var trimBeginCoreFuncBody = solverContext.MkITE(checkTrim, continueTrim, subStr);
        solverContext.AddRecDef(trimBeginCoreFunc, [sourceArg, charsArg, indexArg], trimBeginCoreFuncBody);
        solverContext.AddRecDef(trimBeginFunc, [sourceArg, charsArg],
            trimBeginCoreFunc.Apply(sourceArg, charsArg, Constant.Zero));
        return _trimBeginFunc = trimBeginFunc;
    }

    private static FuncDecl DeclareOrGetTrimEndFunc()
    {
        if (_trimEndFunc is not null)
        {
            return _trimEndFunc;
        }

        var solverContext = GenerationContext.GetInstance().SolverContext;
        var intSort = solverContext.IntSort;
        var stringSort = solverContext.StringSort;
        var arraySort = solverContext.MkArraySort(intSort, stringSort);
        //string TrimEnd(string source,char[] chars) => TrimBeginCore(source,chars,source.Length-1);
        var trimEndFunc = solverContext.MkFuncDecl("TrimEnd", [stringSort, arraySort], stringSort);
        //string TrimEndCore(string source,char[] chars,int index);
        var trimEndCoreFunc = solverContext.MkFuncDecl("TrimEndCore", [stringSort, arraySort], stringSort);
        var sourceArg = (SeqExpr)solverContext.MkConstDecl("source", stringSort).Apply();
        var charsArg = (ArrayExpr)solverContext.MkConstDecl("chars", arraySort).Apply();
        var indexArg = solverContext.MkIntConst("index");
        /*
         * return chars.Contains(source[index]) && index > 0 ?
         *      TrimEndCore(source, chars, index - 1) :
         *      source.Substring(index);
         */

        //source[index]
        var targetedChar = solverContext.MkAt(sourceArg, Constant.One);
        //chars.Contains(source[index])
        var isTargetedChar = solverContext.MkSetMembership(targetedChar, charsArg);
        //index > 0
        var isInRange = solverContext.MkGt(indexArg, Constant.Zero);
        //chars.Contains(source[index]) && index > 0
        var checkTrim = solverContext.MkAnd(isTargetedChar, isInRange);
        //TrimEndCore(source, chars, index + 1)
        var continueTrim = trimEndCoreFunc.Apply(sourceArg, charsArg, indexArg - Constant.One);
        //source.Substring(index);
        var substrLength = solverContext.MkLength(sourceArg) - indexArg - Constant.One;
        var subStr = Substring(sourceArg, indexArg, (IntExpr)substrLength);
        //return ...
        var trimBeginCoreFuncBody = solverContext.MkITE(checkTrim, continueTrim, subStr);
        solverContext.AddRecDef(trimEndCoreFunc, [sourceArg, charsArg, indexArg], trimBeginCoreFuncBody);
        solverContext.AddRecDef(trimEndFunc, [sourceArg, charsArg], trimEndCoreFunc.Apply(sourceArg, charsArg,
            solverContext.MkLength(sourceArg) - 1));
        return _trimEndFunc = trimEndFunc;
    }

    private static FuncDecl DeclareOrGetTrimFunc()
    {
        if (_trimFunc is not null)
        {
            return _trimFunc;
        }

        var solverContext = GenerationContext.GetInstance().SolverContext;
        var intSort = solverContext.IntSort;
        var stringSort = solverContext.StringSort;
        var arraySort = solverContext.MkArraySort(intSort, stringSort);
        //string Trim(string source,char[] chars) => TrimBegin(TrimEnd(source,chars),chars);
        var trimFunc = solverContext.MkFuncDecl("TrimEnd", [stringSort, arraySort], stringSort);
        var sourceArg = (SeqExpr)solverContext.MkConstDecl("source", stringSort).Apply();
        var charsArg = (ArrayExpr)solverContext.MkConstDecl("chars", arraySort).Apply();
        solverContext.AddRecDef(trimFunc, [sourceArg, charsArg], TrimBegin(TrimEnd(sourceArg, charsArg), charsArg));
        _trimFunc = trimFunc;
        return _trimFunc;
    }


    private static FuncDecl DeclareOrGetLastIndexOf()
    {
        if (_lastIndexOfFunc is not null)
        {
            return _lastIndexOfFunc;
        }

        var solverContext = GenerationContext.GetInstance().SolverContext;
        var intSort = solverContext.IntSort;
        var stringSort = solverContext.StringSort;

        //int LastIndexOf(string s,string c) => LastIndexOfCore(s,c,-1);
        var lastIndexOfFunc = solverContext.MkFuncDecl("LastIndexOf", [stringSort, stringSort], intSort);
        /*int LastIndexOfCore(string s,string c,int lastIndex)
         *{
         *  var lastIndexOf = s.IndexOf(c);
         *  return lastIndexOf != -1 ? LastIndexOfCore(s.Substring(lastIndexOf + c.Length), c,lastIndex) : lastIndex;
         *}
         */
        var lastIndexOfCoreFunc =
            solverContext.MkFuncDecl("LastIndexOfCore", [stringSort, stringSort, intSort], intSort);
        var sArg = (SeqExpr)solverContext.MkConstDecl("s", stringSort).Apply();
        var cArg = (SeqExpr)solverContext.MkConstDecl("c", stringSort).Apply();
        var lastIndexArg = solverContext.MkIntConst("lastIndex");
        //var lastIndexOf = s.IndexOf(c);
        var lastIndexOf = IndexOf(sArg, cArg);
        // return lastIndexOf == -1 ? lastIndex : LastIndexOfCore(s.Substring(lastIndexOf), c,lastIndex);
        var lastIndexOfCoreFuncBody = solverContext.MkITE(solverContext.MkEq(lastIndexOf, Constant.SOne),
            lastIndexArg,
            lastIndexOfCoreFunc.Apply(Substring(sArg, (IntExpr)(lastIndexOf + solverContext.MkLength(cArg))), cArg,
                lastIndexOf));

        solverContext.AddRecDef(lastIndexOfCoreFunc, [sArg, cArg, lastIndexArg], lastIndexOfCoreFuncBody);
        solverContext.AddRecDef(lastIndexOfFunc, [sArg, cArg], lastIndexOfCoreFunc.Apply(sArg, cArg, Constant.SOne));
        _lastIndexOfFunc = lastIndexOfFunc;
        return _lastIndexOfFunc;
    }

    private static FuncDecl DeclareOrGetSplit()
    {
        if (_splitFunc is not null)
        {
            return _splitFunc;
        }

        var solverContext = GenerationContext.GetInstance().SolverContext;
        var charSort = solverContext.CharSort;
        var stringSort = solverContext.StringSort;
        var intSort = solverContext.IntSort;
        var charArraySort = solverContext.MkArraySort(intSort, charSort);
        var stringArraySort = solverContext.MkArraySort(intSort, stringSort);
        /*
         * public static  ImmutableArray<string> Split(string str, char[] separators,int index, int lastSpeIndex)
           {
               if (index >= str.Length)
               {
                   if (lastSpeIndex < index)
                   {
                       return ImmutableArray<string>.Empty.Add(str.Substring(lastSpeIndex, index - lastSpeIndex));
                   }

                   return ImmutableArray<string>.Empty;
               }

               if (separators.Contains(str[index]))
               {
                   var seperatedString = str.Substring(lastSpeIndex, index - lastSpeIndex);
                   return string.IsNullOrEmpty(seperatedString) ?
                       Split(str,separators,index + 1, index + 1):
                       Split(str,separators,index + 1, index + 1).Add(seperatedString);
               }

               return Split(str, separators, index + 1, lastSpeIndex);
           }
         */
        //ImmutableArray<string> Split(string str,char[] separators) => SplitCore(s,separators,0,0) 
        var splitFunc = solverContext.MkFuncDecl("Split", [stringSort, charArraySort], stringArraySort);
        //ImmutableArray<string> SplitCore(string str, char[] separators,int index, int lastSpeIndex)
        var splitCoreFunc = solverContext.MkFuncDecl("SplitCore",
            [stringSort, charArraySort, intSort, intSort, stringArraySort], stringArraySort);
        var strArg = (SeqExpr)solverContext.MkConstDecl("str", stringSort).Apply();
        var separatorsArg = (ArrayExpr)solverContext.MkConstDecl("separators", charArraySort).Apply();
        var indexArg = solverContext.MkIntConst("index");
        var lastSpeIndexArg = solverContext.MkIntConst("lastSpeIndex");
        //index >= str.Length
        var isOutOfRange = indexArg >= Length(strArg);
        //lastSpeIndex < index
        var isResultString = lastSpeIndexArg < indexArg;
        //str.Substring(lastSpeIndex, index - lastSpeIndex);
        var resultString = Substring(strArg, lastSpeIndexArg, (IntExpr)(indexArg - lastSpeIndexArg));
        //ImmutableArray<string>.Empty.Add(str.Substring(lastSpeIndex, index - lastSpeIndex));
        var resultArray = solverContext.MkSetAdd(solverContext.MkEmptySet(stringArraySort), resultString);
        //if (lastSpeIndex < index) {...} return ImmutableArray<string>.Empty;
        var isResultStringBlock =
            solverContext.MkITE(isResultString, resultArray, solverContext.MkEmptySet(stringArraySort));
        //separators.Contains(str[index])
        var isSeparatorChar = solverContext.MkSetMembership(solverContext.MkAt(strArg, indexArg), separatorsArg);
        // var seperatedString = str.Substring(lastSpeIndex, index - lastSpeIndex);
        var seperatedString = Substring(strArg, lastSpeIndexArg, (IntExpr)(indexArg - lastSpeIndexArg));
        /*
         * return string.IsNullOrEmpty(seperatedString) ?
           Split(str,separators,index + 1, index + 1):
           Split(str,separators,index + 1, index + 1).Add(seperatedString);
         */
        var incrementIndex = indexArg + Constant.One;
        var maySplitThenContinue = solverContext.MkITE(
            IsIdentity(seperatedString),
            splitCoreFunc.Apply(strArg, separatorsArg, incrementIndex, incrementIndex),
            solverContext.MkSetAdd(
                (ArrayExpr)splitCoreFunc.Apply(strArg, separatorsArg, incrementIndex, incrementIndex),
                seperatedString
            )
        );

        //if (index >= str.Length){...} if(separators.Contains(str[index])){...} return Split(str, separators, index + 1, lastSpeIndex);
        var body = solverContext.MkITE(isOutOfRange, isResultStringBlock,
            solverContext.MkITE(isSeparatorChar, maySplitThenContinue,
                splitCoreFunc.Apply(strArg, separatorsArg, incrementIndex, lastSpeIndexArg)));

        solverContext.AddRecDef(splitCoreFunc, [strArg, separatorsArg, indexArg, lastSpeIndexArg], body);
        solverContext.AddRecDef(splitFunc, [strArg, separatorsArg],
            splitCoreFunc.Apply(strArg, separatorsArg, indexArg, Constant.Zero, Constant.Zero));

        _splitFunc = splitFunc;
        return _splitFunc;
    }
}