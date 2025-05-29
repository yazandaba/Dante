namespace Sample;

public class MatMulThenSum
{
    private long[,] MatMul(long[,] a, long[,] b)
    {
        if (a.GetLength(1) != b.GetLength(0))
        {
            return new long[0, 0];
        }

        var c = new long[a.GetLength(0), b.GetLength(1)];
        for (long i = 0; i < a.GetLength(0); i++)
        for (long j = 0; j < b.GetLength(1); j++)
        for (long k = 0; k < a.GetLength(1); k++)
        {
            c[i, j] += a[i, k] * b[k, j];
        }

        return c;
    }

    public long Original(long[,] a, long[,] b)
    {
        long sum = 0;
        var result = MatMul(a, b);

        for (long m = 0; m < result.GetLength(0); m++)
        for (long n = 0; n < result.GetLength(1); n++)
        {
            sum += result[m, n];
        }

        return sum;
    }

    public long Transformed(long[,] a, long[,] b)
    {
        long sum = 0;
        var result = MatMul(a, b);

        for (long m = result.GetLength(0) - 1; m >= 0; --m)
        for (long n = result.GetLength(1) - 1; n >= 0; --n)
        {
            sum += result[m, n];
        }

        return sum;
    }
}