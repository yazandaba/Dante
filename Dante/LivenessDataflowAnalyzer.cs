using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Dante;

using DependencyLocals = Dictionary<StatementSyntax, ImmutableHashSet<ISymbol>>;

internal readonly struct LivenessInfo : IEquatable<LivenessInfo>
{
    public static LivenessInfo Default { get; } = new() { BasicBlock = null! };
    public required BasicBlock BasicBlock { get; init; }
    public ImmutableHashSet<ISymbol> LiveOut { get; init; } = ImmutableHashSet<ISymbol>.Empty;
    public ImmutableHashSet<ISymbol> LiveIn { get; init; } = ImmutableHashSet<ISymbol>.Empty;
    public ImmutableHashSet<ISymbol> RegionLocals { get; init; } = ImmutableHashSet<ISymbol>.Empty;

    public LivenessInfo()
    {
    }

    public bool Equals(LivenessInfo other)
    {
        return BasicBlock == other.BasicBlock;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is LivenessInfo livenessInfo &&
               ReferenceEquals(BasicBlock, livenessInfo.BasicBlock);
    }

    public override int GetHashCode()
    {
        return BasicBlock.GetHashCode();
    }

    public static bool IsDefault(in LivenessInfo livenessInfo)
    {
        return livenessInfo.Equals(Default);
    }
}

internal class LivenessDataflowAnalyzer
{
    private readonly Dictionary<BasicBlock, LivenessInfo> _basicBlocksLivenessInfo;

    /// <summary>
    ///     the table of regions that have local symbols which form a dependency to operations in that region
    /// </summary>
    /// <code>
    /// for (int i = 0; i &lt; 50 ;++i)
    /// {
    ///    x += 10;
    /// }
    /// </code>
    /// <remarks>
    ///     In the code example, there's a simple for loop. In the CFG, the loop termination expression (i &lt; 50)
    ///     forms a basic block that follows the previous basic block containing the initialization (int i = 0).
    ///     When we fetch the scope of the termination condition basic block (i &lt; 50), the enclosing for statement
    ///     becomes the relevant scope. However, this creates a problem: based on Roslyn's dataflow analysis results,
    ///     variable 'i' is classified as a local symbol (which is obviously) and gets excluded when computing
    ///     the live-in set for the termination condition basic block (i &lt; 50).
    ///     But since this basic block actually depends on 'i', the variable should be included in the region
    ///     for (i &lt; 50) while being excluded from its predecessor (i = 0).
    ///     This set of variables (when non-empty) serves as an indicator for the region scanning stage, signaling
    ///     to the liveness computation stage that it should include local variable 'i' from its predecessor
    ///     (more precisely, from its immediate dominator).
    /// </remarks>
    private readonly DependencyLocals _dependencyLocals;
    private readonly HashSet<BasicBlock> _worklist;
    private readonly SemanticModel _semantics;
    private readonly BasicBlock _root;
    private readonly CallableDeclarationAdapter _callable;

    public LivenessDataflowAnalyzer(CallableDeclarationAdapter callable,
        ControlFlowGraph controlFlowGraph,
        SemanticModel semantics)
    {
        _callable = callable;
        _semantics = semantics;
        _root = controlFlowGraph.Blocks[0];
        _basicBlocksLivenessInfo = new Dictionary<BasicBlock, LivenessInfo>(controlFlowGraph.Blocks.Length);
        _worklist = new HashSet<BasicBlock>(controlFlowGraph.Blocks.Length);
        _dependencyLocals = new DependencyLocals(controlFlowGraph.Blocks.Length, ReferenceEqualityComparer.Instance);
    }

    public void Analyze()
    {
        if (_root.FallThroughSuccessor?.Destination is { Kind: BasicBlockKind.Exit })
            //empty CFG
        {
            return;
        }

        var executableEntry = _root.FallThroughSuccessor!.Destination!;
        var executableEntryLiveness = AnalyzePartial(executableEntry);
        var entryLiveness = new LivenessInfo
        {
            BasicBlock = _root,
            LiveIn = _callable.Parameters.ToImmutableHashSet(SymbolEqualityComparer.Default)!,
            LiveOut = executableEntryLiveness.LiveIn
        };
        _basicBlocksLivenessInfo.Add(_root, entryLiveness);
        _worklist.Clear(); //start the second pass of the analysis with empty worklist
        AnalyzeComplete(_root);
        
        //so GC can cleanup the entries 
        _dependencyLocals.Clear();
        _worklist.Clear();
    }

