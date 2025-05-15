namespace Sample;

public class NullCoalescing
{
    public string Original(string? str, string ext)
    {
        if (str == null)
        {
            str = ext;
        }
        
        return str + ext;
    }

    public string Transformed(string? str, string ext)
    {
        str ??= ext;
        return str + ext;
    }
}