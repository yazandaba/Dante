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
    
    public string Transformed(string? strx, string ext)
    {
        strx ??= ext;
        return strx + ext;
    }
}