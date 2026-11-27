using System.Runtime.InteropServices;

namespace I76.Sim.Tests;

/// <summary>
/// Which platform's maths a test's recorded numbers are of. The sim builds its rotations and aims with MathF.Sin/Cos/Atan2 (and
/// Pow, Exp, ...), which .NET hands to the platform's C library; that library's last bit is not the same everywhere. Measured on
/// an Apple M2 (macOS 15.5, .NET 8.0.33) against Windows x64 (.NET 8.0.34): every float and double Sin, Cos, Tan, Atan, Atan2, Asin,
/// Acos, Exp, Log and Pow over the same 20000 inputs gives other bits, while +, -, *, /, Sqrt, Vector3's Dot, Cross, Length and
/// Normalize, and a*b+c (not contracted into a fused multiply-add) give the same ones. A mission's StateHash then parts from
/// Windows' within a few ticks (T05's tick 4: the player's car's rotation differs in the last bit), so hashes recorded on Windows
/// and missions played by dice measured there are checked only where the C library is the one they were made with
/// (docs/perf/tick-bench.md, "Two machines" and "Other platforms").
/// </summary>
internal static class MathPlatform
{
    /// <summary>False on macOS and on Arm: the C library of the recording (Windows' x64) is not that platform's.</summary>
    public static bool LibmAsRecorded => !OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture != Architecture.Arm64;
}
