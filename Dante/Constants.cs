using Dante.Generator;
using Microsoft.Z3;

namespace Dante;

internal static class Constant
{
    static Constant()
    {
        var ctx = GenerationContext.GetInstance();
        var context = ctx.SolverContext;
        Zero = context.MkInt(0);
        One = context.MkInt(1);
        Two = context.MkInt(2);
        Three = context.MkInt(3);
        Four = context.MkInt(4);
        Five = context.MkInt(5);
        Six = context.MkInt(6);
        Seven = context.MkInt(7);
        Eight = context.MkInt(8);
        Nine = context.MkInt(9);
        SOne = context.MkInt(-1);
    }

    public static IntExpr Zero { get; }
    public static IntExpr One { get; }
    public static IntExpr Two { get; }
    public static IntExpr Three { get; }
    public static IntExpr Four { get; }
    public static IntExpr Five { get; }
    public static IntExpr Six { get; }
    public static IntExpr Seven { get; }
    public static IntExpr Eight { get; }
    public static IntExpr Nine { get; }

    /// <summary>
    ///     signed or minus one (-1)
    /// </summary>
    public static IntExpr SOne { get; }
}