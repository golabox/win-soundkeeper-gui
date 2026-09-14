using System.Runtime.InteropServices;

namespace SoundKeeper.GUI.Services;

public sealed class EngineExecutableResolver
{
    private readonly string _baseDirectory;
    private readonly Architecture _architecture;

    public EngineExecutableResolver(string? baseDirectory = null, Architecture? architecture = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _architecture = architecture ?? RuntimeInformation.ProcessArchitecture;
    }

    public string ExpectedFileName => _architecture switch
    {
        Architecture.Arm64 => "SoundKeeperARM64.exe",
        Architecture.X64 => "SoundKeeper64.exe",
        _ => throw new PlatformNotSupportedException("Sound Keeper GUI prend en charge x64 et ARM64.")
    };

    public string? Resolve()
    {
        var overridePath = Environment.GetEnvironmentVariable("SOUNDKEEPER_ENGINE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        foreach (var candidate in GetCandidates())
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public IReadOnlyList<string> GetCandidates()
    {
        var fileName = ExpectedFileName;
        return
        [
            Path.Combine(_baseDirectory, "Engine", fileName),
            Path.Combine(_baseDirectory, fileName),
            Path.GetFullPath(Path.Combine(_baseDirectory, "..", "..", "..", "..", "Bin", fileName)),
            Path.GetFullPath(Path.Combine(_baseDirectory, "..", "..", "..", "..", "..", "Bin", fileName))
        ];
    }
}
