using System.Runtime.InteropServices;

namespace I76.Formats.Video;

/// <summary>
/// The native Ogg Theora + Vorbis decoder (native/ogv/i26ogv.c: libogg, libvorbis and libtheora compiled into one library,
/// i26ogv.dll on Windows), loaded at run time from the first of the paths given that opens, so the game runs without it (the
/// engine's own player then plays the upscaled movies, as before). Optional by design: nothing here throws when it is missing.
/// </summary>
public static class OgvLibrary
{
    /// <summary>The library's file name on this system (a macOS or Linux build would be the same source built as these).</summary>
    public static string FileName =>
        OperatingSystem.IsWindows() ? "i26ogv.dll" : OperatingSystem.IsMacOS() ? "libi26ogv.dylib" : "libi26ogv.so";

    /// <summary>The folder name under the game's native/ folder for this system and processor (win-x64, linux-x64, osx-arm64 ...).</summary>
    public static string Platform =>
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    private static readonly object Lock = new();
    private static bool _tried;
    private static Api? _api;

    /// <summary>Where it was loaded from, or null (not looked for yet, or not found).</summary>
    public static string? LoadedFrom { get; private set; }
    /// <summary>The libraries inside, as the library tells them, or null.</summary>
    public static string? Versions { get; private set; }
    /// <summary>Why it is not loaded: the paths looked at, or the export missing; null when loaded.</summary>
    public static string? Missing { get; private set; }

    /// <summary>The library loaded, from the first candidate path that opens and has every export (the first call decides; later ones see its result).</summary>
    public static bool Load(IEnumerable<string?> candidates)
    {
        lock (Lock)
        {
            if (_tried) return _api is not null;
            _tried = true;
            var looked = new List<string>();
            foreach (var path in candidates)
            {
                if (string.IsNullOrEmpty(path)) continue;
                looked.Add(path);
                if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var handle)) continue;
                if (Api.From(handle) is not { } api) { NativeLibrary.Free(handle); Missing = $"{path}: an export is missing"; continue; }
                if (api.Abi() != Abi) { NativeLibrary.Free(handle); Missing = $"{path}: C API {api.Abi()}, this build wants {Abi}"; continue; }
                _api = api;
                LoadedFrom = path;
                Versions = Marshal.PtrToStringUTF8(api.Versions());
                Missing = null;
                return true;
            }
            Missing ??= looked.Count == 0 ? "no path to look at" : "not found at " + string.Join(", ", looked);
            return false;
        }
    }

    /// <summary>Loaded (after <see cref="Load"/>).</summary>
    public static bool Available { get { lock (Lock) return _api is not null; } }

    /// <summary>For tests: forget the result, as if never looked for (a loaded library stays mapped).</summary>
    public static void ResetForTests() { lock (Lock) { _tried = false; _api = null; LoadedFrom = null; Versions = null; Missing = null; } }

    /// <summary>The C API's version this build was written against (I26OGV_ABI).</summary>
    public const int Abi = 1;

    internal static Api? Current { get { lock (Lock) return _api; } }

    internal sealed class Api
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int AbiFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr VersionsFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr OpenFn(IntPtr pathUtf8);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int InfoFn(IntPtr h, out OgvInfo info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int NextFrameFn(IntPtr h, IntPtr y, int ys, IntPtr cb, int cbs, IntPtr cr, int crs, out long index, out double seconds);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int NextRgbaFn(IntPtr h, IntPtr rgba, int stride, out long index, out double seconds);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ReadAudioFn(IntPtr h, IntPtr pcm, int maxFrames);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int RewindFn(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void CloseFn(IntPtr h);

        public required AbiFn Abi;
        public required VersionsFn Versions;
        public required OpenFn Open;
        public required InfoFn Info;
        public required NextFrameFn NextFrame;
        public required NextRgbaFn NextRgba;
        public required ReadAudioFn ReadAudio;
        public required RewindFn Rewind;
        public required CloseFn Close;

        public static Api? From(IntPtr lib)
        {
            T? F<T>(string name) where T : Delegate => NativeLibrary.TryGetExport(lib, name, out var p) ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
            var abi = F<AbiFn>("i26ogv_abi"); var ver = F<VersionsFn>("i26ogv_versions"); var open = F<OpenFn>("i26ogv_open");
            var info = F<InfoFn>("i26ogv_info_get"); var next = F<NextFrameFn>("i26ogv_next_frame"); var rgba = F<NextRgbaFn>("i26ogv_next_frame_rgba");
            var audio = F<ReadAudioFn>("i26ogv_read_audio"); var rew = F<RewindFn>("i26ogv_rewind"); var close = F<CloseFn>("i26ogv_close");
            if (abi is null || ver is null || open is null || info is null || next is null || rgba is null || audio is null || rew is null || close is null) return null;
            return new Api { Abi = abi, Versions = ver, Open = open, Info = info, NextFrame = next, NextRgba = rgba, ReadAudio = audio, Rewind = rew, Close = close };
        }
    }
}

