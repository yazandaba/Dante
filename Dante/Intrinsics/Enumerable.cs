using Dante.Extensions;
using Dante.Generator;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.Z3;

namespace Dante.Intrinsics;

internal abstract class EnumerableQueryBase;

internal class WhereEnumerable : EnumerableQueryBase
{
    public required FuncDecl Predicate { get; init; }
}

internal class SelectEnumerable : EnumerableQueryBase
{
    public required FuncDecl ProjectionFunc { get; init; }
}

internal class TakeEnumerable : EnumerableQueryBase
{
    public required IntExpr? Count { get; init; }
}

internal partial class Enumerable
{
    private readonly DatatypeSort _enumerableSort;
    private readonly DatatypeExpr _enumerableExpr;
    private static readonly Dictionary<ITypeSymbol, Enumerable> InstantiationMap = new(SymbolEqualityComparer.Default);
    private static readonly Dictionary<DatatypeSort, Enumerable> UnderlyingTypeInstantiationMap = new();

    public static Enumerable CreateOrGet(ITypeSymbol typeParameter)
    {
        if (InstantiationMap.TryGetValue(typeParameter, out var enumerable)) return enumerable;

        var namedTypeSymbol = (INamedTypeSymbol)typeParameter;
        var typeArgument = namedTypeSymbol.TypeArguments.FirstOrDefault() ?? typeParameter;
        enumerable = new Enumerable(typeArgument.AsSort(true));
        InstantiationMap[typeParameter] = enumerable;
        UnderlyingTypeInstantiationMap[enumerable._enumerableSort] = enumerable;
        return enumerable;
    }

    public static Enumerable CreateOrGet(DatatypeExpr coreEnumerable)
    {
        var sort = (DatatypeSort)coreEnumerable.Sort;
        UnderlyingType.IsEnumerable(coreEnumerable).Should().BeTrue();
        UnderlyingTypeInstantiationMap.Should().ContainKey(sort, "core enumerable sort must be associated " +
                                                                 "to enumerable object, you should create one " +
                                                                 "using Enumerable.CreateOrGet(ITypeSymbol) first ");

        var enumerable = UnderlyingTypeInstantiationMap[sort];
        return new Enumerable(enumerable.ElementSort, coreEnumerable);
    }

    public static Enumerable CreateOrGet(IArrayTypeSymbol arrayType, ArrayExpr arrayExpr)
    {
        var enumerable = CreateOrGet(arrayType.ElementType);
        var elementSort = enumerable.ElementSort;
        return new Enumerable(elementSort, arrayExpr);
    }

    private Enumerable(Sort elementSort, DatatypeExpr expression)
    {
        _enumerableSort = (DatatypeSort)expression.Sort;
        _enumerableExpr = expression;
        ElementSort = elementSort;
    }


    private Enumerable(Sort ofType, ArrayExpr? underlyingArray = default)
    {
        ElementSort = ofType;
        var context = ofType.Context;
        var intSort = context.IntSort;
        var enumerableName = EnumerableNameGenerator.GenerateName(this);
        var underlyingArraySort = context.MkArraySort(intSort, ofType);
        var coreEnumerableCtor = context.MkConstructor("CreateEnumerable", "IsEnumerable", ["coreEnumerable"],
            [underlyingArraySort]);
        _enumerableSort = context.MkDatatypeSort(enumerableName, [coreEnumerableCtor]);
        if (underlyingArray is null)
        {
            var array = context.MkConstArray(ofType, ofType.MkUniqueDefault());
            _enumerableExpr = (DatatypeExpr)_enumerableSort.Constructors[0].Apply(array);
        }
        else
        {
            _enumerableExpr = (DatatypeExpr)_enumerableSort.Constructors[0].Apply(underlyingArray);
        }
    }

