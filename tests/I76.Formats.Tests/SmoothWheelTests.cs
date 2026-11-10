using I76.Formats.Assembly;
using I76.Formats.Vfs;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// The modern look's round wheel (SmoothWheel) against the .wdf mesh it stands in for, on every stock car of the base
/// game and the Nitro Pack: about the same hub, no larger than the original reaches, and its rim on the texture's
/// own tyre.
/// </summary>
[Collection("AddonFiles")]
public class SmoothWheelTests
{
    private static IEnumerable<AssetVfs> Installs()
    {
        if (TestInstall.Dir is not null) yield return TestInstall.Vfs;
        if (NitroFactAttribute.Dir is not null) yield return new AssetVfs(new GameInstall(NitroFactAttribute.Dir));
    }

    /// <summary>A face mapped straight turns its picture with the wheel (+1); one mirrored in u (a wheel on the far side) against it (-1).</summary>
    [Fact]
    public void UvTurnFollowsTheFacesMapping()
    {
        static SmoothWheel Disc(float su)
        {
            var rim = new System.Numerics.Vector3[8]; var uv = new System.Numerics.Vector2[8];
            for (int k = 0; k < 8; k++)
            {
                float a = MathF.Tau * k / 8, y = 0.3f * MathF.Cos(a), z = 0.3f * MathF.Sin(a);
                rim[k] = new(0.1f, 0.5f + y, 2 + z);
                uv[k] = new(0.5f + su * y, 0.5f + z);
            }
            return new SmoothWheel(rim, uv, rim, new(0.1f, 0.5f, 2), new(0.5f, 0.5f), 0.1f, 0, 0, 0, 0, true, 0.3f);
        }
        Assert.Equal(1, Disc(1).UvTurn);
        Assert.Equal(-1, Disc(-1).UvTurn);
        // turning the wheel by a turns its picture by UvTurn * a: the rim point at angle 0 goes to the uv of the point at angle a
        var d = Disc(-1);
        float t = MathF.Tau / 8;
        var p0 = d.RimUv[0] - d.CentreUv; var p1 = d.RimUv[1] - d.CentreUv;
        float turned = MathF.Atan2(p1.Y, p1.X) - MathF.Atan2(p0.Y, p0.X);
        Assert.Equal(d.UvTurn * t, turned, 3);
    }

    /// <summary>The still light is the strip's frames averaged texel by texel: a hole that moves across the frames averages out, the light stays; the first frame's key is its alpha.</summary>
    [Fact]
    public void TheStillLightIsTheStripsMean()
    {
        static I76.Formats.Imaging.IndexedTexture Frame(int hole)
        {
            var ix = new byte[16]; var pal = new ushort[3];
            pal[0] = 0xffff; pal[1] = 0x0000; pal[2] = 0x8410;   // white, black, mid grey
            for (int i = 0; i < 16; i++) ix[i] = i < 8 ? (byte)0 : (byte)2;   // the top half white, the bottom grey: the light
            ix[hole] = 1;   // the hole, in the top half, moving along
            ix[15] = I76.Formats.Imaging.TextureReader.TransparentIndex;
            return new I76.Formats.Imaging.IndexedTexture(4, 4, ix, pal);
        }
        var light = SmoothWheel.StillLight([Frame(0), Frame(1), Frame(2), Frame(3)]);
        Assert.NotNull(light);
        for (int i = 0; i < 4; i++) Assert.Equal(191, light![i * 4]);   // white three times in four
        Assert.Equal(255, light![4 * 4]);   // the top half's other texels: white
        Assert.InRange(light[8 * 4], 128, 136);   // the bottom half: the grey, untouched
        Assert.Equal(0, light[15 * 4 + 3]);   // the key
        Assert.Equal(255, light[0 * 4 + 3]);
        Assert.Null(SmoothWheel.StillLight([Frame(0), new I76.Formats.Imaging.IndexedTexture(2, 2, new byte[4], null)]));
    }

