using System.Diagnostics;

namespace Dante.Extensions;

internal static class StopWatchExtension
{
    public static void DisplayExecutionTime(this Stopwatch stopwatch, string operationName)
    {
        var elapsed = stopwatch.Elapsed;
        Console.Write($"{operationName} completed in ");
        Console.ForegroundColor = ConsoleColor.Green;
        if (elapsed.TotalMilliseconds < 1)
        {
            Console.WriteLine($"{elapsed.TotalMicroseconds:F1} μs");
        }
        else if (elapsed.TotalSeconds < 1)
        {
            Console.WriteLine($"{elapsed.TotalMilliseconds:F2} ms");
        }
        else if (elapsed.TotalMinutes < 1)
        {
            Console.WriteLine($"{elapsed.TotalSeconds:F2} seconds");
        }
        else
        {
            Console.WriteLine($@"{elapsed:mm\:ss\.fff}");
        }

        Console.ResetColor();
    }
}