using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Dante.Asserts;
using Dante.Extensions;
using Dante.Intrinsics;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Z3;

namespace Dante.Generators;

internal sealed partial class FunctionGenerator
{
    private readonly Context _context;
    private readonly CallableDeclarationAdapter _ownerCallable;
    private readonly ControlFlowGraph _ownerMethodCfg;
    private readonly BasicBlockAnnotator _basicBlockAnnotator;
    private readonly FunctionGenerationContext _genContext;
    private readonly Sort _functionSort;
    private readonly Dictionary<BasicBlock, BasicBlockInfo> _generatedBlocks;
    private readonly SymbolEvaluationTable _functionDefaultEvalTable = new();
    private readonly FlowCaptureTable _flowCaptureTable = new();
    private readonly LivenessDataflowAnalyzer _livenessDataflowAnalyzer;
    private static readonly ILogger<FunctionGenerator> Logger = LoggerFactory.Create<FunctionGenerator>();

    private FunctionGenerator(
        CallableDeclarationAdapter ownerCallable,
        ControlFlowGraph ownerMethodCfg,
        uint graphId)
    {
        _genContext = FunctionGenerationContext.Create(ownerMethodCfg);
        _context = _genContext.SolverContext;
        var ownerCompilation = _genContext.Compilation;
        _ownerCallable = ownerCallable;
        _ownerMethodCfg = ownerMethodCfg;
        _basicBlockAnnotator = new BasicBlockAnnotator(ownerMethodCfg, graphId);
        _functionSort = ownerCallable.ReturnType.AsSort();
        _generatedBlocks =
            new Dictionary<BasicBlock, BasicBlockInfo>(ownerMethodCfg.Blocks.Length,
                ReferenceEqualityComparer.Instance);
        foreach (var parameter in ownerCallable.Parameters)
        {
            GenerateFunctionParameter(parameter, _functionDefaultEvalTable);
        }

        _livenessDataflowAnalyzer = new LivenessDataflowAnalyzer(
            ownerCallable,
            ownerMethodCfg,
            ownerCompilation.GetSemanticModel(_ownerCallable.SyntaxTree)
        );
    }

    /// <summary>
    ///     generate a non-quantified bi-proposition that original function will always imply the transformed functions
    ///     for the same set of passed arguments and vice versa.
    /// </summary>
    /// <param name="originalMethod">original C# method generated function</param>
    /// <param name="transformedMethod">transformed C# method generated function</param>
    /// <param name="sharedContext">context that was used to generate both methods</param>
    /// <returns></returns>
    public static BoolExpr GenerateSatisfiabilityExpression(
        FuncDecl originalMethod,
        FuncDecl transformedMethod,
        GenerationContext sharedContext)
    {
        if (!originalMethod.HasSameParameters(transformedMethod))
        {
            throw new NotSupportedException("methods being compared do not have same parameter types");
        }

        var solverParameters = GeneratePropositionsArguments(originalMethod, sharedContext);
        var satExpr = !sharedContext.SolverContext.MkEq(
            originalMethod.Apply(solverParameters.ToArray()),
            transformedMethod.Apply(solverParameters.ToArray()));
        return satExpr;
    }

    public FuncDecl Generate()
    {
        return GenerateFunctionFromControlFlowGraph();
    }

    private static IReadOnlyList<Expr> GeneratePropositionsArguments(FuncDecl func, GenerationContext context)
    {
        var args = new List<Expr>(func.Domain.Length);
        args.AddRange(func.Domain.Select(param => context.SolverContext.MkFreshConst("param", param)));
        return args;
    }

    #region ControlFlowGraphGeneration

    private FuncDecl GenerateFunctionFromControlFlowGraph()
    {
        _livenessDataflowAnalyzer.Analyze();
        var entry = _ownerMethodCfg.Blocks[0];
        var entryFuncDecl = DeclareFunctionFromBasicBlock(entry);
        //we add entry basic block early to the global evaluation table so we can reuse/call declared func
        //recursively while generating other functions CFG.
        _genContext.GlobalEvaluationTable.TryAdd(_ownerCallable.UnderlyingSymbol, entryFuncDecl);
        return GenerateBasicBlockWithoutRecursion(entry);
    }

    private FuncDecl GenerateBasicBlock(BasicBlock basicBlock)
    {
        if (WasGenerated(basicBlock, out var declaredBasicBlockFunc))
        {
            return basicBlock.IsBooleanShortCircuitBlock(_ownerCallable)
                ? GenerateShortCircuitedBasicBlock(basicBlock)
                : declaredBasicBlockFunc;
        }

        return GenerateBasicBlockWithoutRecursion(basicBlock);
    }