    private static Enumerable GenerateTake(Enumerable enumerable, IntExpr count, bool useUniqueDefault = true)
    {
        var ctx = count.Context;
        var elementSort = enumerable.ElementSort;
        var inArray = GenerateToArray(enumerable);
        var inArraySort = (ArraySort)inArray.Sort;
        var takeFuncName = LinqFuncNameGenerator.GenerateName("Take", enumerable);
        var takeFunc = ctx.MkRecFuncDecl(takeFuncName, [enumerable, count.Sort], enumerable);

        var recursiveTakeFuncName = LinqFuncNameGenerator.GenerateName("TakeRec", enumerable);
        var index = (IntExpr)ctx.MkConst("index", ctx.IntSort);
        var outArray = ctx.MkArrayConst("array", inArraySort.Domain, inArraySort.Range);
        var outArraySort = (ArraySort)outArray.Sort;
        var recursiveTakeFunc = ctx.MkRecFuncDecl(recursiveTakeFuncName,
            [inArraySort, outArraySort, count.Sort, index.Sort], outArray.Sort);
        var recursiveTakeBody = ctx.MkITE(ctx.MkLt(index, count),
            recursiveTakeFunc.Apply(
                inArray,
                ctx.MkStore(outArray, index, ctx.MkSelect(inArray, index)),
                count,
                ctx.MkAdd(index, ctx.MkInt(1))),
            outArray);

        var emptyArray = ctx.MkConstArray(
            outArraySort.Domain,
            useUniqueDefault ? outArraySort.Domain.MkUniqueDefault() : outArraySort.Domain.MkDefault()
        );
        var takeBody = recursiveTakeFunc.Apply(inArray, emptyArray, count, ctx.MkInt(0));
        ctx.AddRecDef(recursiveTakeFunc, [inArray, outArray, count, index], recursiveTakeBody);
        ctx.AddRecDef(takeFunc, [enumerable, count], new Enumerable(elementSort, (ArrayExpr)takeBody));
        return new Enumerable(elementSort, (DatatypeExpr)takeFunc.Apply(enumerable, count));
    }

    private static ArrayExpr GenerateToArray(Enumerable enumerable)
    {
        return (ArrayExpr)enumerable._enumerableSort.Accessors[0][0].Apply(enumerable._enumerableExpr);
    }

    private static Enumerable GenerateSelect(Enumerable enumerable, FuncDecl lambda,
        IReadOnlyList<FuncDecl> wherePredicates)
    {
        return Map("Select", enumerable, lambda, wherePredicates);
    }

    private static Enumerable GenerateWhere(Enumerable enumerable, FuncDecl lambda,
        IReadOnlyList<FuncDecl> wherePredicates)
    {
        return Map("Where", enumerable, lambda, wherePredicates);
    }

    private static Enumerable Map(string queryName, Enumerable enumerable, FuncDecl lambda,
        IReadOnlyList<FuncDecl> wherePredicates)
    {
        var enumerableSort = enumerable._enumerableSort;
        var elementSort = enumerable.ElementSort;
        var ctx = elementSort.Context;
        var mapName = LinqFuncNameGenerator.GenerateName(queryName, enumerable);
        var mapFunc = ctx.MkRecFuncDecl(mapName, [enumerable], enumerableSort);
        var predicateFunc = MapCore(queryName, enumerable, lambda, wherePredicates);
        var map = ctx.MkMap(predicateFunc, GenerateToArray(enumerable));
        var body = new Enumerable(elementSort, map);
        ctx.AddRecDef(mapFunc, [enumerable], body);
        return new Enumerable(elementSort, (DatatypeExpr)mapFunc.Apply(enumerable));
    }

    private static FuncDecl MapCore(string queryName, Enumerable enumerable, FuncDecl lambda,
        IReadOnlyList<FuncDecl> wherePredicates)
    {
        var elementSort = enumerable.ElementSort;
        var ctx = elementSort.Context;
        var mapCoreFuncName = LinqFuncNameGenerator.GenerateName($"{queryName}Core", enumerable);
        var mapCoreFunc = ctx.MkRecFuncDecl(mapCoreFuncName, [elementSort], elementSort);
        var mapCoreFuncParam = ctx.MkConstDecl("element", elementSort).Apply();
        var mapCoreFuncParams = new[] { mapCoreFuncParam };
        var lambdaInvoke = MapLambdaInvoke(queryName, lambda, mapCoreFuncParam, wherePredicates);
        ctx.AddRecDef(mapCoreFunc, mapCoreFuncParams, lambdaInvoke);
        return mapCoreFunc;
    }

    private static Expr MapLambdaInvoke(string queryName, FuncDecl lambda, Expr element,
        IReadOnlyList<FuncDecl> wherePredicates)
    {
        return queryName switch
        {
            "Select" => SelectLambdaInvoke(lambda, element, wherePredicates),
            "Where" => WhereLambdaInvoke(lambda, wherePredicates, element),
            _ => element.Sort.MkUniqueDefault()
        };
    }

