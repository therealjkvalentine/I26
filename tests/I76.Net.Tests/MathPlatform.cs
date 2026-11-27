using System.Runtime.InteropServices;

namespace I76.Net.Tests;

/// <summary>
/// Whether the C library under MathF.Sin/Cos/Atan2/... is the one the pinned numbers of a played game were made with (Windows x64's):
/// macOS' and Arm's give other last bits, so a bot game's kills and levels over five minutes fall differently there (see
/// I76.Sim.Tests' MathPlatform and docs/perf/tick-bench.md, "Other platforms").
/// </summary>
internal static class MathPlatform
{
    public static bool LibmAsRecorded => !OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture != Architecture.Arm64;
}
