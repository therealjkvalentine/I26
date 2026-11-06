namespace I76.Formats.Shell;

/// <summary>
/// Which folders of the user directory hold the world texture pack: tex-world-8x, tex-world-4x, tex-world-3x and tex-world-2x side
/// by side (pack.json's "scale" says each one's size), or the plain tex-world of a pack made before the sizes had folders. A folder
/// may hold only some textures (pack.json's "partial" and "covers": the 8x pack of the cockpit and the cars), so the loader reads a
/// chain of them (<see cref="Chain"/>), per texture: the chosen size's file when that folder has it, else the next size down's.
/// The size is chosen twice ([port] world_textures and car_textures): for the world, and for the cars and the cockpit's art together; a
/// texture's chain starts at the size its kind asks for (<see cref="ChainFor"/>), and a kind at 0 (1997) has none: the install's own.
/// </summary>
public enum TextureKind { World, Car, Dash }

/// <summary>The texture pack's folders and how a texture is looked for in them (see <see cref="TextureKind"/>).</summary>
public static class HdPackFolders
{
    /// <summary>The sizes the loader looks for, largest first. World Textures draws the 4x and Car &amp; Dash Textures the 4x or the 8x (TextureRow); 3x and 2x packs are still loaded when a file or the Developer panel names them, or when they are all there is.</summary>
    public static readonly int[] Sizes = [8, 4, 3, 2];
    public const int DefaultSize = 4;
    public const string Plain = "tex-world";

    public static string Folder(int size) => $"{Plain}-{size}x";

    /// <summary>A stored size in the set, else the default (a hand-edited file).</summary>
    public static int Normalize(int size) => Array.IndexOf(Sizes, size) >= 0 ? size : DefaultSize;

    /// <summary>The sizes whose folders exist, largest first (<paramref name="exists"/> takes a folder name).</summary>
    public static int[] Available(Func<string, bool> exists) => [.. Sizes.Where(s => exists(Folder(s)))];

    /// <summary>
    /// The first folder to load: the chosen size's when it exists, else the plain tex-world, else (an install with only other sizes) the
    /// nearest size present (a tie takes the larger); null for none.
    /// </summary>
    public static string? Choose(int size, Func<string, bool> exists)
    {
        size = Normalize(size);
        if (exists(Folder(size))) return Folder(size);
        if (exists(Plain)) return Plain;
        var have = Available(exists);
        return have.Length == 0 ? null : Folder(have.MinBy(s => Math.Abs(s - size)));
    }

    /// <summary>
    /// The folders a texture is looked for in, in order: <see cref="Choose"/>'s, then every smaller size present, largest first. A
    /// texture missing from a partial pack (an 8x of the cockpit and the cars) comes from the next size down (the 4x); a full pack
    /// has every texture, so the rest of the chain is never read. The plain tex-world has no size: nothing follows it. Empty for none.
    /// </summary>
    public static string[] Chain(int size, Func<string, bool> exists)
    {
        if (Choose(size, exists) is not { } first) return [];
        if (first == Plain) return [first];
        int top = SizeOf(first);
        return [first, .. Available(exists).Where(s => s < top).Select(Folder)];
    }

    /// <summary>
    /// Whether a texture is the cockpit's own art (the dash, its gauges, panels, hands, mirror and radar: every stem that starts with z,
    /// as tools/upscale-textures.py keeps at full scale) or a car's or the world's (everything else; an 8x pack of the cockpit and the
    /// cars holds no world texture, so the cars' size governs what else it has). The mission maps are z* too but no 8x pack holds them.
    /// </summary>
    public static bool IsCockpit(string member)
    {
        var stem = Path.GetFileName(member);
        return stem.Length > 0 && (stem[0] == 'z' || stem[0] == 'Z');
    }

    /// <summary>
    /// A texture's kind: the cockpit's art (<see cref="IsCockpit"/>), a car's (<paramref name="car"/>: a texture of a vehicle's model, as
    /// the model builder marks them when it loads a car, HdTextures.MarkCar) or the world's (the ground, buildings, props, sky, effects).
    /// </summary>
    public static TextureKind KindOf(string member, bool car) => IsCockpit(member) ? TextureKind.Dash : car ? TextureKind.Car : TextureKind.World;

    /// <summary>The size a kind is looked for at first: the world's, or the one size of the cars and the dash; 0 for the install's own.</summary>
    public static int SizeFor(TextureKind kind, int worldSize, int carSize) => kind == TextureKind.World ? worldSize : carSize;

    /// <summary><see cref="Chain"/> from the size the texture's kind asks for: none (the original) at 0; a car's texture reads the 8x folder only at 8.</summary>
    public static string[] ChainFor(TextureKind kind, int worldSize, int carSize, Func<string, bool> exists) =>
        SizeFor(kind, worldSize, carSize) is var size && size == 0 ? [] : Chain(size, exists);

    /// <summary>The size a chain's first folder serves (0 for the plain tex-world or none).</summary>
    public static int SizeOf(string? folder) =>
        folder is not null && folder.StartsWith(Plain + "-") && folder.EndsWith('x') && int.TryParse(folder.AsSpan(Plain.Length + 1, folder.Length - Plain.Length - 2), out int s) ? s : 0;
}
