namespace I76.Sim.Collision;

/// <summary>
/// A grid over the static objects' XZ bounds, for the object pass (<see cref="Vehicles.VehicleStep"/>'s ObjectContacts), which
/// tested every car against every object of the map every tick. <see cref="Near"/> gives the indices, in the list's order, of
/// every object whose bounds meet a box: a superset of the objects the pass's own first tests let through, which it then runs
/// as before, so its contacts are the same. An object's bounds are the ones those tests read: a sign placement's part spheres
/// (or its own sphere), anything else's centre and XZ radius. The grid is built again whenever the list or any placement's bounds
/// changed (<see cref="StaticObject.BoundsEpoch"/>: the builds, a collapse, a refresh after a node's death or a gate's slide,
/// a restore). An object whose bounds are not finite is in every answer.
/// </summary>
internal sealed class ObjectIndex
{
    private const int MaxCells = 256;
    private const float MinCell = 32f;

    private int _epoch = int.MinValue, _count = -1;
    private StaticObject? _first, _last;
    private float _x0, _z0, _cell = MinCell;
    private int _nx, _nz;
    private int[] _start = [], _items = [], _always = [];
    private int[] _seen = [];
    private int _stamp;
    private float[] _lx = [], _hx = [], _lz = [], _hz = [];
    private readonly object _lock = new();

    /// <summary>
    /// Fills <paramref name="into"/> (grown as needed) with the indices of the objects of <paramref name="objects"/> whose bounds
    /// meet [minX, maxX] x [minZ, maxZ], ascending; the count. A box that is not finite gets every object.
    /// </summary>
    public int Near(List<StaticObject> objects, float minX, float maxX, float minZ, float maxZ, ref int[] into)
    {
        lock (_lock)
        {
            if (_count != objects.Count || _epoch != Volatile.Read(ref StaticObject.BoundsEpoch)
                || (_count > 0 && (!ReferenceEquals(_first, objects[0]) || !ReferenceEquals(_last, objects[^1]))))
                Build(objects);
            if (!(float.IsFinite(minX) && float.IsFinite(maxX) && float.IsFinite(minZ) && float.IsFinite(maxZ)) || _nx == 0)
            {
                if (into.Length < objects.Count) into = new int[objects.Count];
                for (int i = 0; i < objects.Count; i++) into[i] = i;
                return objects.Count;
            }
            if (++_stamp == int.MaxValue) { Array.Clear(_seen); _stamp = 1; }
            int n = 0;
            foreach (int i in _always) Add(i, ref into, ref n);
            int ix0 = X(minX), ix1 = X(maxX), iz0 = Z(minZ), iz1 = Z(maxZ);
            for (int iz = iz0; iz <= iz1; iz++)
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    int c = iz * _nx + ix;
                    for (int k = _start[c]; k < _start[c + 1]; k++)
                    {
                        int i = _items[k];
                        // the cell's objects, kept only if their own bounds meet the box (the pass's order is restored below)
                        if (_hx[i] < minX || _lx[i] > maxX || _hz[i] < minZ || _lz[i] > maxZ) continue;
                        Add(i, ref into, ref n);
                    }
                }
            Array.Sort(into, 0, n);
            return n;
        }
    }

    private void Add(int i, ref int[] into, ref int n)
    {
        if (_seen[i] == _stamp) return;
        _seen[i] = _stamp;
        if (n == into.Length) Array.Resize(ref into, System.Math.Max(16, into.Length * 2));
        into[n++] = i;
    }

    private int X(float x) => System.Math.Clamp((int)MathF.Floor((x - _x0) / _cell), 0, _nx - 1);
    private int Z(float z) => System.Math.Clamp((int)MathF.Floor((z - _z0) / _cell), 0, _nz - 1);

    private void Build(List<StaticObject> objects)
    {
        // the epoch first: a change during the build makes the next query build again
        _epoch = Volatile.Read(ref StaticObject.BoundsEpoch);
        int count = objects.Count;
        _count = count;
        _first = count > 0 ? objects[0] : null;
        _last = count > 0 ? objects[^1] : null;
        if (_lx.Length < count) { _lx = new float[count]; _hx = new float[count]; _lz = new float[count]; _hz = new float[count]; }
        if (_seen.Length < count) { _seen = new int[count]; _stamp = 0; }
        var always = new List<int>();
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            var o = objects[i];
            float lx, hx, lz, hz;
            if (o.SignPlacement)
            {
                if (o.Spheres.Count == 0) (lx, hx, lz, hz) = (o.Centre.X - o.SphereRadius, o.Centre.X + o.SphereRadius, o.Centre.Z - o.SphereRadius, o.Centre.Z + o.SphereRadius);
                else
                {
                    (lx, hx, lz, hz) = (float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);
                    foreach (var sp in o.Spheres)
                    {
                        lx = MathF.Min(lx, sp.Centre.X - sp.Radius); hx = MathF.Max(hx, sp.Centre.X + sp.Radius);
                        lz = MathF.Min(lz, sp.Centre.Z - sp.Radius); hz = MathF.Max(hz, sp.Centre.Z + sp.Radius);
                    }
                }
            }
            else (lx, hx, lz, hz) = (o.Centre.X - o.Radius, o.Centre.X + o.Radius, o.Centre.Z - o.Radius, o.Centre.Z + o.Radius);
            (_lx[i], _hx[i], _lz[i], _hz[i]) = (lx, hx, lz, hz);
            if (!(float.IsFinite(lx) && float.IsFinite(hx) && float.IsFinite(lz) && float.IsFinite(hz)) || lx > hx || lz > hz) { always.Add(i); continue; }
            x0 = MathF.Min(x0, lx); x1 = MathF.Max(x1, hx); z0 = MathF.Min(z0, lz); z1 = MathF.Max(z1, hz);
        }
        _always = [.. always];
        if (x0 > x1) { _nx = _nz = 0; _start = [0]; _items = []; return; }
        _cell = MathF.Max(MinCell, MathF.Max(x1 - x0, z1 - z0) / MaxCells);
        (_x0, _z0) = (x0, z0);
        _nx = System.Math.Clamp((int)MathF.Floor((x1 - x0) / _cell) + 1, 1, MaxCells + 1);
        _nz = System.Math.Clamp((int)MathF.Floor((z1 - z0) / _cell) + 1, 1, MaxCells + 1);
        // counts, then offsets, then the items (each object in every cell its bounds cover, in index order within a cell)
        var counts = new int[_nx * _nz + 1];
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                int sum = 0;
                for (int c = 0; c < counts.Length; c++) { int k = counts[c]; counts[c] = sum; sum += k; }
                _start = (int[])counts.Clone();
                _items = new int[sum];
            }
            for (int i = 0; i < count; i++)
            {
                if (!(float.IsFinite(_lx[i]) && float.IsFinite(_hx[i]) && float.IsFinite(_lz[i]) && float.IsFinite(_hz[i])) || _lx[i] > _hx[i] || _lz[i] > _hz[i]) continue;
                int ax = X(_lx[i]), bx = X(_hx[i]), az = Z(_lz[i]), bz = Z(_hz[i]);
                for (int iz = az; iz <= bz; iz++)
                    for (int ix = ax; ix <= bx; ix++)
                    {
                        int c = iz * _nx + ix;
                        if (pass == 0) counts[c]++;
                        else _items[counts[c]++] = i;
                    }
            }
        }
    }
}
