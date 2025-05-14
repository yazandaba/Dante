using Microsoft.Z3;

namespace Dante;

internal sealed class SortPool
{
    public FPSort SingleSort { get; }
    public FPSort DoubleSort { get; }
    public FPSort DecimalSort { get; }
    public FPRMExpr IEEE754Rounding { get; }
    public ArraySort CharArraySort { get; }

    public SortPool(Context context)
    {
        SingleSort = context.MkFPSort32();
        DoubleSort = context.MkFPSort64();
        DecimalSort = context.MkFPSort128();
        IEEE754Rounding = context.MkFPRoundNearestTiesToEven();
        CharArraySort = context.MkArraySort(context.CharSort, context.IntSort);
    }
}