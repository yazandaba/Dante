using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.Z3;

namespace Dante.Generators;

internal record GenerationContext
{
    private IntExpr? _recursionDepth;
    private readonly Random _randomDepthGenerator = new();

    private static uint _recursionDepthCore;
    private static GenerationContext? _generationContext;

    protected GenerationContext()
    {
    }

    public static GenerationContext CreateOrGet(Context solverContext,
        CSharpCompilation compilation,
        uint recursionDepth,
        bool randomDepth)
    {
        _recursionDepthCore = recursionDepth;
        _generationContext = new GenerationContext
        {
            SolverContext = solverContext,
            SortPool = new SortPool(solverContext),
            Compilation = compilation,
            RecursionDepth = solverContext.MkInt(recursionDepth),
            RandomDepth = randomDepth
        };

        return _generationContext;
    }

    public static GenerationContext GetInstance()
    {
        return _generationContext ?? throw new InvalidOperationException(
            $"getting instance of '{nameof(GenerationContext)}'" +
            $"without creating it first. did you call CreateOrGet ?");
    }

    public required Context SolverContext { get; init; }
    public required SortPool SortPool { get; init; }

    /// <summary>
    ///     the compilation unit of targeted C# file
    /// </summary>
    public required CSharpCompilation Compilation { get; init; }

    /// <summary>
    ///     class or struct members evaluation table
    /// </summary>
    public SymbolEvaluationTable GlobalEvaluationTable { get; init; } = new();

    /// <summary>
    ///     the virtual depth of recursion when abstract symbols are being used in loops
    /// </summary>
    /// <remarks>
    ///     this will only get used in cases where the user didn't define that in code (e.g., user can explicitly set)
    ///     the number of iterations in a for loop and the compiler will respect that, but if something like 'array.Length'
    ///     was used, then that expression will get reduced to this
    /// </remarks>
    public IntExpr RecursionDepth
    {
        get
        {
            if (_recursionDepth is null)
            {
                return _recursionDepth = SolverContext.MkInt(1000);
            }

            if (RandomDepth)
            {
                return _recursionDepth = SolverContext.MkInt(_randomDepthGenerator.Next() % _recursionDepthCore);
            }

            return _recursionDepth;
        }
        init => _recursionDepth = value;
    }

    /// <summary>
    ///     if set to true, each time <see cref="RecursionDepth" /> is called a random depth will get generated
    /// </summary>
    /// <remarks>
    ///     the maximum random depth must be less or equal to <see cref="RecursionDepth" />
    /// </remarks>
    public bool RandomDepth { get; init; }
}

internal record FunctionGenerationContext : GenerationContext
{
    public required ControlFlowGraph TargetControlFlowGraph { get; init; }

    public static FunctionGenerationContext Create(ControlFlowGraph targetControlFlowGraph)
    {
        var genContext = GetInstance();
        return new FunctionGenerationContext
        {
            GlobalEvaluationTable = genContext.GlobalEvaluationTable,
            SolverContext = genContext.SolverContext,
            SortPool = genContext.SortPool,
            Compilation = genContext.Compilation,
            TargetControlFlowGraph = targetControlFlowGraph,
            RecursionDepth = genContext.RecursionDepth,
            RandomDepth = genContext.RandomDepth
        };
    }
}