using I76.Formats.Assembly;
using I76.Formats.Vfs;
using I76.Sim.Fsm;
using I76.Sim.Missions;
using I76.Sim.Session;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The Nitro Pack's Shutdown (P13, Natty Dread). Its police cars (p13cp01, a Texas Fuzz saved with the plain engine row 3) have
/// an ordinary horn, and the script never calls setSiren: they sound no siren, in the original too (SirenTests). The police
/// helicopter waits on its pad by the police cars, engine off (the script's hide), until 90 mission seconds after the first of
/// the four targets (the two Mondo Burgers, the two warehouses) falls, or until it is shot. Then the radio says it is coming
/// (13aut02), it starts, climbs to 16 m (setHeliHeight) and goes for the player, attack and follow by turns.
/// </summary>
public class P13ShutdownTests
{
    private static string? NitroDir =>
        Environment.GetEnvironmentVariable("I76_NITRO_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    [Fact]
    public void The_script_never_turns_a_siren_on()
    {
        if (NitroDir is null) return;
        var install = new GameInstall(NitroDir);
        var code = I76.Formats.Fsm.FsmDisassembler.Disassemble(new I76.Formats.Mission.MissionFile(install.FindMission("p13")!))!.Value.Code;
        Assert.Contains(code, i => i.Mnemonic == "exec" && i.Operand == "setHeliHeight");
        Assert.DoesNotContain(code, i => i.Mnemonic == "exec" && i.Operand == "setSiren");
        // P16, the other mission with the same police car, does
        var p16 = I76.Formats.Fsm.FsmDisassembler.Disassemble(new I76.Formats.Mission.MissionFile(install.FindMission("p16")!))!.Value.Code;
        Assert.Contains(p16, i => i.Mnemonic == "exec" && i.Operand == "setSiren");
    }

    [Fact]
    public void The_helicopter_comes_90_seconds_after_the_first_warehouse_falls()
    {
        if (NitroDir is null) return;
        var install = new GameInstall(NitroDir);
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, install.FindMission("p13")!), asm, rand: new I76.Sim.Rng.CrtRand(1));
        var s = new SimSession(setup);
        s.AddDriver(setup.World.Player!);
        var h = setup.Script!.Host;
        var lines = new List<(string Name, float At)>();
        h.Cb.Started += l => lines.Add((l.Name, h.Now));
        var heli = setup.Entities.Single(e => e.Name == "vuchuey2");
        var warehouse = setup.Entities.First(e => e.Name == "bcywhse2");
        var input = new CarInput();
        float fell = -1, woke = -1;
        void Until(float seconds)
        {
            while (h.Now < seconds && setup.Script.Result is null)
            {
                // the player's car kept whole: the Mondo trucks would end the mission first
                var hd = setup.World.Player!.Health!;
                Array.Copy(hd.ArmourMax, hd.Armour, 4); Array.Copy(hd.ChassisMax, hd.Chassis, 4);
                if (fell < 0 && h.Now >= 20)
                {
                    setup.Damage.DestroyStructure(warehouse.Static!);
                    if (warehouse.Dead) fell = h.Now;
                }
                s.Tick([input]);
                if (woke < 0 && heli.Order != AiOrder.Hide) woke = h.Now;
                setup.Combat.Events.Clear();
                setup.Damage.Events.Clear();
            }
        }
        Until(105);
        Assert.True(fell > 0);
        Assert.Null(setup.Script.Result);
        Assert.Equal(-1, woke);   // 85 s on: still on its pad
        Assert.DoesNotContain(lines, l => l.Name.Equals("13aut02.wav", StringComparison.OrdinalIgnoreCase));
        Until(fell + 105);
        Assert.True(woke - fell is >= 89f and < 91f, $"fell at {fell:0.00}, woke at {woke:0.00}");   // the timers count whole seconds
        // the radio's call is asked for as it starts; it waits its turn behind the lines already queued (priority 3)
        Assert.Single(lines, l => l.Name.Equals("13aut02.wav", StringComparison.OrdinalIgnoreCase));
        Assert.True(heli.Order is AiOrder.Attack or AiOrder.Follow, $"{heli.Order}");
        Assert.Same(h.Player, heli.OrderTarget);
        var v = heli.Vehicle!;
        float above = (float)v.Pos.Y - setup.Ground.BareHeight((float)v.Pos.X, (float)v.Pos.Z);
        Assert.True(above > 10, $"{above:0.0} m above the ground");
    }
}