    public LivenessInfo GetLivenessInfoOf(BasicBlock block)
    {
        _basicBlocksLivenessInfo
            .TryGetValue(block, out var livenessInfo)
            .Should()
            .BeTrue("you cannot get the liveness info of this block, did you run Analyze()?");

        return livenessInfo;
    }

    private LivenessInfo AnalyzePartial(BasicBlock block)
    {
        //the block is already in the worklist so we are dealing with cycle 
        if (_worklist.Contains(block))
        {
            return LivenessInfo.Default;
        }

        if (_basicBlocksLivenessInfo.TryGetValue(block, out var livenessInfo))
        {
            return livenessInfo;
        }

        _worklist.Add(block);
        //basic block is just an empty block that has two successors 
        //where execution may flow
        if (LivenessAnalysisHelper.IsBinaryFlowOnlyBlock(block))
        {
            var fallthrough = block.FallThroughSuccessor!.Destination!;
            var conditional = block.ConditionalSuccessor?.Destination!;
            var conditionalLiveness = fallthrough.Kind is BasicBlockKind.Exit
                ? LivenessInfo.Default
                : AnalyzePartial(conditional);
            var fallthroughLiveness = fallthrough.Kind is BasicBlockKind.Exit
                ? LivenessInfo.Default
                : AnalyzePartial(fallthrough);
            var region = GetPossibleRegionOfBranchingOnlyBlock(block);
            var analysis = AnalyzeDataFlowOfRegion(region);
            analysis.Should().NotBeNull();
            analysis.Succeeded.Should().BeTrue();
            return ComputeLiveness(block, region, fallthroughLiveness, conditionalLiveness, analysis);
        }

        if (LivenessAnalysisHelper.IsBranchingExecutableBlock(block))
        {
            var fallthrough = block.FallThroughSuccessor!.Destination!;
            var conditional = block.ConditionalSuccessor?.Destination!;
            var conditionalLiveness = fallthrough.Kind is BasicBlockKind.Exit
                ? LivenessInfo.Default
                : AnalyzePartial(conditional);
            var fallthroughLiveness = fallthrough.Kind is BasicBlockKind.Exit
                ? LivenessInfo.Default
                : AnalyzePartial(fallthrough);
            var operationSyntax = LivenessAnalysisHelper.GetFirstRelevantOperationInBlock(block);
            var region = LivenessAnalysisHelper.GetRegionOf(operationSyntax);
            var analysis = AnalyzeDataFlowOfRegion(region);
            analysis.Should().NotBeNull();
            analysis.Succeeded.Should().BeTrue();
            return ComputeLiveness(block, region, fallthroughLiveness, conditionalLiveness, analysis);
        }

        if (LivenessAnalysisHelper.IsSerialExecutableBlock(block))
        {
            var operationSyntax = LivenessAnalysisHelper.GetFirstRelevantOperationInBlock(block);
            var region = LivenessAnalysisHelper.GetRegionOf(operationSyntax);
            //we extract for loop declaration here then later in the CFG they can reuse it as dependency
            var (_, isDeclaratorBlock) = GetForLoopVariableDeclarations(block, region);
            var successorLiveness = AnalyzePartial(block.FallThroughSuccessor!.Destination!);
            var analysis = AnalyzeDataFlowOfRegion(region);
            analysis.Should().NotBeNull();
            analysis.Succeeded.Should().BeTrue();
            return ComputeLiveness(block, region, successorLiveness, LivenessInfo.Default, analysis,
                !isDeclaratorBlock);
        }

        if (LivenessAnalysisHelper.IsFallthroughOnlyBlock(block))
        {
            var fallthroughLiveness = AnalyzePartial(block.FallThroughSuccessor!.Destination!);
            _basicBlocksLivenessInfo.Add(block, fallthroughLiveness);
            return fallthroughLiveness;
        }

        throw new UnreachableException();
    }

