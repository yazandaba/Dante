using Dante.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Dante;

internal readonly record struct DataflowAnalysisResult
{
    /// <summary>
    ///     parameters or locals that live out (aka used) by successor nodes and/or got modified/written in scope
    /// </summary>
    public IReadOnlyList<ISymbol> LiveOutSymbols { get; init; }

    /// <summary>
    ///     parameters or locals that are alive (aka used) by analyzed scope and got modified/written in outer scope
    /// </summary>
    public IReadOnlyList<ISymbol> LiveInSymbols { get; init; }

    /// <summary>
    ///     local variables that were declared inside the analysis scope (excluding parameters)
    /// </summary>
    public IReadOnlyList<ISymbol> ScopeLocalSymbols { get; init; }
}

internal class DataflowAnalyzer
{
    private readonly DataFlowAnalysis _opsDataFlowAnalysis;
    private readonly bool _analyzingTerminatingStatement;
    private readonly bool _analyzingAnonymous;
    private DataflowAnalysisResult _dataflowAnalysisResult;

    public static DataflowAnalyzer? Create(BasicBlock targetedBasicBlock, SemanticModel semantics,
        bool blockBased = true)
    {
        if (targetedBasicBlock.Operations.Length is 0)
        {
            if (targetedBasicBlock.BranchValue is not null)
            {
                //analyze return operation sub control flow graph
                var analyzer = CreateAnalyzerTargetingReturn(targetedBasicBlock, semantics);
                //if sub CFG starting from 'targetedBasicBlock' is not a return block, then we should try to analyze 
                //the possible expression 
                if (analyzer is null && !targetedBasicBlock.IsReturnBasicBlock())
                    analyzer = CreateAnalyzerTargetingBranchesOrExpression(targetedBasicBlock, semantics);

                return analyzer;
            }

            return default;
        }

        DataFlowAnalysis? dataflowAnalysis;
        if (blockBased)
        {
            var operation = targetedBasicBlock.Operations[0];
            var isDeclarator = operation.Syntax is VariableDeclaratorSyntax;
            var opStatementScope = operation.Syntax.FirstAncestorOrSelf<StatementSyntax>();

            var analysisScope = opStatementScope switch
            {
                IfStatementSyntax ifStatement => ifStatement.Statement,
                ForStatementSyntax forStatement when isDeclarator => forStatement,
                ForStatementSyntax forStatement => forStatement.Statement,
                WhileStatementSyntax whileStatement => whileStatement.Statement,
                DoStatementSyntax doStatement => doStatement.Statement,
                BlockSyntax block => block,
                _ => opStatementScope?.FirstAncestorOrSelf<BlockSyntax>()
            };
            if (analysisScope is null) return default;
            dataflowAnalysis = semantics.AnalyzeDataFlow(analysisScope);
        }
        else
        {
            var analysisScopeBeg = targetedBasicBlock.Operations[0].Syntax.FirstAncestorOrSelf<StatementSyntax>()!;
            var analysisScopeEnd = targetedBasicBlock.Operations[^1].Syntax.FirstAncestorOrSelf<StatementSyntax>()!;
            dataflowAnalysis = semantics.AnalyzeDataFlow(analysisScopeBeg, analysisScopeEnd);
        }

        if (dataflowAnalysis?.Succeeded is false or null) return default;

        return new DataflowAnalyzer(dataflowAnalysis, false);
    }

    public static DataflowAnalyzer? CreateAnalyzerTargetingReturn(BasicBlock targetedBasicBlock,
        SemanticModel semantics)
    {
        var analysisScope =
            targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<SimpleLambdaExpressionSyntax>() ??
            targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<ParenthesizedLambdaExpressionSyntax>() ??
            targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<ReturnStatementSyntax>() as CSharpSyntaxNode;

        if (analysisScope is null) return default;

        var isAnonymous = analysisScope is AnonymousFunctionExpressionSyntax;
        var dataflowAnalysis = semantics.AnalyzeDataFlow(analysisScope);
        return !dataflowAnalysis.Succeeded ? default : new DataflowAnalyzer(dataflowAnalysis, true, isAnonymous);
    }

    private static DataflowAnalyzer? CreateAnalyzerTargetingBranchesOrExpression(BasicBlock targetedBasicBlock,
        SemanticModel semantics)
    {
        /*SyntaxNode? analysisScope =
            (CSharpSyntaxNode?)targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<ForStatementSyntax>()?.Statement  ??
            (CSharpSyntaxNode?)targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<WhileStatementSyntax>()?.Statement ??
            (CSharpSyntaxNode?)targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<DoStatementSyntax>()?.Statement ??
            targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<ExpressionSyntax>();*/

        var analysisScope =
            targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<StatementSyntax>() ??
            targetedBasicBlock.BranchValue!.Syntax.FirstAncestorOrSelf<ExpressionSyntax>() as CSharpSyntaxNode;

        if (analysisScope is null) return default;

        analysisScope = analysisScope switch
        {
            IfStatementSyntax ifStatement => ifStatement.Statement,
            ForStatementSyntax forStatement => forStatement.Statement,
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            ExpressionSyntax expression => expression,
            _ => analysisScope
        };
        var dataflowAnalysis = semantics.AnalyzeDataFlow(analysisScope);
        return !dataflowAnalysis.Succeeded ? default : new DataflowAnalyzer(dataflowAnalysis, false);
    }


    private DataflowAnalyzer(DataFlowAnalysis opsDataFlowAnalysis,
        bool analyzingTerminatingStatement,
        bool analyzingAnonymous = false)
    {
        _opsDataFlowAnalysis = opsDataFlowAnalysis;
        _analyzingTerminatingStatement = analyzingTerminatingStatement;
        _analyzingAnonymous = analyzingAnonymous;
    }

    public DataflowAnalyzer AnalyzeLiveOutSymbols()
    {
        if (_analyzingTerminatingStatement)
            _dataflowAnalysisResult = _dataflowAnalysisResult with
            {
                LiveOutSymbols = _opsDataFlowAnalysis.DataFlowsOut
            };

        else
            _dataflowAnalysisResult = _dataflowAnalysisResult with
            {
                LiveOutSymbols =
                _opsDataFlowAnalysis.DataFlowsOut
                    .Union(_opsDataFlowAnalysis.ReadOutside, SymbolEqualityComparer.Default).ToList()
            };
        return this;
    }

    public DataflowAnalyzer AnalyzeLiveInSymbols()
    {
        if (_analyzingAnonymous)
            _dataflowAnalysisResult = _dataflowAnalysisResult with
            {
                LiveInSymbols =
                _opsDataFlowAnalysis.DataFlowsIn
                    .Intersect(_opsDataFlowAnalysis.Captured, SymbolEqualityComparer.Default).ToList()
            };
        else
            _dataflowAnalysisResult = _dataflowAnalysisResult with { LiveInSymbols = _opsDataFlowAnalysis.DataFlowsIn };
        return this;
    }

    public DataflowAnalyzer AnalyzeScopeLocalSymbols()
    {
        _dataflowAnalysisResult = _dataflowAnalysisResult with
        {
            ScopeLocalSymbols = _opsDataFlowAnalysis.VariablesDeclared
        };
        return this;
    }

    public DataflowAnalysisResult AnalysisResult()
    {
        return _dataflowAnalysisResult;
    }
}