using System.Collections.Concurrent;
using I76.Formats.Vfs;

namespace I76.Sim.Vehicles;

/// <summary>
/// The files a mission's cars are built from, read once for the mission. A melee's respawn and regen pad and a co-op respawn
/// build the car again in the middle of a tick; reading and unpacking its files there (and assembling its cockpit model to see
/// which arms hold handguns) was 5-20 ms of that tick. Inside <see cref="Use"/>, on that thread, <see cref="VehicleFactory"/>
/// and the armoury read through it, so the car is built from the bytes the mission's load read (nothing writes to them).
/// Outside a scope, and for another install's files, every read goes to the install as before.
/// </summary>
public sealed class CarFiles(AssetVfs vfs)
{
    [ThreadStatic] private static CarFiles? _current;

    private readonly ConcurrentDictionary<string, byte[]?> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (bool Right, bool Left)> _arms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _loops = new(StringComparer.Ordinal);

    /// <summary>Reads through this cache on this thread until the scope is disposed.</summary>
    public Scope Use()
    {
        var was = _current;
        _current = this;
        return new Scope(was);
    }

    public readonly struct Scope(CarFiles? was) : IDisposable
    {
        public void Dispose() => _current = was;
    }

    private static CarFiles? For(AssetVfs v) => _current is { } c && ReferenceEquals(c._vfs, v) ? c : null;
    private readonly AssetVfs _vfs = vfs;

    /// <summary><see cref="AssetVfs.TryRead"/>, through the thread's scope if there is one for this vfs.</summary>
    internal static byte[]? TryRead(AssetVfs v, string name) => For(v) is { } c ? c._files.GetOrAdd(name, v.TryRead) : v.TryRead(name);

    /// <summary><see cref="AssetVfs.Read(string)"/>, through the thread's scope (a missing file throws as it does).</summary>
    internal static byte[] Read(AssetVfs v, string name) => TryRead(v, name) ?? v.Read(name);

    /// <summary>Which arms of the car's cockpit hold a handgun (its GUNR and GUNL parts), found once a mission per car file in a scope.</summary>
    internal static (bool Right, bool Left) Arms(AssetVfs v, string vcfName, Func<(bool, bool)> find) =>
        For(v) is { } c ? c._arms.GetOrAdd(vcfName, _ => find()) : find();

    /// <summary>Whether a sound file loops (its GAS0 mode), found once a mission per file in a scope.</summary>
    internal static bool Loops(AssetVfs v, string sound) =>
        For(v) is { } c ? c._loops.GetOrAdd(sound, s => I76.Formats.Audio.SoundFiles.Loops(v, s)) : I76.Formats.Audio.SoundFiles.Loops(v, sound);
}