    private LivenessInfo AnalyzeComplete(BasicBlock block)
    {
        if (_worklist.Contains(block) && _basicBlocksLivenessInfo.TryGetValue(block, out var livenessInfo))
        {
            return livenessInfo;
        }

        _worklist.Add(block);
        if (LivenessAnalysisHelper.IsBinaryFlowOnlyBlock(block) ||
            LivenessAnalysisHelper.IsBranchingExecutableBlock(block))
        {
            var blockLiveness = _basicBlocksLivenessInfo[block];
            var conditional = block.ConditionalSuccessor?.Destination!;
            var fallthrough = block.FallThroughSuccessor!.Destination!;
            var conditionalLiveness =
                fallthrough.Kind is BasicBlockKind.Exit ? LivenessInfo.Default : AnalyzeComplete(conditional);

            var partialLiveness = blockLiveness with
            {
                LiveIn = blockLiveness.LiveIn
                    .Union(conditionalLiveness.LiveIn)
                    .Except(blockLiveness.RegionLocals),

                LiveOut = blockLiveness.LiveOut
                    .Union(conditionalLiveness.LiveOut)
                    .Except(blockLiveness.RegionLocals)
            };
            _basicBlocksLivenessInfo[block] = partialLiveness;

            var fallthroughLiveness =
                fallthrough.Kind is BasicBlockKind.Exit ? LivenessInfo.Default : AnalyzeComplete(fallthrough);
            var completeLiveness = partialLiveness with
            {
                LiveIn = partialLiveness.LiveIn
                    .Union(fallthroughLiveness.LiveIn)
                    .Union(conditionalLiveness.LiveIn)
                    .Except(partialLiveness.RegionLocals),

                LiveOut = partialLiveness.LiveOut
                    .Union(fallthroughLiveness.LiveOut)
                    .Union(conditionalLiveness.LiveOut)
                    .Except(partialLiveness.RegionLocals)
            };
            _basicBlocksLivenessInfo[block] = completeLiveness;
            return completeLiveness;
        }

        if (LivenessAnalysisHelper.IsSerialExecutableBlock(block) ||
            LivenessAnalysisHelper.IsFallthroughOnlyBlock(block))
        {
            var fallthrough = AnalyzeComplete(block.FallThroughSuccessor!.Destination!);
            var blockLiveness = _basicBlocksLivenessInfo[block];
            var completeLiveness = blockLiveness with
            {
                LiveIn = blockLiveness.LiveIn.Union(fallthrough.LiveIn).Except(blockLiveness.RegionLocals),
                LiveOut = blockLiveness.LiveOut.Union(fallthrough.LiveOut).Except(blockLiveness.RegionLocals)
            };
            _basicBlocksLivenessInfo[block] = completeLiveness;
            return completeLiveness;
        }

        throw new UnreachableException();
    }

    private static IReadOnlyList<CSharpSyntaxNode> GetPossibleRegionOfBranchingOnlyBlock(BasicBlock block)
    {
        //branch basic blocks got two successors to flow-in to 
        //first successor
        var branchExpr = (CSharpSyntaxNode)block.BranchValue!.Syntax;
        var firstScope = LivenessAnalysisHelper.GetFirstRelevantOperationFromNode(branchExpr);

        //second successor
        return firstScope switch
        {
            IfStatementSyntax { Else: not null } ifStatement => [ifStatement],
            _ => LivenessAnalysisHelper.GetRegionOf(firstScope)
        };
    }

    private DataFlowAnalysis AnalyzeDataFlowOfRegion(IReadOnlyList<CSharpSyntaxNode> region)
    {
        return region.Count is 1
            ? _semantics.AnalyzeDataFlow(region[0])
            : _semantics.AnalyzeDataFlow(region[0], region[^1]);
    }

    private LivenessInfo ComputeLiveness(BasicBlock block,
        IReadOnlyList<CSharpSyntaxNode> region,
        in LivenessInfo fallthroughLiveness,
        in LivenessInfo conditionalLiveness,
        DataFlowAnalysis analysis,
        bool includeDependencies = true)
    {
        var blockLiveOut = LivenessInfo.IsDefault(conditionalLiveness)
            ? fallthroughLiveness.LiveIn
            : fallthroughLiveness.LiveIn.Union(conditionalLiveness.LiveIn);

        var (mayDependencyLocals, _) = GetForLoopVariableDeclarations(block, region);
        var blockLiveIn = analysis.DataFlowsIn
            .Where(symbol => symbol.Name is not "this")
            .ToImmutableHashSet(SymbolEqualityComparer.Default)
            .Union(analysis.Captured)
            .Union(blockLiveOut)
            .Except(analysis.VariablesDeclared);

        LivenessInfo currentBlockLiveness;
        if (includeDependencies)
        {
            if (mayDependencyLocals.Count > 0)
            {
                blockLiveIn = blockLiveIn.Union(mayDependencyLocals);
                currentBlockLiveness = new LivenessInfo
                {
                    BasicBlock = block,
                    LiveOut = blockLiveOut,
                    LiveIn = blockLiveIn,
                    RegionLocals = analysis.VariablesDeclared
                        .ToImmutableHashSet(SymbolEqualityComparer.Default)
                        .Except(mayDependencyLocals)
                };
                _basicBlocksLivenessInfo.Add(block, currentBlockLiveness);
                return currentBlockLiveness;
            }
        }

        currentBlockLiveness = new LivenessInfo
        {
            BasicBlock = block,
            LiveOut = blockLiveOut,
            LiveIn = blockLiveIn,
            RegionLocals = analysis.VariablesDeclared.ToImmutableHashSet(SymbolEqualityComparer.Default)
        };
        _basicBlocksLivenessInfo.Add(block, currentBlockLiveness);
        return currentBlockLiveness;
    }

