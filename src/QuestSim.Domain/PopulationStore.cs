using System.Security.Cryptography;

namespace QuestSim.Domain;

// Population snapshots live in numbered files beside world.json. The world references a snapshot (name + SHA-256)
// only after it is fully written, so a crash can never pair a world with a half-written population.
public sealed partial class WorldAuthority
{
    public bool PopulationSaveRequested { get; set; }
    public byte[]? CapturedPopulation { get; private set; }
    public uint CapturedPopulationTick { get; private set; }
    public string PopulationLoad { get; private set; } = "none";

    // Called while the coarse tick is idle; produces the bytes to write off the main thread.
    void CapturePopulation()
    {
        using var buffer = new MemoryStream();
        using (var w = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Pop!.Write(w);
            for (int s = 0; s < 64; s++) { w.Write(slotFact[s]); w.Write(slotStart[s]); w.Write(slotCredibility[s]); }
        }
        CapturedPopulation = buffer.ToArray();
        CapturedPopulationTick = Pop.Tick;
        PopulationSaveRequested = false;
    }

    public byte[]? TakeCapturedPopulation() { var bytes = CapturedPopulation; CapturedPopulation = null; return bytes; }

    public void CommitPopulation(string fileName, string hash) { State.PopulationFile = fileName; State.PopulationHash = hash; }

    // Synchronous capture for shutdown and tests (waits for the running coarse tick first).
    public byte[]? CapturePopulationNow()
    {
        if (Pop is null) return null;
        WaitForPopulation();
        CapturePopulation();
        return TakeCapturedPopulation();
    }

    public static string WritePopulationFile(string worldPath, uint tick, byte[] bytes)
    {
        string name = Path.GetFileName(worldPath) + $".population-{tick}.bin", path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(worldPath))!, name);
        using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
        return name;
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // Keep the committed snapshot and the newest one; remove older generations.
    public static void PrunePopulationFiles(string worldPath, string keep)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(worldPath))!;
        var files = Directory.GetFiles(dir, Path.GetFileName(worldPath) + ".population-*.bin")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f)).ToArray();
        foreach (var f in files.Skip(1).Where(f => Path.GetFileName(f) != keep)) File.Delete(f);
    }

    // On startup: restore the committed snapshot if it is intact, else fall back to seed + deltas (positions and rumors reset).
    public void LoadPopulation(string worldPath)
    {
        if (Pop is null || State.PopulationFile.Length == 0) { PopulationLoad = Pop is null ? "none" : "seed"; return; }
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(worldPath))!, State.PopulationFile);
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (Hash(bytes) != State.PopulationHash) throw new InvalidDataException("hash mismatch");
            using var r = new BinaryReader(new MemoryStream(bytes));
            Pop.Read(r);
            for (int s = 0; s < 64; s++) { slotFact[s] = r.ReadInt32(); slotStart[s] = r.ReadInt64(); slotCredibility[s] = r.ReadUInt32(); }
            foreach (var d in State.AgentDeltas.Where(d => d.Dead)) Pop.Info[d.Index] |= Population.Dead;
            PopulationLoad = "snapshot";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            PopulationLoad = "seed-fallback: " + e.Message;
        }
    }
}
