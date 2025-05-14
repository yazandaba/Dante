using Dante.Extensions;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Dante.Generator;

internal partial class FunctionGenerator
{
    private static class FunctionGeneratorFactory
    {
        private static uint _graphId;

        public static FunctionGenerator CreateGenerator(MethodDeclarationSyntax targetedMethod)
        {
            var generationContext = GenerationContext.GetInstance();
            var semantics = generationContext.Compilation.GetSemanticModel(targetedMethod.SyntaxTree);
            var methodSymbol = semantics.GetDeclaredSymbol(targetedMethod);
            methodSymbol.Should().NotBeNull($"symbol of method '{targetedMethod.GetSyntaxNodeText()}' could not" +
                                            $"be found in compilation");
            var methodBodyOperation = semantics.GetOperation(targetedMethod);
            methodBodyOperation
                .Should()
                .NotBeNull($"could not retrieve operations of method '{targetedMethod}'")
                .And
                .BeAssignableTo<IMethodBodyOperation>($"could not retrieve operations of method '{targetedMethod}'");

            var cfg = ControlFlowGraph.Create((IMethodBodyOperation)methodBodyOperation!);
            var callable = new CallableDeclarationAdapter(targetedMethod, methodSymbol!);
            return new FunctionGenerator(callable, cfg, _graphId++);
        }

        public static FunctionGenerator CreateGenerator(IFlowAnonymousFunctionOperation targetedAnonymousFunction,
            ControlFlowGraph ownerControlFlowGraph)

        {
            var syntax = (AnonymousFunctionExpressionSyntax)targetedAnonymousFunction.Syntax;
            var anonymousFuncCfg =
                ownerControlFlowGraph.GetAnonymousFunctionControlFlowGraph(targetedAnonymousFunction);
            var callable = new CallableDeclarationAdapter(syntax, targetedAnonymousFunction.Symbol);
            return new FunctionGenerator(callable, anonymousFuncCfg, _graphId++);
        }
    }


    public static FunctionGenerator Create(MethodDeclarationSyntax targetedMethod)
    {
        return FunctionGeneratorFactory.CreateGenerator(targetedMethod);
    }


    public static FunctionGenerator Create(IFlowAnonymousFunctionOperation targetedAnonymousFunction,
        ControlFlowGraph ownerControlFlowGraph)
    {
        return FunctionGeneratorFactory.CreateGenerator(targetedAnonymousFunction, ownerControlFlowGraph);
    }
}