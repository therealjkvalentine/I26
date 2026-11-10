using System.Globalization;
using I76.Formats.Bwd2;
using I76.Formats.Tests;
using I76.Formats.Vfs;
using I76.Game.Shell;
using I76.Sim.Damage;
using I76.Sim.Trip;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The garage's equipment tooltips (PartTips): every part the lists can show has one, at most two lines of plain text, and each
/// number in it is the one the simulation uses (the specials' constants, the component, wheel and weapon files as the sim reads them).
/// </summary>
public class PartTipsTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static void Plain(PartTip t, string what)
    {
        var lines = t.Text.Split('\n');
        Assert.True(lines.Length <= 2, $"{what}: {lines.Length} lines");
        foreach (var l in lines)
        {
            Assert.False(string.IsNullOrWhiteSpace(l), $"{what}: an empty line");
            Assert.All(l, c => Assert.InRange(c, ' ', '~'));
            Assert.True(l.Length <= 66, $"{what}: a line of {l.Length} characters: {l}");
        }
    }

    [Theory]
    [InlineData(false, 9)]
    [InlineData(true, 11)]
    public void EverySpecialHasATooltip(bool nitro, int count)
    {
        Assert.Equal(count, PartsDatabase.SpecialCount(nitro));
        for (int type = 1; type <= count; type++)
        {
            var t = PartTips.Special(type, nitro);
            Assert.NotNull(t);
            Plain(t!, PartsDatabase.SpecialNames[type - 1]);
            Assert.StartsWith(PartsDatabase.SpecialNames[type - 1].Replace("NitrousOxide", "Nitrous Oxide").Replace("Structo Bmpr", "Structo Bumper"), t!.Effect);
        }
    }

    /// <summary>The specials' numbers are the sim's: its constants, its charge counts, and the arithmetic the damage code does with them.</summary>
    [Fact]
    public void TheSpecialsNumbersAreTheSims()
    {
        string S(int type, bool nitro = false) => PartTips.Special(type, nitro)!.Text;
        Assert.Contains($"{SpecialPart.FullCharges(1)} uses of {Specials.JamSeconds.ToString(Inv)} s", S(1));
        Assert.Contains($"within {Combat.ContactRange.ToString(Inv)} m", S(1));
        Assert.Contains($"{Specials.NitroForce.ToString(Inv)} times the drive force for {Specials.NitroSeconds.ToString(Inv)} s", S(2));
        Assert.Contains($"{SpecialPart.FullCharges(2)} charges", S(2));
        Assert.Contains("x1.25", S(3));
        Assert.Equal(1.25f, Specials.BlowerScale);
        Assert.Contains($"{Specials.XAustBrakeScale.ToString(Inv)}x as hard", S(4));
        Assert.Contains($"{Specials.StructoChassis.ToString(Inv)}x as well", S(5));
        Assert.Contains("1.1x as well", S(6));
        Assert.Contains("about 9 % less armor", S(6));
        // what the damage code makes of the Curb Feelers: a hit of 11 wears 9 off the armor (11 / 1.100000024, rounded down)
        Assert.Equal(9, VehicleDamage.Wear(11, Specials.CurbFeelersArmour));
        Assert.Contains("1.1x as well", S(7));
        Assert.Contains("2.1x front and rear", S(7));
        Assert.Contains("10 % more rounds", S(8));
        Assert.Equal(110, Specials.PlusTenth(100));
        Assert.Contains($"one hit in {100 / Specials.CupHoldersPercent}", S(9));
        Assert.Contains("No effect", S(10));
        Assert.Contains("10 % harder", S(10, nitro: true));
        Assert.Contains("No effect", S(11));
        Assert.Contains($"{Specials.TireCoversWheelHp}x the hit points", S(11, nitro: true));
        Assert.Contains("25 %", S(11, nitro: true));
    }

    [Fact]
    public void TheHandgunPaintAndEmptyHaveTooltips()
    {
        Plain(PartTips.Handgun, ".45");
        var g = Armory.HandgunDef(1);
        Assert.Contains($"{g.Rate.ToString(Inv)} a second", PartTips.Handgun.Text);
        Assert.Contains($"reach {MathF.Round(Combat.Reach(g) / 10) * 10} m", PartTips.Handgun.Text);
        Plain(PartTips.Paint, "paint");
        Plain(PartTips.Empty, "empty");
        var tips = new PartTips(null, false);
        Assert.Equal(PartTips.Paint, tips.For(new PartDef("Classic", 14, 0, "", "x.vtf", 0, 1, 0)));
        Assert.Equal(PartTips.Empty, tips.For(new PartDef("EMPTY", 7, 0, "", "", 0, 1, 0)));
        Assert.Contains("Cup Holders", tips.Text(new PartDef("Cup Holders", 13, 0, "spc09", "spc09", 8, 0, 0)));
    }

    // ---- the install's parts ----

    private static void EveryPartOf(AssetVfs vfs, bool nitro, List<string>? dump)
    {
        var db = PartsDatabase.Build(vfs);
        var tips = new PartTips(vfs, nitro);
        var missing = new List<string>();
        var (engines, brakes, susps) = VehicleFactory.ReadComponents(vfs);
        foreach (var p in db.Parts)
        {
            var t = tips.For(p);
            if (t is null) { missing.Add($"{p.Id} ({p.Type}, {p.Stem})"); continue; }
            Plain(t, p.Id);
            if (p.IsEmpty) { Assert.Equal(PartTips.Empty, t); continue; }
            dump?.Add($"[{TypeName(p.Type)}] {p.Id}  ({p.Stem})\n  {t.Text.Replace("\n", "\n  ")}\n  source: {t.Source}\n");
            switch (p.Type)
            {
                case 2:
                    var e = engines[VehicleFactory.EngineCompId(vfs, p.Ordinal)];
                    Assert.Contains($"drive force {e.A.ToString("#,0", Inv)}", t.Text);
                    Assert.Contains($"{e.Hp} hit points", t.Text);
                    break;
                case 3:
                    Assert.Contains($"corners at {susps[p.Ordinal].A.ToString("0.0#", Inv)}x", t.Text);
                    break;
                case 4:
                    Assert.Contains($"{brakes[p.Ordinal].A.ToString("0.0#", Inv)}x braking", t.Text);
                    break;
                case 5:
                    var w = ChassisModel.WheelStatsOf(vfs.Read(p.Stem))!.Value;
                    Assert.Contains($"grip {w.Grip.ToString("0.0#", Inv)}x", t.Text);
                    Assert.Contains($"{w.Hp} hit points", t.Text);
                    break;
                case 7 or 8 or 9:
                    var g = GizmoDef.Read(p.Stem, vfs.Read(p.Stem))!;
                    // a caltrop (no damage channel) and the Nitro Pack's Chemical Mortar (kills the driver) say what they do instead
                    if (g.Damage > 0 && (g.Ordnance?.Mask ?? 0) != 0 && !(nitro && g.Kind == 0x16)) Assert.Contains($"{g.Damage} ", t.Text);
                    Assert.Contains($"{g.HitPoints} hit points", t.Text);
                    break;
            }
        }
        Assert.True(missing.Count == 0, $"no tooltip for {string.Join(", ", missing)}");
        Assert.Equal(PartsDatabase.SpecialCount(nitro), db.Parts.Count(p => p.Type == 13 && !p.IsEmpty));
    }

    private static string TypeName(int t) => t switch { 2 => "engine", 3 => "suspension", 4 => "brakes", 5 => "tyre", 7 => "weapon", 8 => "dropper", 9 => "handgun", 13 => "special", 14 => "paint", _ => $"type {t}" };

    [InstallFact]
    public void EveryPartOfTheGameHasATooltipWithTheSimsNumbers()
    {
        var dump = Environment.GetEnvironmentVariable("I76_TIPS_DUMP") is { Length: > 0 } ? new List<string>() : null;
        try
        {
            EveryPartOf(TestInstall.Vfs, false, dump);
            if (Environment.GetEnvironmentVariable("I76_NITRO_INSTALL") is { Length: > 0 } nd && Directory.Exists(nd))
            {
                dump?.Add("\n==== the Nitro Pack ====\n");
                EveryPartOf(new AssetVfs(new GameInstall(nd)), true, dump);
            }
        }
        finally
        {
            if (dump is not null) File.WriteAllText(Environment.GetEnvironmentVariable("I76_TIPS_DUMP")!, string.Join("\n", dump));
        }
    }
}
