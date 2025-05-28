using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.Z3;

namespace Dante.Generator;

internal sealed class BasicBlockAnnotator
{
    private readonly uint _graphId;
    private readonly uint _positionalNotation;
    private uint _basicBlockId;

    public BasicBlockAnnotator(ControlFlowGraph cfg, uint graphId)
    {
        _graphId = graphId;
        _positionalNotation = cfg.Blocks.Length switch
        {
            < 1000 => 2,
            >= 1000 => 3
        };
    }

    public string GenerateBlockName()
    {
        return $"BB{_graphId}{0.ToString($"D{_positionalNotation}")}{_basicBlockId++}";
    }
}

internal sealed class BasicBlockInfo
{
    /// <summary>
    ///     if basic block was an exit basic block, then this block is generated as two functions,
    ///     one function being the immediate dominator block of the returning or throwing block and the final/exit function
    ///     which is the one returning or throwing expression
    /// </summary>
    public FuncDecl? GeneratedBasicBlockImmediateDominator { get; init; }

    /// <summary>
    ///     Z3 function that was generated out of the corresponding basic block
    /// </summary>
    public required FuncDecl GeneratedBasicBlock { get; init; }

    /// <summary>
    ///     parameters and locals (excluding those declared in basic block corresponding lexical scope) that
    ///     are used in the basic block and/or out of it. so if a local was declared in current scope and
    ///     was used in the same or nested scope then it will not be part of the list
    /// </summary>
    public required IReadOnlyList<ISymbol> DependentOnSymbols { get; init; }

    /// <summary>
    ///     parameters or locals that are alive or used out and in the corresponding basic block also include
    ///     locals declared in basic block (corresponding lexical scope)
    /// </summary>
    public required IReadOnlyList<ISymbol> BasicBlockLexicalSymbols { get; init; }

    /// <summary>
    ///     respective evaluation table of basic block
    /// </summary>
    public SymbolEvaluationTable BasicBlockEvaluationTable { get; set; } = null!;
}