using Dante.Asserts;
using Dante.Extensions;
using Dante.Intrinsics;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Z3;

namespace Dante.Generator;

internal partial class ExpressionGenerator
{
    public override ArrayExpr VisitArrayCreation(IArrayCreationOperation operation, GenerationContext context)
    {
        var arrayTypeOpt = operation.Type as IArrayTypeSymbol;
        arrayTypeOpt.Should().NotBeNull("array creation operation return type must be an array type");
        var solverContext = context.SolverContext;
        var arrayType = arrayTypeOpt!;
        var arraySort = (ArraySort)arrayType.AsSort();
        var domain = arraySort.Domain;
        ArrayExpr constArray;
        if (arrayType.Rank is 1)
            constArray = solverContext.MkConstArray(arraySort.Domain, domain.MkDefault());
        else
            constArray = (ArrayExpr)solverContext.MkConst($"const{arrayType.ElementType.ToDisplayString()}", arraySort);

        if (operation.Initializer is not null)
        {
            var indexer = 0;
            foreach (var elementValue in operation.Initializer.ElementValues)
            {
                var genElementVal = elementValue.Accept(this, context);
                GenerationAsserts.RequireValidExpression(genElementVal, elementValue);
                constArray = solverContext.MkOptimizedStore(constArray, indexer++, (Expr)genElementVal!);
            }
        }

        return constArray;
    }

    public override Expr VisitArrayElementReference(IArrayElementReferenceOperation operation,
        GenerationContext context)
    {
        var solverContext = context.SolverContext;
        var (array, indices) = GenerateArrayElementReferenceTerms(operation, context);
        return solverContext.MkSelect(array, indices);
    }

    private (ArrayExpr array, Expr[] indices) GenerateArrayElementReferenceTerms(
        IArrayElementReferenceOperation operation,
        GenerationContext context)
    {
        var rank = operation.Indices.Length;
        var referencedArray = operation.ArrayReference.Accept(this, context);
        if (referencedArray is DatatypeExpr maybeArray && UnderlyingType.IsMaybe(maybeArray))
            referencedArray = MaybeIntrinsics.Value(maybeArray);
        GenerationAsserts.RequireValidExpression<ArrayExpr>(referencedArray, operation.ArrayReference);
        var indicesExpression = new Expr[rank];
        for (var i = 0; i < rank; ++i) indicesExpression[i] = GenerateIndexExpression(operation, context, i);

        return ((ArrayExpr)referencedArray!, indicesExpression);
    }

    private ArrayExpr GenerateStore(IArrayElementReferenceOperation operation, GenerationContext context, Expr operand)
    {
        var solverContext = context.SolverContext;
        var (array, indices) = GenerateArrayElementReferenceTerms(operation, context);
        return solverContext.MkStore(array, indices, operand);
    }

    private Expr GenerateIndexExpression(
        IArrayElementReferenceOperation operation,
        GenerationContext context,
        int dimension = 0)
    {
        var indexOp = operation.Indices[dimension];
        var indexExpression = indexOp.Accept(this, context);
        GenerationAsserts.RequireValidExpression(indexExpression, operation);
        return (Expr)indexExpression!;
    }
}