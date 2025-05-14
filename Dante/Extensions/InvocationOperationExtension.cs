using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Dante.Extensions;

internal static class InvocationOperationExtension
{
    public static bool IsStringMethodCall(this IInvocationOperation invocation, SemanticModel semantics)
    {
        var dotnetString = semantics.Compilation.GetTypeByMetadataName("System.String");
        return invocation.Instance is { Type.SpecialType: SpecialType.System_String } ||
               invocation.TargetMethod.ContainingType.Equals(dotnetString, SymbolEqualityComparer.Default);
    }

    public static bool IsHasValueNullableCall(this IInvocationOperation invocation)
    {
        var invokedMethod = invocation.TargetMethod;
        var nullableHasValue = CSharpBuiltinMethods.NullableHasValue();
        return SymbolEqualityComparer.Default.Equals(invokedMethod, nullableHasValue);
    }

    public static bool IsEnumerableCall(this IInvocationOperation invocation, SemanticModel semantics)
    {
        var dotnetEnumerable = semantics.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        return invocation.TargetMethod.ContainingType.Equals(dotnetEnumerable, SymbolEqualityComparer.Default);
    }
}