/// <summary>i26ogv_info, as the library fills it.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct OgvInfo
{
    public int Width, Height, FrameWidth, FrameHeight, PicX, PicY, FpsNum, FpsDen, PixelFormat, ColorSpace, AudioRate, AudioChannels;
    public long FrameCount, AudioFrames;

    /// <summary>Seconds a frame.</summary>
    public readonly double FrameDuration => FpsNum > 0 ? FpsDen / (double)FpsNum : 0;
    /// <summary>4:2:0 (TH_PF_420), the only layout the packed picture (<see cref="OgvDecoder.PackedSize"/>) takes.</summary>
    public readonly bool Is420 => PixelFormat == 0;
}

/// <summary>
/// One .ogv file open in the native decoder. One thread at a time (the movie player's decoder thread). The picture comes out
/// packed for one R8 texture (<see cref="PackedSize"/>): the luma plane, then under it each chroma row as Cb then Cr side by
/// side (half the width each), so the main thread uploads one image a frame and a shader makes the colour.
/// </summary>
public sealed class OgvDecoder : IDisposable
{
    private readonly OgvLibrary.Api _api;
    private IntPtr _h;
    public OgvInfo Info { get; }

    private OgvDecoder(OgvLibrary.Api api, IntPtr h, OgvInfo info) { _api = api; _h = h; Info = info; }

    /// <summary>The file opened, or null (no library loaded, no such file, not Theora).</summary>
    public static OgvDecoder? Open(string path)
    {
        if (OgvLibrary.Current is not { } api) return null;
        var utf8 = Marshal.StringToCoTaskMemUTF8(path);
        try
        {
            var h = api.Open(utf8);
            if (h == IntPtr.Zero) return null;
            if (api.Info(h, out var info) != 0 || info.Width <= 0 || info.Height <= 0 || info.FpsNum <= 0) { api.Close(h); return null; }
            return new OgvDecoder(api, h, info);
        }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    /// <summary>The packed 4:2:0 picture's size: the width, and the luma's height plus half (rounded up) for the chroma rows.</summary>
    public static (int Width, int Height) PackedSize(int width, int height) => (width + (width & 1), height + (height + 1) / 2);
    public (int Width, int Height) Packed => PackedSize(Info.Width, Info.Height);
    public int PackedBytes { get { var (w, h) = Packed; return w * h; } }

    /// <summary>
    /// The next frame, packed into <paramref name="buffer"/> (a pinned array of at least <see cref="PackedBytes"/>), or decoded
    /// only when <paramref name="buffer"/> is null (a frame whose turn has passed). False at the end of the stream.
    /// </summary>
    public bool NextPacked(byte[]? buffer, out long index, out double seconds)
    {
        ObjectDisposedException.ThrowIf(_h == IntPtr.Zero, this);
        int r;
        if (buffer is null) r = _api.NextFrame(_h, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, out index, out seconds);
        else
        {
            var (w, h) = Packed;
            if (buffer.Length < w * h) throw new ArgumentException("buffer too small", nameof(buffer));
            int lumaH = Info.Height;
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr p = handle.AddrOfPinnedObject();
                r = _api.NextFrame(_h, p, w, p + w * lumaH, w, p + w * lumaH + w / 2, w, out index, out seconds);
            }
            finally { handle.Free(); }
        }
        if (r < 0) throw new InvalidDataException($"theora decode failed ({r})");
        return r > 0;
    }

    /// <summary>The next frame as RGBA8 (the library's own conversion: BT.601 limited range, chroma bilinear), or decoded only. False at the end.</summary>
    public bool NextRgba(byte[]? rgba, out long index, out double seconds)
    {
        ObjectDisposedException.ThrowIf(_h == IntPtr.Zero, this);
        int r;
        if (rgba is null) r = _api.NextRgba(_h, IntPtr.Zero, 0, out index, out seconds);
        else
        {
            if (rgba.Length < Info.Width * Info.Height * 4) throw new ArgumentException("buffer too small", nameof(rgba));
            var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
            try { r = _api.NextRgba(_h, handle.AddrOfPinnedObject(), Info.Width * 4, out index, out seconds); }
            finally { handle.Free(); }
        }
        if (r < 0) throw new InvalidDataException($"theora decode failed ({r})");
        return r > 0;
    }

    /// <summary>Decoded sound, interleaved float frames of <see cref="OgvInfo.AudioChannels"/>: the frames written, 0 at the end of the sound (or none).</summary>
    public int ReadAudio(float[] pcm)
    {
        ObjectDisposedException.ThrowIf(_h == IntPtr.Zero, this);
        if (Info.AudioChannels <= 0) return 0;
        var handle = GCHandle.Alloc(pcm, GCHandleType.Pinned);
        try
        {
            int r = _api.ReadAudio(_h, handle.AddrOfPinnedObject(), pcm.Length / Info.AudioChannels);
            if (r < 0) throw new InvalidDataException($"vorbis decode failed ({r})");
            return r;
        }
        finally { handle.Free(); }
    }

    /// <summary>Back to the first frame and sample.</summary>
    public void Rewind()
    {
        ObjectDisposedException.ThrowIf(_h == IntPtr.Zero, this);
        if (_api.Rewind(_h) != 0) throw new InvalidDataException("rewind failed");
    }

    public void Dispose()
    {
        if (_h == IntPtr.Zero) return;
        _api.Close(_h);
        _h = IntPtr.Zero;
    }
}
