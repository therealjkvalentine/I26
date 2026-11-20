namespace I76.Formats.Video;

/// <summary>What plays an upscaled .ogv movie: the port's own decoder thread (<see cref="OgvDecoder"/>) or the engine's video player.</summary>
public enum MovieDecodeMode { Thread, Engine }

/// <summary>
/// [port] movie_decode: <c>thread</c> (the default) decodes an upscaled movie on the movie player's own thread through the native
/// library (i26ogv), as the original Smacker briefings are, ahead of the picture with its sound buffered; <c>engine</c> plays it
/// with the engine's video player, which decodes on the main thread, as before. Thread falls back to the engine's player when the
/// library is not there (a macOS or Linux build, a checkout without it) or cannot open the file.
/// </summary>
public static class MovieDecode
{
    public const string Key = "movie_decode";
    public const MovieDecodeMode Default = MovieDecodeMode.Thread;

    /// <summary>The key's value as read: "engine" (any case) is the engine's player, anything else the default.</summary>
    public static MovieDecodeMode Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "engine" => MovieDecodeMode.Engine,
        "thread" => MovieDecodeMode.Thread,
        _ => Default,
    };

    public static string Name(MovieDecodeMode mode) => mode == MovieDecodeMode.Engine ? "engine" : "thread";

    /// <summary>
    /// What plays the movie: the setting, or the hook I76_MOVIE_DECODE (<paramref name="hook"/>: thread or engine) over it; thread
    /// only with the library loaded (<paramref name="libraryLoaded"/> asked only then, so the engine's choice never looks for it).
    /// </summary>
    public static MovieDecodeMode Choose(MovieDecodeMode setting, string? hook, Func<bool> libraryLoaded)
    {
        var want = hook is { Length: > 0 } h && h.Trim().ToLowerInvariant() is "thread" or "engine" ? Parse(h) : setting;
        return want == MovieDecodeMode.Thread && libraryLoaded() ? MovieDecodeMode.Thread : MovieDecodeMode.Engine;
    }
}
