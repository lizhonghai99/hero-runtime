using System;
using System.Threading.Tasks;

namespace Hero.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var app = new CliApp();
            return await app.RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"hi fatal: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
