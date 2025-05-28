namespace Sample;

public class Factorial
{
    public int Original(int x)
    {
        if (x <= 1) return 1;

        return x * Original(x - 1);
    }

    public int Transformed(int x)
    {
        var factorial = 1;
        while (x > 1)
        {
            factorial *= x;
            --x;
        }

        return factorial;
    }
}