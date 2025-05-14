namespace Sample;

public class Loop
{
    int Original(int x)
    {
        while (x > 0 && x < 100)
        {
            x *= 2;
        }

        return x;
    }

    public int Transformed(int y)
    {
        while (y > 0 && y < 100)
        {
            y <<= 1;
        }

        return y;
    }
}