﻿using Dante.Utilities;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using CommandLineParser = Ookii.CommandLine.CommandLineParser;

namespace Dante;

public static class Program
{
    private static readonly SExpressionHighlighter Highlighter = new();

    public static int Main(string[] args)
    {
        try
        {
            var arguments = CommandLineParser.Parse<DanteCommandLine>(args);
            if (arguments is null) return -1;

            arguments.Project = Path.GetFullPath(arguments.Project);
            if (!File.Exists(arguments.Project))
            {
                Console.Error.WriteLine($"specified project file {arguments.Project} does not exist");
                return -1;
            }

            MSBuildLocator.RegisterDefaults();
            var ws = MSBuildWorkspace.Create();
            var project = ws.OpenProjectAsync(Path.GetFullPath(arguments.Project)).Result;
            var compilation = (CSharpCompilation)project.GetCompilationAsync().Result!;
            var diagnostics = compilation.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error);
            foreach (var diagnostic in diagnostics) Console.WriteLine(diagnostic);

            var verificationDriver = new VerificationDriver(compilation, arguments.Timeout, arguments.RecursionDepth);
            var (succeed, satisfiable, message, model, smtText) =
                verificationDriver.Verify(arguments.Class, arguments.Original, arguments.Transformed);

            if (!succeed) return -1;

            Console.WriteLine($"Message:{message}");
            Console.WriteLine("=========================================");

            if (arguments.Debug)
            {
                if (!satisfiable)
                {
                    Console.WriteLine("Model:");
                    if (arguments.Pretty)
                        Highlighter.Highlight(model);
                    else
                        Console.WriteLine(model);
                    Console.WriteLine("\n=========================================");
                }

                Console.WriteLine("SMT:");
                if (arguments.Pretty)
                    Highlighter.Highlight(smtText);
                else
                    Console.WriteLine(smtText);
                Console.WriteLine("\n=========================================");
            }

            return satisfiable ? 0 : -1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return -1;
        }
    }
}