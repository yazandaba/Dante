namespace Sample;

public class Linq
{
    #region failing

    public IEnumerable<int> OriginalTakeAndFilter(int[] numbers)
    {
        var result = new int[10];
        for (var i = 0; i < 10; i++)
        {
            if (numbers[i] > 10 && numbers[i] < 30)
            {
                result[i] = numbers[i];
            }
        }

        return result;
    }

    public IEnumerable<int> TransformedTakeAndFilter(int[] numbers)
    {
        return numbers.Take(10).Where(n => n > 10 && n < 30);
    }

    public IEnumerable<int> OriginalFailTakeAndSelect(int[] numbers)
    {
        var result = new int[10];
        var i = 0;
        while (i < 10)
        {
            result[i] = numbers[i] + 2;
            ++i;
        }

        return result;
    }

    public IEnumerable<int> TransformedFailTakeAndSelect(int[] numbers)
    {
        return numbers.Take(10).Select(n => n + 1);
    }

    #endregion failing


    public IEnumerable<int> OriginalSelect(int[] numbers)
    {
        var result = new int[numbers.Length];
        for (var i = 0; i < numbers.Length; i++)
        {
            result[i] = numbers[i] + 1;
        }

        return result;
    }

    public IEnumerable<int> TransformedSelect(int[] numbers)
    {
        return numbers.Select(n => n + 1);
    }

    public IEnumerable<int> OriginalTakeAndSelect(int[] numbers)
    {
        var result = new int[10];
        var i = 0;
        while (i < 10)
        {
            result[i] = numbers[i++] + 1;
        }

        return result;
    }

    public IEnumerable<int> TransformedTakeAndSelect(int[] numbers)
    {
        return numbers.Take(10).Select(n => n + 1);
    }

    public IEnumerable<int> OriginalOrdering(int[] numbers)
    {
        return numbers.Select(n => n).Where(n => n > 10 && n < 30);
    }

    public IEnumerable<int> TransformedOrdering(int[] numbers)
    {
        return numbers.Where(n => n > 10 && n < 30).Select(n => n);
    }
}