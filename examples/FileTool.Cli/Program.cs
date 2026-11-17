using System;
using System.Diagnostics.CodeAnalysis;

namespace FileTool.Cli;

internal static class Program
{
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters",
        Justification = "This English-only bootstrap marker is not the future parser UI.")]
    private static void Main()
    {
        Console.WriteLine("FileTool.Cli scaffold: parser example not implemented.");
    }
}
