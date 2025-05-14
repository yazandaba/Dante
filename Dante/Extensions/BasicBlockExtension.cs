using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Dante.Extensions;

internal static class BasicBlockExtension
{
    public static bool IsReturnBasicBlock(this BasicBlock basicBlock)
    {
        var fallthroughEdge = basicBlock.FallThroughSuccessor;
        return fallthroughEdge?.Semantics is ControlFlowBranchSemantics.Return;
    }

    public static bool IsConditionalBasicBlock(this BasicBlock basicBlock)
    {
        return basicBlock.BranchValue is not null &&
               basicBlock.FallThroughSuccessor is not null &&
               basicBlock.ConditionalSuccessor is not null &&
               basicBlock.ConditionKind is not ControlFlowConditionKind.None;
    }

    public static bool IsBooleanShortCircuitBlock(this BasicBlock basicBlock, CallableDeclarationAdapter callable)
    {
        return
            callable.ReturnType.SpecialType is SpecialType.System_Boolean &&
            basicBlock.IsReturnBasicBlock() &&
            basicBlock.Predecessors.Length is 2 &&
            basicBlock.BranchValue is IFlowCaptureReferenceOperation &&
            basicBlock.BranchValue.Syntax is BinaryExpressionSyntax binExpr &&
            (binExpr.IsKind(SyntaxKind.LogicalAndExpression) || binExpr.IsKind(SyntaxKind.LogicalOrExpression));
    }
}