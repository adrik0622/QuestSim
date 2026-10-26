using System.Text;

namespace QuestSim.Domain;

// Compact binary actor list for the 10 Hz snapshot stream (~18 bytes per NPC). Players keep exact doubles; NPCs use
// float32 positions and regenerate their names client-side from the world seed; only titled (named) NPCs send a name.
public static class Wire
{
    static readonly string[] Conditions = ["", "injured", "infected", "captive"];

    public static byte[] EncodeActors(long sequence, long tick, IReadOnlyList<ActorState> actors, Func<uint, bool> titled)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer, Encoding.UTF8);
        w.Write((byte)1); w.Write(sequence); w.Write(tick); w.Write((ushort)actors.Count);
        foreach (var a in actors)
        {
            int condition = Math.Max(0, Array.IndexOf(Conditions, a.Condition));
            if (a.Kind == 0)
            {
                w.Write((byte)0); w.Write(a.Id); w.Write(a.Name); w.Write(a.X); w.Write(a.Z); w.Write(a.Heading);
            }
            else
            {
                uint index = uint.Parse(a.Id.AsSpan(4));
                bool named = titled(index);
                w.Write((byte)(a.Kind | (named ? 0x80 : 0))); w.Write(index);
                if (named) w.Write(a.Name);
                w.Write((float)a.X); w.Write((float)a.Z); w.Write((short)Math.Round(Math.IEEERemainder(a.Heading, Math.Tau) * 10000));
            }
            w.Write((byte)(a.Faction < 0 ? 255 : a.Faction)); w.Write((byte)Math.Clamp(a.Health, 0, 255)); w.Write((byte)condition);
        }
        w.Flush();
        return buffer.ToArray();
    }

    public static (long Sequence, long Tick, ActorState[] Actors) DecodeActors(byte[] data, ulong seed)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        if (r.ReadByte() != 1) throw new InvalidDataException("Unknown actor packet version");
        long sequence = r.ReadInt64(), tick = r.ReadInt64();
        var actors = new ActorState[r.ReadUInt16()];
        for (int i = 0; i < actors.Length; i++)
        {
            byte kind = r.ReadByte();
            string id, name;
            double x, z, heading;
            if (kind == 0) { id = r.ReadString(); name = r.ReadString(); x = r.ReadDouble(); z = r.ReadDouble(); heading = r.ReadDouble(); }
            else
            {
                uint index = r.ReadUInt32();
                id = $"npc:{index}";
                name = (kind & 0x80) != 0 ? r.ReadString() : Population.GeneratedName(seed, index, (kind & 0x7F) == 2);
                x = r.ReadSingle(); z = r.ReadSingle(); heading = r.ReadInt16() / 10000.0;
            }
            byte faction = r.ReadByte(), health = r.ReadByte(), condition = r.ReadByte();
            actors[i] = new ActorState(id, name, x, z, heading, kind & 0x7F, faction == 255 ? -1 : faction, health, Conditions[Math.Min(condition, (byte)3)]);
        }
        return (sequence, tick, actors);
    }
}

// Background crowd: nearby coarse agents as placeholders (no interaction), sent about once per second, each with the
// velocity it walks at until the next update (0.02 m/s units) so clients can move it smoothly in between.
public static class CrowdWire
{
    public sealed record Figure(uint Index, double X, double Z, bool Zombie, int Faction, double Vx = 0, double Vz = 0);

    public static byte[] Encode(double viewerX, double viewerZ, IReadOnlyList<Figure> figures)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        w.Write((byte)2); w.Write(viewerX); w.Write(viewerZ); w.Write((ushort)figures.Count);
        foreach (var f in figures)
        {
            w.Write(f.Index);
            w.Write((short)Math.Clamp(Math.Round((f.X - viewerX) * 10), short.MinValue, short.MaxValue));
            w.Write((short)Math.Clamp(Math.Round((f.Z - viewerZ) * 10), short.MinValue, short.MaxValue));
            w.Write((byte)((f.Zombie ? 0x80 : 0) | (f.Faction & 0x0F)));
            w.Write((sbyte)Math.Clamp(Math.Round(f.Vx * 50), -127, 127)); w.Write((sbyte)Math.Clamp(Math.Round(f.Vz * 50), -127, 127));
        }
        w.Flush();
        return buffer.ToArray();
    }

    public static Figure[] Decode(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        if (r.ReadByte() != 2) throw new InvalidDataException("Unknown crowd packet version");
        double x = r.ReadDouble(), z = r.ReadDouble();
        var figures = new Figure[r.ReadUInt16()];
        for (int i = 0; i < figures.Length; i++)
        {
            uint index = r.ReadUInt32();
            double fx = x + r.ReadInt16() / 10.0, fz = z + r.ReadInt16() / 10.0;
            byte kind = r.ReadByte();
            double vx = r.ReadSByte() / 50.0, vz = r.ReadSByte() / 50.0;
            figures[i] = new Figure(index, fx, fz, (kind & 0x80) != 0, kind & 0x0F, vx, vz);
        }
        return figures;
    }
}
