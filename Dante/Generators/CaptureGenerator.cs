using Dante.Asserts;
using Dante.Intrinsics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Z3;

namespace Dante.Generators;

/// <summary>
///     this partition implements code generation of C# lowered nullable operations like null coalescing or null
///     conditional access and other control flow sensetive operaionts,
///     such operations are lowered in the control flow graph to be represented as null checking conditional basic block
///     with operations of type <seealso cref="IFlowCaptureReferenceOperation" /> or
///     <seealso cref="IFlowCaptureOperation" />
/// </summary>
internal partial class ExpressionGenerator
{
    public override Expr VisitIsNull(IIsNullOperation operation, GenerationContext argument)
    {
        return operation.Operand.Accept(this, argument)! is not DatatypeExpr expr
            ? argument.SolverContext.MkFalse() //expression cannot be a maybe expression, so it cannot be null 
            : MaybeIntrinsics.IsNull(expr);
    }

    public override Expr VisitFlowCapture(IFlowCaptureOperation operation, GenerationContext context)
    {
        var generatedValue = operation.Value.Accept(this, context) as Expr;
        GenerationAsserts.RequireValidExpression(generatedValue, operation.Value);
        _owningControlFlowGraphFlowCaptureTable.Bind(operation, generatedValue!);
        //if flow capture operation value is a flow capture reference, then we should start building the graph
        //to reach the captured intermediate value 
        _owningControlFlowGraphFlowCaptureTable.TryTrack(operation);
        return generatedValue!;
    }

    public override Expr VisitFlowCaptureReference(IFlowCaptureReferenceOperation operation, GenerationContext context)
    {
        //TODO we can return concrete value instead of Maybe when operation type is not nullable, but be careful such change may break other nullable related code generation
        return _owningControlFlowGraphFlowCaptureTable.Fetch(operation);
    }

    public override MaybeExpr VisitDefaultValue(IDefaultValueOperation operation, GenerationContext argument)
    {
        var maybe = MaybeIntrinsics.CreateOrGet(operation.Type!);
        return MaybeIntrinsics.Default(maybe);
    }
}