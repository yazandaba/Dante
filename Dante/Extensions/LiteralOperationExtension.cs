using Dante.Intrinsics;
using FluentAssertions;
using Microsoft.CodeAnalysis.Operations;

namespace Dante.Extensions;

internal static class LiteralOperationExtension
{
    public static bool IsNullableLiteral(this ILiteralOperation literal)
    {
        var constVal = literal.ConstantValue;
        return literal.Type?.IsNullable() ?? constVal is { HasValue: true, Value: null };
    }

    public static MaybeExpr AsDefaultMaybeExpression(this ILiteralOperation literal)
    {
        literal.IsNullableLiteral().Should().BeTrue("none must be generated out of null literal," +
                                                    "when trying to create a 'maybe' expression out of literal");
        var conversionOperation = literal.Parent as IConversionOperation;
        conversionOperation.Should().NotBeNull("null literal cannot be represented as maybe if it is " +
                                               "unknown what the source type is");
        var conversionType = conversionOperation!.Type!;
        var maybe = MaybeIntrinsics.CreateOrGet(conversionType);
        return MaybeIntrinsics.Default(maybe);
    }
}