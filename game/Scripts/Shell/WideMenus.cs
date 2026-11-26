using Godot;
using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// The optional widescreen menu pack (tex-menu-wide; WideMenuPack, tools/menu-wide.py), found by HdPacks beside the menu pack: the
/// sides of the menu pack's pages for windows wider than 4:3. ShellBackdrop asks it for a page's wide picture; the 4:3 page is the
/// menu pack's, untouched in the middle, so the frame, its hotspots and every 2D layout stay where they were (the sides are
/// decoration only). Graphic Detail's Menu Width ([port] menu_width) turns it off; the 1997 look never asks (it draws no HD art).
/// </summary>
public static class WideMenus
{
    private static HdSource? _source;
    private static WideMenuPack? _pack;
    private static bool _probed;

    /// <summary>The player's Menu Width: Wide (true, the default) or 4:3.</summary>
    public static bool Enabled { get; set; } = true;

    public static bool Installed { get { Probe(); return _pack is not null; } }
    public static WideMenuPack? Pack { get { Probe(); return _pack; } }
    /// <summary>"name version", or null.</summary>
    public static string? Label => Installed ? (_source!.Info?.ToString() ?? "folder") : null;

    private static void Probe()
    {
        if (_probed) return;
        _probed = true;
        if (I76.Game.Assets.HdPacks.Found.Get(HdPackDiscovery.MenuWide) is not { } found) return;
        try { _source = found.Open(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { GD.PushWarning($"wide menu pack: {found.Path}: {e.Message}"); return; }
        _pack = WideMenuPack.Parse(_source.Read("wide.json"));
        if (_pack is null) { GD.PushWarning($"wide menu pack: {found.Path}: no readable wide.json"); _source.Dispose(); _source = null; return; }
        GD.Print($"Wide menu art: {_source.Origin} ({_pack.Count} pages, layers {string.Join(", ", _pack.Layers.Select(l => l.Name))}{(_source.Info is { } i ? $", {i}" : "")})");
    }

    /// <summary>The layer a window of this size takes, or null (no pack, Menu Width 4:3, or a window no wider than 4:3).</summary>
    public static WideMenuPack.Layer? LayerFor(Vector2 window) =>
        Enabled && window.Y > 0 && Pack is { } p ? p.Choose(window.X / window.Y) : null;

    /// <summary>
    /// The record's wide page over its 4:3 page <paramref name="page"/> for <paramref name="layer"/>, and the width added on each side
    /// (texels); null when the pack has no wide page for it (none, or the 4:3 page is not the one its sides were made for: logged once).
    /// </summary>
    public static (Image Image, int Side)? Compose(int key, Image page, WideMenuPack.Layer layer)
    {
        if (Pack is not { } p || _source is null) return null;
        if (page.GetFormat() != Image.Format.Rgba8) page.Convert(Image.Format.Rgba8);
        int w = page.GetWidth(), h = page.GetHeight();
        var got = p.Compose(key, page.GetData(), w, h, layer, Strip);
        if (got is not { } g)
        {
            if (p.Get(key) is not null && _refused.Add(key))
                GD.Print($"Wide menu art: record {key} not widened (its 4:3 page is not the one the {layer.Name} sides were made for, or a strip is missing)");
            return null;
        }
        return (Image.CreateFromData(g.Width, g.Height, false, Image.Format.Rgba8, g.Rgba), (g.Width - w) / 2);
    }

    private static readonly HashSet<int> _refused = [];

    private static (byte[] Rgba, int W, int H)? Strip(string rel)
    {
        if (_source?.Read(rel) is not { } b) return null;
        var img = new Image();
        if (img.LoadWebpFromBuffer(b) != Error.Ok || img.IsEmpty()) return null;
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        return (img.GetData(), img.GetWidth(), img.GetHeight());
    }
}
