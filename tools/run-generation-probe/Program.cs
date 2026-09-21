using GenerationPersistenceProbe;

namespace RunGenerationProbe;

/// <summary>
/// Hands every argument (after the `--` separator of dotnet run) to the probe's
/// entry point and returns its exit code unchanged, so the coordinator can use
/// this project as the single command for a case.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => Runner.Run(args);
}