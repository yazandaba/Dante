using Microsoft.Z3;

namespace Dante;

internal static class UnderlyingType
{
    private static bool IsMaybe(Sort sort)
    {
        if (sort is not DatatypeSort datatypeSort) return false;

        if (datatypeSort.NumConstructors is not 2) return false;

        var none = datatypeSort.Constructors[0];
        var some = datatypeSort.Constructors[1];
        if (some.Arity is not 1) return false;

        var value = datatypeSort.Accessors[1][0];
        var name = $"Maybe{value.Range}";
        if (some.Domain[0] is ArraySort) name = "MaybeArray";
        return datatypeSort.Name.ToString() == name &&
               none.Name.ToString() is "None" or "none" &&
               some.Name.ToString() is "Some" or "some";
    }

    public static bool IsMaybe(Expr expr)
    {
        return IsMaybe(expr.Sort);
    }

    public static bool IsEnumerable(Sort sort)
    {
        if (sort is not DatatypeSort datatypeSort) return false;

        if (datatypeSort.NumConstructors is not 1) return false;

        var createEnumerable = datatypeSort.Constructors[0];
        if (createEnumerable.Arity is not 1) return false;

        const string name = "Enumerable_";
        return datatypeSort.Name.ToString().StartsWith(name) &&
               createEnumerable.Name.ToString() is "CreateEnumerable";
    }

    public static bool IsEnumerable(Expr expr)
    {
        return IsEnumerable(expr.Sort);
    }
}