    /// <summary>
    ///     generate basic block ignoring recursive calls down in the CFG
    /// </summary>
    /// <remarks>
    ///     misusing this function will cause stack overflow
    /// </remarks>
    /// <param name="basicBlock">basic block to start code generation from</param>
    /// <returns></returns>
    /// <exception cref="UnreachableException"></exception>
    private FuncDecl GenerateBasicBlockWithoutRecursion(BasicBlock basicBlock)
    {
        FuncDecl declaredBasicBlockFunc;
        if (basicBlock.IsConditionalBasicBlock())
        {
            declaredBasicBlockFunc = DeclareFunctionFromBasicBlock(basicBlock);
            var blockEvalTable = BuildBasicBlockEvalTable(basicBlock);
            var expressionGenerator = new ExpressionGenerator(blockEvalTable, _flowCaptureTable);
            EvaluateBasicBlockBody(basicBlock, expressionGenerator);
            var condition = basicBlock.BranchValue!.Accept(expressionGenerator, _genContext);

            GenerationAsserts.RequireValidExpression<BoolExpr>(condition, basicBlock.BranchValue);
            var fallThroughFuncCall = GenerateCallToBasicBlockSuccessorFunc(basicBlock, blockEvalTable);
            var conditionalFuncCall = GenerateCallToBasicBlockSuccessorFunc(basicBlock, blockEvalTable, false);
            var basicBlockBody = basicBlock.ConditionKind is ControlFlowConditionKind.WhenTrue
                ? _context.MkITE((BoolExpr)condition!, conditionalFuncCall, fallThroughFuncCall)
                : _context.MkITE((BoolExpr)condition!, fallThroughFuncCall, conditionalFuncCall);

            var declaredBasicBlockFuncParams = GenerateBasicBlockFuncParameterList(basicBlock);
            _context.AddRecDef(declaredBasicBlockFunc, declaredBasicBlockFuncParams, basicBlockBody);
            return declaredBasicBlockFunc;
        }

        if (basicBlock.IsReturnBasicBlock())
        {
            var (immDomFunc, returnFunc) = DeclareFunctionFromReturningBasicBlock(basicBlock);
            var immDomDeclaredBasicBlockFuncParams = GenerateBasicBlockFuncParameterList(basicBlock);
            var immDomBlockEvalTable = BuildBasicBlockEvalTable(basicBlock);
            var immDomExpressionGenerator = new ExpressionGenerator(immDomBlockEvalTable, _flowCaptureTable);
            EvaluateBasicBlockBody(basicBlock, immDomExpressionGenerator);

            var returnBlock = basicBlock.FallThroughSuccessor!.Destination!;
            //if the returning block is an exit block that means the basic block that the immediate dominator
            //is falling through to is not really the returning block! actually, the returning block
            //is the immediate dominator block itself 
            if (returnBlock.Kind is BasicBlockKind.Exit)
            {
                var returningBlockBody = GenerateExitBasicBlockBody(basicBlock, immDomExpressionGenerator);
                _context.AddRecDef(immDomFunc, immDomDeclaredBasicBlockFuncParams, returningBlockBody);
                return immDomFunc;
            }

            var returnFuncParams = GenerateBasicBlockFuncParameterList(returnBlock);
            var expressionGenerator =
                new ExpressionGenerator(immDomBlockEvalTable, _flowCaptureTable, returnFuncParams);
            var returnFuncBody = GenerateExitBasicBlockBody(basicBlock, expressionGenerator);
            _context.AddRecDef(returnFunc, returnFuncParams, returnFuncBody);

            var returnFuncArgs = GenerateBasicBlockFuncArgumentList(returnBlock, immDomBlockEvalTable);
            var immDomFuncBody = returnFunc.Apply(returnFuncArgs);
            _context.AddRecDef(immDomFunc, immDomDeclaredBasicBlockFuncParams, immDomFuncBody);
            return immDomFunc;
        }

        if (basicBlock.FallThroughSuccessor?.Destination is not null)
        {
            declaredBasicBlockFunc = DeclareFunctionFromBasicBlock(basicBlock);
            var blockEvalTable = BuildBasicBlockEvalTable(basicBlock);
            var expressionGenerator = new ExpressionGenerator(blockEvalTable, _flowCaptureTable);
            EvaluateBasicBlockBody(basicBlock, expressionGenerator);
            var successorCall = GenerateCallToBasicBlockSuccessorFunc(basicBlock, blockEvalTable);
            var declaredBasicBlockFuncParams = GenerateBasicBlockFuncParameterList(basicBlock);
            _context.AddRecDef(declaredBasicBlockFunc, declaredBasicBlockFuncParams, successorCall);
            return declaredBasicBlockFunc;
        }

        throw new UnreachableException();
    }