    /// <summary>
    /// Every wheel style the stock cars wear (the first four letters of its strip: WA11 is the Piranha's ten-hole wheel),
    /// whether its way up was read off the real game, and which half of the hub the picture shows brighter where the strip
    /// says so plainly (more than a quarter brighter). The real game (86Box, the retail 1.0 software renderer; T01's Piranha
    /// wearing each style's .wdf in turn, seen from its right side parked) shows every style's strip with its FIRST stored
    /// row at the BOTTOM of the wheel, as it shows the bodies' textures: each strip was registered on the original's frame
    /// by a free affine both ways up, and the way up with the first row at the bottom won on every style that has a way up
    /// to tell (the correlation's margin is given; WA01 and WA91, spoked wheels evenly lit, look alike both ways). So the
    /// chrome domes (WA71, WA81, WAA1) show their sky above and the ground below, the bus's front wheel (WF11) is lit from
    /// above and its rear (WB11), painted the other way up, from below, and the tank's road wheels are on the ground.
    /// A style missing here fails the test: capture it in the original before giving it a row.
    /// </summary>
    public static readonly Dictionary<string, (bool Original, int BrighterHalf, string How)> WheelStyles = new()
    {
        ["WA01"] = (false, 0, "captured; looks alike both ways (0.02)"),
        ["WA11"] = (true, 0, "both right wheels, 0.12 and 0.14"),
        ["WA21"] = (true, 0, "rear 0.20, front 0.07"),
        ["WA31"] = (true, 0, "rear 0.25"),
        ["WA41"] = (true, 0, "rear 0.10, front 0.04"),
        ["WA51"] = (true, 0, "0.29 and 0.34"),
        ["WA61"] = (true, 0, "0.21 and 0.25"),
        ["WA71"] = (true, +1, "0.36 and 0.37; the dome's sky above"),
        ["WA81"] = (true, +1, "0.25 and 0.29; the dome's sky above"),
        ["WA91"] = (false, 0, "captured; looks alike both ways"),
        ["WAA1"] = (true, +1, "rear 0.59; the dome's sky above"),
        ["WB11"] = (true, -1, "the bus's rear, 0.16: lit from below in the original too"),
        ["WF11"] = (true, +1, "the bus's front, 0.20 and 0.21"),
        ["WX11"] = (true, 0, "the tractor's front, 0.22"),
        ["WY11"] = (false, +1, "the tractor's rear, captured cut off by the frame: 0.02, not decisive"),
        ["WD11"] = (true, 0, "the tank's track: its six road wheels at the bottom, by eye"),
        ["WE11"] = (true, 0, "the other tank's track: the same, by eye"),
        ["NA_1"] = (false, 0, "the Dillo's legs wear its body map: not captured"),
    };

