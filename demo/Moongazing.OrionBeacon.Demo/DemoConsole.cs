namespace Moongazing.OrionBeacon.Demo;

/// <summary>
/// Small console helpers so every feature demo prints with the same look: a titled banner per
/// section and indented, prefixed step lines.
/// </summary>
internal static class DemoConsole
{
    public static void Banner(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', 72));
    }

    public static void Step(string message) => Console.WriteLine($"  - {message}");

    public static void Note(string message) => Console.WriteLine($"    {message}");
}