    private static Expr SelectLambdaInvoke(FuncDecl lambda, Expr element, IReadOnlyList<FuncDecl> wherePredicates)
    {
        var ctx = lambda.Context;
        var defaultValue = element.Sort.MkUniqueDefault();
        if (wherePredicates.Count is 0) return lambda.Apply(element);

        if (wherePredicates.Count is 1)
            return ctx.MkITE((BoolExpr)wherePredicates[0].Apply(element), lambda.Apply(element), defaultValue);

        var selectionCondition = ctx.MkAnd(wherePredicates.Select(p => (BoolExpr)p.Apply(element)));
        return ctx.MkITE(selectionCondition, lambda.Apply(element), defaultValue);
    }

    private static Expr WhereLambdaInvoke(FuncDecl lambda, IReadOnlyList<FuncDecl> predicates, Expr element)
    {
        var ctx = lambda.Context;
        var defaultValue = element.Sort.MkUniqueDefault();
        Expr lambdaInvocation;
        if (predicates.Count > 0)
            lambdaInvocation = ctx.MkITE(
                ctx.MkAnd((BoolExpr)lambda.Apply(element), ctx.MkAnd(
                    predicates.Select(p => (BoolExpr)p.Apply(element)))),
                element,
                defaultValue);
        else
            lambdaInvocation = ctx.MkITE((BoolExpr)lambda.Apply(element), element, defaultValue);

        return lambdaInvocation;
    }

    public Sort ElementSort { get; }

    public static implicit operator Sort(Enumerable enumerable)
    {
        return enumerable._enumerableSort;
    }

    public static implicit operator Expr(Enumerable enumerable)
    {
        return enumerable._enumerableExpr;
    }
}

internal partial class Enumerable
{
    internal class LinqQueryBuilder
    {
        private readonly Queue<EnumerableQueryBase> _subQueries = [];
        private Enumerable? _instance;
        private bool _convertToArray;

        public Enumerable Where(Enumerable enumerable, FuncDecl lambda)
        {
            _subQueries.Enqueue(new WhereEnumerable { Predicate = lambda });
            return enumerable;
        }

        public Enumerable Take(Enumerable enumerable, IntExpr count)
        {
            _subQueries.Enqueue(new TakeEnumerable { Count = count });
            return enumerable;
        }

        public Enumerable Select(Enumerable enumerable, FuncDecl lambda)
        {
            _subQueries.Enqueue(new SelectEnumerable { ProjectionFunc = lambda });
            return enumerable;
        }

        public Enumerable ToArray(Enumerable enumerable)
        {
            _convertToArray = true;
            return enumerable;
        }

        public Enumerable Instance(Enumerable enumerable)
        {
            _instance = enumerable;
            return enumerable;
        }

        public Expr Build()
        {
            _instance.Should().NotBeNull("instance was not initialized");
            Enumerable? result = default;
            IntExpr? takeCount = default;
            var useUniqueDefault = false;
            var predicates = new List<FuncDecl>();
            while (_subQueries.Count > 0)
            {
                var subQuery = _subQueries.Dequeue();
                switch (subQuery)
                {
                    case SelectEnumerable select:
                        result = GenerateSelect(result ?? _instance!, select.ProjectionFunc, predicates);
                        useUniqueDefault = predicates.Count > 0;
                        break;
                    case WhereEnumerable where when _subQueries.TryPeek(out _):
                        predicates.Add(where.Predicate);
                        break;
                    case WhereEnumerable where:
                        result = GenerateWhere(result ?? _instance!, where.Predicate, predicates);
                        useUniqueDefault = true;
                        break;
                    case TakeEnumerable take:
                        takeCount = take.Count;
                        break;
                }
            }

            result ??= _instance!;
            takeCount ??= GenerationContext.GetInstance().RecursionDepth;
            result = GenerateTake(result, takeCount, useUniqueDefault);
            return _convertToArray ? GenerateToArray(result) : result;
        }
    }
}

file static class EnumerableNameGenerator
{
    public static string GenerateName(Enumerable enumerable)
    {
        return $"Enumerable_{enumerable.ElementSort.Name}";
    }
}

file static class LinqFuncNameGenerator
{
    private static ulong _nameId;

    public static string GenerateName(string queryName, Enumerable enumerable)
    {
        return $"{queryName}_{enumerable.ElementSort}_{_nameId++}";
    }
}