    /// <summary>
    /// Every stock car's wheel face maps v upward, so the strip's first stored row is at the bottom of the wheel as the
    /// original shows it (<see cref="WheelStyles"/>), and the picture's brighter half is the one the original shows.
    /// </summary>
    [InstallFact]
    public void EveryWheelFaceShowsItsPictureTheWayUpTheOriginalDoes()
    {
        int faces = 0;
        var seen = new HashSet<string>();
        foreach (var vfs in Installs())
        {
            var res = new TextureResolver(vfs);
            foreach (var n in vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase)).Order())
            {
                AssembledModel? m; try { m = new ModelAssembler(vfs).Assemble(n); } catch { continue; }
                if (m is null) continue;
                foreach (var (slot, w) in m.WheelGeos)
                    foreach (var geo in new[] { w.Intact, w.Blown })
                    {
                        // a face whose v runs along the axle's z (one blown wheel, the Dillo's) has no way up to check
                        if (geo is null || SmoothWheel.FaceMap(geo, w.Node) is not { } fm || Math.Abs(fm.B.X) < 1e-3f) continue;
                        Assert.True(fm.B.X > 0, $"{n} slot {slot} {geo.Name}: v runs down the wheel ({fm.B.X:F3} m per unit of v), the picture upside down");
                        faces++;
                        // a blown tyre's strip (WA1A...) is mapped like its intact one's; none was captured in the original
                        if (geo != w.Intact) continue;
                        string style = fm.Face.Texture.Length >= 4 ? fm.Face.Texture[..4].ToUpperInvariant() : fm.Face.Texture;
                        Assert.True(WheelStyles.TryGetValue(style, out var row), $"{n} slot {slot} {geo.Name}: the wheel style {style} ({fm.Face.Texture}) has no row in WheelStyles");
                        if (row.BrighterHalf == 0 || !seen.Add(style + (slot & 1))) continue;
                        if (res.ResolveFace(fm.Face.Texture, m.ObjectCode, m.TmtNames, vtfMaps: m.VtfMaps) is not { } mem || res.Load(mem) is not { } tex) continue;
                        // the hub (within 0.22 of the picture's middle): its mean luminance above and below the axle as the face shows it
                        var rgba = I76.Formats.Imaging.TextureReader.ToRgba(tex, null);
                        double up = 0, down = 0; int nu = 0, nd = 0;
                        for (int y = 0; y < tex.Height; y++)
                            for (int x = 0; x < tex.Width; x++)
                            {
                                float du = (x + 0.5f) / tex.Width - 0.5f, dv = (y + 0.5f) / tex.Height - 0.5f;
                                int i = (y * tex.Width + x) * 4;
                                if (du * du + dv * dv > 0.22f * 0.22f || rgba[i + 3] == 0) continue;
                                float h = fm.A.X * du + fm.B.X * dv;   // metres above the axle
                                double l = 0.299 * rgba[i] + 0.587 * rgba[i + 1] + 0.114 * rgba[i + 2];
                                if (h > 0) { up += l; nu++; } else if (h < 0) { down += l; nd++; }
                            }
                        up /= nu; down /= nd;
                        Assert.True(row.BrighterHalf > 0 ? up > 1.25 * down : down > 1.25 * up,
                            $"{n} slot {slot} {style}: the hub shows {up:F0} above the axle and {down:F0} below; the original shows it brighter {(row.BrighterHalf > 0 ? "above" : "below")}");
                    }
            }
        }
        Assert.True(faces > 600, $"{faces} wheel faces checked");
        Assert.True(seen.Count >= 10, $"{seen.Count} strips' brighter halves checked");
    }

    [InstallFact]
    public void EveryRoundWheelIsFittedToItsHubAndTexture()
    {
        int fitted = 0, blown = 0;
        foreach (var vfs in Installs())
        {
            var res = new TextureResolver(vfs);
            foreach (var n in vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase)).Order())
            {
                AssembledModel? m; try { m = new ModelAssembler(vfs).Assemble(n); } catch { continue; }
                if (m is null) continue;
                foreach (var (slot, w) in m.WheelGeos)
                    foreach (var geo in new[] { w.Intact, w.Blown })
                    {
                        if (geo is null || !Wheels.RollsAboutHub(geo, w.Node)) continue;
                        var face = geo.Faces.FirstOrDefault(f => f.Texture.Length > 0);
                        if (face is null || res.ResolveFace(face.Texture, m.ObjectCode, m.TmtNames, vtfMaps: m.VtfMaps) is not { } mem || res.Load(mem) is not { } tex) continue;
                        if (SmoothWheel.Build(geo, w.Node, tex) is not { } s) continue;
                        var vs = geo.Vertices.Select(w.Node.Apply).ToArray();
                        float y0 = vs.Min(p => p.Y), y1 = vs.Max(p => p.Y), z0 = vs.Min(p => p.Z), z1 = vs.Max(p => p.Z);
                        string who = $"{n} slot {slot} {mem}";
                        Assert.True(s.UvTurn != 0, $"{who}: the face's mapping gives no sense to turn its light by");
                        // the rim lies on the original's own outer face, inside the box the original spans (a little over for the texture's edge)
                        foreach (var p in s.Rim)
                        {
                            Assert.InRange(p.Y, y0 - 0.02f, y1 + 0.02f);
                            Assert.InRange(p.Z, z0 - 0.02f, z1 + 0.02f);
                        }
                        // every rim point is on an opaque texel; a circle's is near the keyed edge (keyed 4 texels further out) and inside
                        // what round_key keeps solid, so the face is not half covered at its rim
                        for (int k = 0; k < s.Rim.Length; k++)
                        {
                            var uv = s.RimUv[k]; var c = s.CentreUv;
                            var d = Vector2Norm(uv - c);
                            float px = uv.X * tex.Width, py = uv.Y * tex.Height;
                            Assert.True(TextureReader255 != tex.Indices[(int)py * tex.Width + (int)px], $"{who} (circle {s.Circle}): rim {k} of {s.Rim.Length} at {px:F1},{py:F1} is keyed");
                            float fromMid = (uv - new System.Numerics.Vector2(0.5f)).Length();
                            if (s.Circle) Assert.True(fromMid <= SmoothWheel.RoundKeySolid + 1e-4f, $"{who}: rim {k} reads at uv radius {fromMid:F3}, in round_key's fade");
                            float ox = px + d.X * 4f, oy = py + d.Y * 4f;
                            if (s.Circle && ox >= 0 && oy >= 0 && ox < tex.Width && oy < tex.Height)
                                Assert.True(TextureReader255 == tex.Indices[(int)oy * tex.Width + (int)ox], $"{who} (circle {s.Circle}): rim {k} at {px:F1},{py:F1} has no edge outside");
                        }
                        if (s.Circle)
                        {
                            // a circle about the hub, within 3 cm of the original's reach (the ground contact) and not past it
                            Assert.True(Math.Abs(s.Centre.Y) < 0.03f && Math.Abs(s.Centre.Z) < 0.03f, $"{who}: off the hub");
                            float reach = Math.Min(Math.Min(-y0, y1), Math.Min(-z0, z1));
                            Assert.True(s.Radius <= reach + 1e-3f && s.Radius > reach - 0.03f - 0.05f * reach, $"{who}: radius {s.Radius} against {reach}");
                            foreach (var p in s.Rim)
                                Assert.InRange(Math.Sqrt((p.Y - s.Centre.Y) * (p.Y - s.Centre.Y) + (p.Z - s.Centre.Z) * (p.Z - s.Centre.Z)), s.Radius - 1e-3, s.Radius + 1e-3);
                            fitted++;
                        }
                        else blown++;
                        Assert.True(Math.Abs(s.InnerX - s.OuterX) > 0.05f, who);
                    }
            }
        }
        Assert.True(fitted > 500, $"{fitted} round wheels fitted");
        Assert.True(blown > 100, $"{blown} blown outlines fitted");
    }

    private const byte TextureReader255 = 255;
    private static System.Numerics.Vector2 Vector2Norm(System.Numerics.Vector2 v) => v.LengthSquared() < 1e-12f ? System.Numerics.Vector2.UnitX : System.Numerics.Vector2.Normalize(v);
}