    private void EvaluateBasicBlockBody(BasicBlock basicBlock, ExpressionGenerator expressionGenerator)
    {
        foreach (var operation in basicBlock.Operations)
        {
            var genExpr = operation.Accept(expressionGenerator, _genContext);
            GenerationAsserts.RequireValidExpression(genExpr, operation);
            expressionGenerator.InvalidateTemporaries();
        }
    }

    private Expr GenerateExitBasicBlockBody(BasicBlock basicBlock, ExpressionGenerator expressionGenerator)
    {
        var returnOperand = basicBlock.BranchValue;
        var genReturnExpression = returnOperand?.Accept(expressionGenerator, _genContext) as Expr;
        GenerationAsserts.RequireValidExpression(genReturnExpression, returnOperand!);
        if (_ownerCallable.ReturnType.IsNullable() && !UnderlyingType.IsMaybe(genReturnExpression!))
        {
            var maybe = MaybeIntrinsics.CreateOrGet(returnOperand!.Type!);
            return MaybeIntrinsics.Some(maybe, genReturnExpression!);
        }

        return genReturnExpression!;
    }

    private FuncDecl GenerateShortCircuitedBasicBlock(BasicBlock block)
    {
        var blockName = _basicBlockAnnotator.GenerateBlockName();
        var flowCaptureRefOp = (IFlowCaptureReferenceOperation)block.BranchValue!;
        var operand = _flowCaptureTable.Fetch(flowCaptureRefOp);
        operand.Should().BeOfType<BoolExpr>();
        var ctx = operand.Context;
        var func = ctx.MkRecFuncDecl(blockName, [], ctx.BoolSort);
        ctx.AddRecDef(func, [], operand);
        return func;
    }


    private Expr GenerateCallToBasicBlockSuccessorFunc(
        BasicBlock basicBlock,
        SymbolEvaluationTable evaluationTable,
        bool fallthrough = true)
    {
        var successor = fallthrough
            ? basicBlock.FallThroughSuccessor!.Destination!
            : basicBlock.ConditionalSuccessor!.Destination!;

        var successorFunc = GenerateBasicBlock(successor);
        if (successorFunc.Arity is 0)
        {
            return successorFunc.Apply();
        }

        var arguments = GenerateBasicBlockFuncArgumentList(successor, evaluationTable);
        var callSuccessor = successorFunc.Apply(arguments);
        return callSuccessor;
    }

    private Expr[] GenerateBasicBlockFuncArgumentList(BasicBlock targetBlock, SymbolEvaluationTable evaluationTable)
    {
        _generatedBlocks.TryGetValue(targetBlock, out var basicBlockInfo);
        basicBlockInfo.Should().NotBeNull("fetching basic block info failed as basic block was not generated before");
        var arguments = new List<Expr>();
        foreach (var dependentOnSymbol in basicBlockInfo!.DependentOnSymbols)
        {
            var argument = dependentOnSymbol switch
            {
                //be more tolerant in release even if it means less optimized code
#if RELEASE
                ILocalSymbol local => GenerateFunctionLocal(local, evaluationTable),
                IParameterSymbol parameter => GenerateFunctionParameter(parameter, evaluationTable),
                _ => null
#else
                ILocalSymbol local => GenerateFunctionLocal(local, evaluationTable, true),
                IParameterSymbol parameter => GenerateFunctionParameter(parameter, evaluationTable, true),
                _ => null
#endif
            };

            if (argument is not null)
            {
                arguments.Add(argument);
            }
            else
            {
                var dependency = _generatedBlocks[targetBlock];
                if (dependency.BasicBlockEvaluationTable.TryFetch(dependentOnSymbol, out Expr? dependencyValue))
                {
                    arguments.Add(dependencyValue);
                }
            }
        }

        return arguments.ToArray();
    }

    private Expr[] GenerateBasicBlockFuncParameterList(BasicBlock basicBlock)
    {
        _generatedBlocks.TryGetValue(basicBlock, out var basicBlockInfo);
        basicBlockInfo.Should().NotBeNull("fetching basic block info failed as basic block was not generated before");
        var paramList = basicBlockInfo!
            .DependentOnSymbols
            .Select(symbol => symbol is ILocalSymbol localSymbol
                ? _context.MkConstDecl(localSymbol.Name, localSymbol.Type.AsSort())
                : _context.MkConstDecl(((IParameterSymbol)symbol).Name,
                    ((IParameterSymbol)symbol).Type.AsSort()))
            .Select(constDecl => constDecl.Apply())
            .ToArray();

        return paramList;
    }

