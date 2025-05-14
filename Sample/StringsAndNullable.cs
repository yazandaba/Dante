namespace Sample;

public class StringsAndNullable
{
    public string Original(string? x)
    {
        string? y = "mario";
        if (x == null)
        {
            x = "Hello";
            if (x == "No")
            {
                return "Nono";
            }
        }
        else
        {
            x = null;
            y = "nil";
        }

        x = "hey";
        return x + y;
    }

    public string Transformed(string? x)
    {
        string? y = "mario";
        if (x == null)
        {
            x = "Hello";
            if (x == "No")
            {
                return "Nono";
            }
        }
        else
        {
            x = null;
            y = "nil";
        }
        x = "hey";
        return x + y;
    }
}