using Dante.Extensions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Z3;
using Enumerable = Dante.Intrinsics.Enumerable;

namespace Dante.Generator;

internal partial class ExpressionGenerator
{
    public override FuncDecl VisitDelegateCreation(IDelegateCreationOperation operation, GenerationContext argument)
    {
        var @delegate = operation.Target.Accept(this, argument);
        return (FuncDecl)@delegate!;
    }

    public override FuncDecl VisitFlowAnonymousFunction(IFlowAnonymousFunctionOperation operation,
        GenerationContext argument)
    {
        argument.Should().BeOfType<FunctionGenerationContext>("lambda code generation requires " +
                                                              "function generation context ");

        var funcGenContext = (FunctionGenerationContext)argument;
        var functionGenerator = FunctionGenerator.Create(operation, funcGenContext.TargetControlFlowGraph);
        var func = functionGenerator.Generate();
        return func;
    }

    private Expr? GenerateLinq(IInvocationOperation operation, GenerationContext context, SemanticModel semantics)
    {
        operation.IsEnumerableCall(semantics).Should()
            .BeTrue("GenerateLinq must be called against verified linq expression");
        var linqBuilder = new Enumerable.LinqQueryBuilder();
        var linq = GenerateLinqCore(operation, context, semantics, linqBuilder) as DatatypeExpr;
        if (linq is null) return linq;

        return linqBuilder.Build();
    }

    private Expr? GenerateLinqCore(IInvocationOperation operation,
        GenerationContext context,
        SemanticModel semantics,
        Enumerable.LinqQueryBuilder queryBuilder)
    {
        //Enumerable queries/methods are extension methods
        var queryInstance = operation.Arguments[0].Value;
        var genInstance = queryInstance switch
        {
            IInvocationOperation invocation when invocation.IsEnumerableCall(semantics) =>
                GenerateLinqCore(invocation, context, semantics, queryBuilder),
            _ => queryBuilder.Instance(Enumerable.CreateOrGet((DatatypeExpr)queryInstance.Accept(this, context)!))
        } as DatatypeExpr;

        genInstance.Should().NotBeNull();
        switch (operation.TargetMethod.Name)
        {
            case "Select":
            {
                var lambda = operation.Arguments[1].Value;
                var generatedLambda = lambda.Accept(this, context) as FuncDecl;
                generatedLambda.Should().NotBeNull();
                var newEnumerable = Enumerable.CreateOrGet(genInstance!);
                return queryBuilder.Select(newEnumerable, generatedLambda!);
            }
            case "Where":
            {
                var lambda = operation.Arguments[1].Value;
                var generatedLambda = lambda.Accept(this, context) as FuncDecl;
                generatedLambda.Should().NotBeNull();
                var newEnumerable = Enumerable.CreateOrGet(genInstance!);
                return queryBuilder.Where(newEnumerable, generatedLambda!);
            }
            case "Take":
            {
                var countOp = operation.Arguments[1].Value;
                var count = countOp.Accept(this, context) as IntExpr;
                count.Should().NotBeNull();
                var newEnumerable = Enumerable.CreateOrGet(genInstance!);
                return queryBuilder.Take(newEnumerable, count!);
            }
            case "ToArray":
            {
                var newEnumerable = Enumerable.CreateOrGet(genInstance!);
                return queryBuilder.ToArray(newEnumerable);
            }
        }

        return VisitInvocation(operation, context);
    }
}