    #endregion ControlFlowGraphGeneration


    #region LocalsAndParamters

    private Expr? GenerateFunctionParameter(IParameterSymbol parameterSymbol,
        SymbolEvaluationTable basicBlockEvalTable,
        bool existingOnly = false)
    {
        return GenerateFunctionScopedSymbol(parameterSymbol, parameterSymbol.Type, basicBlockEvalTable, existingOnly);
    }

    private Expr? GenerateFunctionLocal(ILocalSymbol localSymbol,
        SymbolEvaluationTable basicBlockEvalTable,
        bool existingOnly = false)
    {
        return GenerateFunctionScopedSymbol(localSymbol, localSymbol.Type, basicBlockEvalTable, existingOnly);
    }

    private Expr? GenerateFunctionScopedSymbol(ISymbol localSymbol,
        ITypeSymbol symbolType,
        SymbolEvaluationTable basicBlockEvalTable,
        bool existingOnly = false)
    {
        if (basicBlockEvalTable.TryFetch(localSymbol, out Expr? paramExpr))
        {
            //symbol is nullable but it's value in the evaluation table is not, such case could simply happen when 
            //you reassign a non-nullable to nullable symbol (x = "string" //x here is nullable)
            //and based on this Roslyn do not produce conversion operation, in such case the compiler here will
            //not generate the value as maybe expression but as concrete expression of some type T and the symbol
            //will get evaluated based on this, here we do conversion to maybe expression if symbol is nullable 
            //because symbol will get used successor basic blocks so we should keep the typing information as-is
            if (symbolType.IsNullable() && !UnderlyingType.IsMaybe(paramExpr))
            {
                var maybe = MaybeIntrinsics.CreateOrGet(symbolType);
                return MaybeIntrinsics.Some(maybe, paramExpr);
            }

            return paramExpr;
        }

        if (!existingOnly)
        {
            var symbolSort = symbolType.AsSort();
            var declaredParameterSymbol = _context.MkConstDecl(localSymbol.Name, symbolSort);
            //for nullable objects apply will yield to the usage of 'None' type constructor of respective maybe monad implicitly
            paramExpr = declaredParameterSymbol.Apply();
            basicBlockEvalTable.AddThenBind(localSymbol, declaredParameterSymbol, paramExpr);
            return paramExpr;
        }

        return null;
    }

    #endregion LocalsAndParamters

    public static FuncDecl? DeclareFunctionFromMethod(IMethodSymbol method, GenerationContext context)
    {
        if (context.GlobalEvaluationTable.TryFetch(method, out FuncDecl? func))
        {
            return func;
        }

        var returnType = method.ReturnType;
        if (returnType.IsVoid())
        {
            Logger.LogWarning("skipping generation of method '{method}' because it returns void", method);
            return default;
        }

        var funcSort = returnType.AsSort();
        var funcParamsSorts = method
            .Parameters
            .Select(param => param.Type.AsSort())
            .ToArray();

        var methodFullName =
            method.ToDisplayString(new SymbolDisplayFormat().WithParameterOptions(SymbolDisplayParameterOptions.None));
        func = context.SolverContext.MkRecFuncDecl(methodFullName, funcParamsSorts, funcSort);
        context.GlobalEvaluationTable.TryAdd(method, func);
        return func;
    }

    private FuncDecl DeclareFunctionFromBasicBlock(BasicBlock basicBlock)
    {
        if (_generatedBlocks.TryGetValue(basicBlock, out var basicBlockInfo))
        {
            return basicBlockInfo.GeneratedBasicBlock;
        }

        var basicBlockName = _basicBlockAnnotator.GenerateBlockName();
        var basicBlockLivenessAnalysis = _livenessDataflowAnalyzer.GetLivenessInfoOf(basicBlock);
        IReadOnlyList<ISymbol> dependentOnSymbols;
        if (_ownerCallable.IsAnonymousLambda)
        {
            dependentOnSymbols = _ownerCallable.Parameters;
        }
        else
        {
            dependentOnSymbols = [..basicBlockLivenessAnalysis.FlowIn];
        }

        var basicBlockSymbols = basicBlockLivenessAnalysis.LiveIn
            .Union(basicBlockLivenessAnalysis.LiveOut)
            .Union(basicBlockLivenessAnalysis.RegionLocals)
            .ToImmutableArray();

        dependentOnSymbols = dependentOnSymbols.OrderBy(symbol => symbol.Name).ToArray();
        var paramsSort = dependentOnSymbols
            .OrderBy(symbol => symbol.Name)
            .Select(symbol =>
                symbol is ILocalSymbol localSymbol
                    ? localSymbol.Type.AsSort()
                    : ((IParameterSymbol)symbol).Type.AsSort())
            .ToArray();

        var basicBlockDecl = _context.MkRecFuncDecl(basicBlockName, paramsSort, _functionSort);
        _generatedBlocks.TryAdd(basicBlock, new BasicBlockInfo
        {
            GeneratedBasicBlock = basicBlockDecl,
            DependentOnSymbols = dependentOnSymbols,
            BasicBlockLexicalSymbols = basicBlockSymbols
        });
        return basicBlockDecl;
    }