    private (ImmutableHashSet<ISymbol> dependencies, bool isDeclBlock) GetForLoopVariableDeclarations(BasicBlock block,
        IReadOnlyList<CSharpSyntaxNode> region)
    {
        if (region.Count is not 1 || region[0] is not ForStatementSyntax { Declaration: not null } forLoop)
        {
            return (dependencies: ImmutableHashSet<ISymbol>.Empty, isDeclBlock: false);
        }

        if (_dependencyLocals.TryGetValue(forLoop, out var dependencies))
        {
            return (dependencies, false);
        }

        var forLoopDecl = forLoop.Declaration!;
        dependencies = block.Operations
            .Where(op => op is ISimpleAssignmentOperation { Syntax: VariableDeclaratorSyntax declarator }
                         && ReferenceEquals((VariableDeclarationSyntax?)declarator.Parent, forLoopDecl))
            .Select(op => (((ISimpleAssignmentOperation)op).Target as ILocalReferenceOperation)?.Local)
            .Where(localRef => localRef is not null)
            .Cast<ISymbol>()
            .ToImmutableHashSet(SymbolEqualityComparer.Default);

        _dependencyLocals.Add(forLoop, dependencies);
        return (dependencies, true);
    }
}

file static class LivenessAnalysisHelper
{
    public static bool IsFallthroughOnlyBlock(BasicBlock block)
    {
        return block.Operations.Length is 0 && block.BranchValue is null;
    }

    public static bool IsBinaryFlowOnlyBlock(BasicBlock block)
    {
        return block.Operations.Length is 0 && block.BranchValue is not null;
    }

    public static bool IsSerialExecutableBlock(BasicBlock block)
    {
        return block.Operations.Length > 0 && block.BranchValue is null;
    }

    public static bool IsBranchingExecutableBlock(BasicBlock block)
    {
        return block.Operations.Length > 0 && block.BranchValue is not null;
    }

    public static CSharpSyntaxNode GetFirstRelevantOperationInBlock(BasicBlock block)
    {
        var operation = (CSharpSyntaxNode)block.Operations[0].Syntax;
        return GetFirstRelevantOperationFromNode(operation);
    }

    public static CSharpSyntaxNode GetFirstRelevantOperationFromNode(CSharpSyntaxNode node)
    {
        var opStatement =
            node.FirstAncestorOrSelf<SimpleLambdaExpressionSyntax>() ??
            node.FirstAncestorOrSelf<ParenthesizedLambdaExpressionSyntax>() ??
            node.FirstAncestorOrSelf<StatementSyntax>() as CSharpSyntaxNode;

        return opStatement!;
    }

    public static IReadOnlyList<CSharpSyntaxNode> GetRegionOf(CSharpSyntaxNode node)
    {
        StatementSyntax statement;
        switch (node)
        {
            case ForStatementSyntax:
                return [node];
            case SimpleLambdaExpressionSyntax simpleLambda:
                return [simpleLambda.Block ?? (CSharpSyntaxNode)simpleLambda.ExpressionBody!];
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                return [parenthesizedLambda.Block ?? (CSharpSyntaxNode)parenthesizedLambda.ExpressionBody!];
            case StatementSyntax stmt:
                statement = stmt;
                break;
            default:
                statement = node.FirstAncestorOrSelf<StatementSyntax>()!;
                break;
        }

        var scope = statement.FirstAncestorOrSelf<StatementSyntax>(s => !ReferenceEquals(s, statement));
        if (scope is null)
        {
            return [];
        }

        if (scope is not BlockSyntax scopeBlock)
        {
            return [scope];
        }

        var statements = scopeBlock.Statements;
        var baseStatementLoc = statements.IndexOf(statement);
        baseStatementLoc.Should().NotBe(-1, "base statement in analysis region should be part of the " +
                                            "region or one of it's predecessor regions");

        var region = new StatementSyntax[statements.Count - baseStatementLoc];
        region[0] = statement;
        for (var i = 1; i < region.Length; ++i)
        {
            region[i] = statements[i + baseStatementLoc];
        }

        return region;
    }
}