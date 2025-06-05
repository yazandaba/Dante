namespace Sample;

public class Loop
{
    private int Original(int x)
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

    public int OriginalComplexLoop(int x, int y, int z, int c1, int c2)
    {
        x = x * 21 / y;
        y += x * z;
        while (c1 > 0 && c1 < c1 * 10)
        {
            y -= z;
            while (c2 < 0 && c2 > c2 % 8 * -1)
            {
                z = y;
                y = z + 1;
                c2++;
            }

            c1 += 2;
        }

        return x;
    }

    public int TransformedComplexLoop(int x, int y, int z, int c1, int c2)
    {
        x = x * 21 / y;
        y += x * z;
        while (c1 > 0 && c1 < c1 * 10)
        {
            y -= z;
            Back:
            if (c2 < 0 && c2 > ~(c2 & 0x07) + 1)
            {
                c2++;
                z = y;
                y = z + 1;
                goto Back;
            }

            c1 += 2;
        }

        return x;
    }
}