    private (FuncDecl exitImmediateDomintaorFunc, FuncDecl exitFunc) DeclareFunctionFromReturningBasicBlock(
        BasicBlock basicBlock)
    {
        basicBlock.IsReturnBasicBlock().Should().BeTrue("trying to declare exit functions out of " +
                                                        "non exit basic block");

        if (_generatedBlocks.TryGetValue(basicBlock, out var basicBlockInfo))
        {
            return new ValueTuple<FuncDecl, FuncDecl>(basicBlockInfo.GeneratedBasicBlockImmediateDominator!,
                basicBlockInfo.GeneratedBasicBlock);
        }

        var immediateDominatorBlockFunc = DeclareFunctionFromBasicBlock(basicBlock);
        var basicBlockName = _basicBlockAnnotator.GenerateBlockName();
        var returnLiveness = _livenessDataflowAnalyzer.GetLivenessInfoOf(basicBlock);
        IReadOnlyList<ISymbol> exitBasicBlockSymbols;
        if (!_ownerCallable.IsAnonymousLambda)
        {
            exitBasicBlockSymbols = [..returnLiveness.FlowIn];
        }
        else
        {
            exitBasicBlockSymbols = _ownerCallable.Parameters;
        }

        exitBasicBlockSymbols = exitBasicBlockSymbols.OrderBy(symbol => symbol.Name).ToArray();
        var paramsSort = exitBasicBlockSymbols
            .Select(symbol =>
                symbol is ILocalSymbol localSymbol
                    ? localSymbol.Type.AsSort()
                    : ((IParameterSymbol)symbol).Type.AsSort())
            .ToArray();

        var basicBlockDecl = _context.MkRecFuncDecl(basicBlockName, paramsSort, _functionSort);
        //the actual exit block which is the immediate successor of the passed basic block
        var exitBlock = basicBlock.FallThroughSuccessor!.Destination!;
        _generatedBlocks.TryAdd(exitBlock, new BasicBlockInfo
        {
            GeneratedBasicBlockImmediateDominator = immediateDominatorBlockFunc,
            GeneratedBasicBlock = basicBlockDecl,
            DependentOnSymbols = exitBasicBlockSymbols,
            BasicBlockLexicalSymbols = exitBasicBlockSymbols
        });
        return (immediateDominatorBlockFunc, basicBlockDecl);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WasGenerated(BasicBlock basicBlock, [MaybeNullWhen(false)] out FuncDecl generatedBlockFunc)
    {
        var wasGenerated = _generatedBlocks.TryGetValue(basicBlock, out var generatedBlockInfo);
        if (wasGenerated)
        {
            generatedBlockFunc = generatedBlockInfo!.GeneratedBasicBlock;
            return true;
        }

        generatedBlockFunc = null;
        return false;
    }

    private SymbolEvaluationTable BuildBasicBlockEvalTable(BasicBlock basicBlock)
    {
        _generatedBlocks.TryGetValue(basicBlock, out var basicBlockInfo);
        basicBlockInfo.Should().NotBeNull("fetching basic block info failed as basic block was not generated before");
        var evalTable = new SymbolEvaluationTable();
        foreach (var symbol in basicBlockInfo!.BasicBlockLexicalSymbols)
        {
            switch (symbol)
            {
                case ILocalSymbol local:
                    GenerateFunctionLocal(local, evalTable);
                    break;
                case IParameterSymbol parameter:
                    GenerateFunctionParameter(parameter, evalTable);
                    break;
                case ILabelSymbol label:
                    label.Should().NotBeNull();
                    break;
                default:
                    throw new UnreachableException();
            }
        }

        basicBlockInfo.BasicBlockEvaluationTable = evalTable;
        return evalTable;
    }
}