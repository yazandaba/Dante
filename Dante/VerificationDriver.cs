using System.Diagnostics;
using Dante.Generator;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Microsoft.Z3;

namespace Dante;

public readonly record struct VerificationResult()
{
    public bool Satisfiable { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public bool Succeed { get; init; }
    public string SmtText { get; init; } = string.Empty;

    public void Deconstruct(out bool succeed, out bool satisfiable, out string message, out string model,
        out string smtText)
    {
        succeed = Succeed;
        satisfiable = Satisfiable;
        message = Message;
        model = Model;
        smtText = SmtText;
    }
}

public class VerificationDriver
{
    private readonly GenerationContext _generationContext;
    private readonly ILogger<VerificationDriver> _logger;

    public VerificationDriver(CSharpCompilation compilation, uint timeout, uint recursionDepth)
    {
        var solverContext = new Context();
        solverContext.UpdateParamValue("proof", "true");
        solverContext.UpdateParamValue("timeout", timeout.ToString());
        _generationContext = GenerationContext.CreateOrGet(solverContext, compilation, recursionDepth);
        _logger = LoggerFactory.Create<VerificationDriver>();
    }

    private FuncDecl? GenerateMethod(string entryTypeFullName, string methodName)
    {
        var compilation = _generationContext.Compilation;
        var typeSymbol = compilation.GetTypeByMetadataName(entryTypeFullName);
        typeSymbol.Should().NotBeNull($"class with name '{methodName}' was not found, make sure" +
                                      $"to pass the right and full qualified name of type");

        var method = GetTargetedMethod(typeSymbol!, methodName);
        if (method is null) return default;

        var funcGenerator = FunctionGenerator.Create(method);
        return funcGenerator.Generate();
    }

    public VerificationResult Verify(
        string entryTypeFullName,
        string originalMethodName,
        string transformedMethodName)
    {
        var solver = _generationContext.SolverContext.MkSolver();
        var originalFunc = GenerateMethod(entryTypeFullName, originalMethodName);
        var transformedFunc = GenerateMethod(entryTypeFullName, transformedMethodName);
        if (originalFunc is null || transformedFunc is null) return new VerificationResult { Succeed = false };

        var satExpression =
            FunctionGenerator.GenerateSatisfiabilityExpression(originalFunc, transformedFunc, _generationContext);

        solver.Assert(satExpression);
        var status = solver.Check();
        var smtText = string.Empty;
        try
        {
            smtText = solver.ToString();
        }
        catch (AccessViolationException)
        {
            _logger.LogError("Z3 internal error (smt text cannot be retrieved)");
        }

        return status switch
        {
            Status.UNKNOWN => new VerificationResult
            {
                Succeed = true,
                Satisfiable = false,
                Message =
                    "undecidable problem, cannot determine if original function always imply transformed function.",
                Model = string.Empty,
                SmtText = smtText
            },

            Status.SATISFIABLE => new VerificationResult
            {
                Succeed = true,
                Satisfiable = false,
                Message = "original function does not always imply transformed function, there was " +
                          "at least one set of values that broke the bi-proposition.",
                Model = solver.Model.ToString(),
                SmtText = smtText
            },

            Status.UNSATISFIABLE => new VerificationResult
            {
                Succeed = true,
                Satisfiable = true,
                Message = "original function always imply transformed function, there was no of set values " +
                          "that could be assigned to the abstract symbols where it breaks the bi-proposition.",
                Model = string.Empty,
                SmtText = smtText
            },

            _ => throw new UnreachableException()
        };
    }

    private MethodDeclarationSyntax? GetTargetedMethod(ITypeSymbol containingType, string methodName)
    {
        try
        {
            var methodSymbol = containingType!
                .GetMembers()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method => method.Name == methodName);
            methodSymbol.Should().NotBeNull($"method with name '{methodSymbol!.Name}' was not found");

            var methodDeclRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            methodDeclRef.Should().NotBeNull($"method '{methodSymbol.Name}' does not have any syntactic declaration," +
                                             $"Dante verifier only works with synthesized binary symbols that has syntax declaration, verification " +
                                             $"against synthesized binary only symbols is not supported ");

            var methodDecl = (MethodDeclarationSyntax)methodDeclRef!.GetSyntax();
            return methodDecl;
        }
        catch (NullReferenceException)
        {
            _logger.LogError("method with name '{methodName}' was not found", methodName);
            return default;
        }
    }
}