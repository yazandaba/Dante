namespace Sample;

public class BasicControlFlow
{
    public string Original(string? x)
    {
        if (x == null)
        {
            x = "Hello";
        }
        else
        {
            x = "Hello";
        }

        return x + "World";
    }

    public string Transformed(string? x)
    {
        return "HelloWorld";
    }
}