using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace I76.Game.Shell;

/// <summary>
/// shell.cfg's keys as plain values (bool, long, double, string, byte[]; any other type kept as the engine's Variant), so
/// <see cref="ShellSettings.Read"/> and <see cref="ShellSettings.Write"/> run without the engine: the tests build one from a
/// file's text. <see cref="From"/> and <see cref="ToConfig"/> cross to the engine's ConfigFile, in the order the keys were set.
/// </summary>
public sealed class SettingsFile
{
    private readonly List<(string Section, string Key)> _order = [];
    private readonly Dictionary<(string, string), object> _values = [];

    public bool HasSectionKey(string section, string key) => _values.ContainsKey((section, key));

    /// <summary>The key's value, else <paramref name="fallback"/> (absent: an empty value).</summary>
    public SettingsValue GetValue(string section, string key, object? fallback = null) =>
        _values.TryGetValue((section, key), out var v) ? new(v) : fallback is SettingsValue f ? f : new(fallback);

    public void SetValue(string section, string key, object value)
    {
        if (value is SettingsValue w) value = w.Obj ?? "";
        if (value is Variant x) value = Plain(x);
        if (value is int i) value = (long)i;
        else if (value is float f) value = (double)f;
        if (!_values.ContainsKey((section, key))) _order.Add((section, key));
        _values[(section, key)] = value;
    }

    public IEnumerable<(string Section, string Key, object Value)> Entries
    {
        get { foreach (var (s, k) in _order) yield return (s, k, _values[(s, k)]); }
    }

    public static SettingsFile From(ConfigFile c)
    {
        var f = new SettingsFile();
        foreach (var s in c.GetSections())
            foreach (var k in c.GetSectionKeys(s))
                f.SetValue(s, k, c.GetValue(s, k));
        return f;
    }

    /// <summary>
    /// A Variant of a plain type as the plain value (any other type kept as the Variant). Each arm is cast to object: without the
    /// casts the switch's type is Variant (every arm converts to it), and each value went in boxed as a Variant, which the casts of
    /// <see cref="SettingsValue"/> read as 0, false or empty.
    /// </summary>
    private static object Plain(Variant v) => v.VariantType switch
    {
        Variant.Type.Bool => (object)(bool)v,
        Variant.Type.Int => (object)(long)v,
        Variant.Type.Float => (object)(double)v,
        Variant.Type.String => (object)(string)v,
        Variant.Type.PackedByteArray => (object)(byte[])v,
        _ => (object)v,
    };

    public ConfigFile ToConfig()
    {
        var c = new ConfigFile();
        foreach (var (s, k, v) in Entries)
            c.SetValue(s, k, v switch
            {
                bool b => b,
                long l => l,
                double d => d,
                string t => t,
                byte[] a => a,
                Variant x => x,
                _ => v.ToString() ?? "",
            });
        return c;
    }
}

/// <summary>One shell.cfg value, converted on the cast as the engine's Variant converts (a number's text, a number's truth).</summary>
public readonly struct SettingsValue(object? obj)
{
    public object? Obj { get; } = obj;

    private static double Number(object? o) => o switch
    {
        bool b => b ? 1 : 0,
        long l => l,
        int i => i,
        double d => d,
        float f => f,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0,
        _ => 0,
    };

    public static explicit operator long(SettingsValue v) => v.Obj switch
    {
        long l => l,
        int i => i,
        string s when long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
        _ => (long)Number(v.Obj),
    };
    public static explicit operator int(SettingsValue v) => unchecked((int)(long)v);
    public static explicit operator double(SettingsValue v) => Number(v.Obj);
    public static explicit operator float(SettingsValue v) => (float)Number(v.Obj);
    public static explicit operator bool(SettingsValue v) => v.Obj switch
    {
        bool b => b,
        string s => bool.TryParse(s.Trim(), out var b) ? b : s.Length > 0,
        null => false,
        _ => Number(v.Obj) != 0,
    };
    public static explicit operator string(SettingsValue v) => v.ToString();
    public static explicit operator byte[](SettingsValue v) => v.Obj as byte[] ?? [];

    /// <summary>The engine's text for the value: true / false, a whole float with ".0".</summary>
    public override string ToString() => Obj switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        double d => d == Math.Floor(d) && !double.IsInfinity(d) ? d.ToString("0.0", CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Obj.ToString() ?? "",
    };
}
