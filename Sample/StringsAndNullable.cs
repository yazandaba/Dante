namespace Sample;

public class StringsAndNullable
{
    public string Original(string? x)
    {
        var y = "mario";
        if (x == null)
        {
            x = "Hello";
            if (x == "No")
            {
                return "Nono";
            }

            y = y + " luigi";
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
        string? y = null;
        if (x == null)
        {
            x = "Hello";
            if (x == "No")
            {
                return "Nono";
            }

            y ??= "mario luigi";
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