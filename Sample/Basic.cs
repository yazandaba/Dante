namespace Sample;

public class Basic
{
    public bool Original(int lhs, int rhs)
    {
        return lhs <= rhs;
    }

    public bool Transformed(int lhs, int rhs)
    {
        return rhs >= lhs;
    }
}