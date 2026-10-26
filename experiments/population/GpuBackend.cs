using System.Diagnostics;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace QuestSim.Population;

// Vulkan compute backend. Each tick is one command buffer; readbacks land in a ring of host buffers and are
// consumed ReadbackDelay ticks later, so the authority never waits on the tick it just submitted.
public sealed unsafe class GpuBackend : IBackend
{
    const int Bindings = 18, Timestamps = 5;
    readonly Vk vk = Vk.GetApi();
    readonly Settings s;
    readonly World w;
    Instance instance;
    PhysicalDevice physical;
    Device device;
    Queue queue;
    uint family;
    float timestampPeriod;
    CommandPool pool;
    DescriptorPool descriptorPool;
    DescriptorSetLayout setLayout;
    PipelineLayout layout;
    Pipeline move, insert, spread;
    readonly List<(Buffer Buffer, DeviceMemory Memory)> owned = new();
    readonly Gpu[] buffers = new Gpu[Bindings];
    readonly Slot[] slots;
    readonly Stopwatch clock = new();
    public string Name => "gpu";
    public string DeviceName { get; private set; } = "";
    public long DeviceBytes { get; private set; }
    public Dictionary<string, double> LastTimingsMs { get; } = new();

    record struct Gpu(Buffer Buffer, DeviceMemory Memory, ulong Size, nint Mapped);
    sealed class Slot
    {
        public CommandBuffer Command;
        public Fence Fence;
        public DescriptorSet Set;
        public QueryPool Queries;
        public Gpu Rumor, Counters, Agg, Events;
        public uint Tick = uint.MaxValue;
        public bool Busy;
        public long SubmittedAt;
    }

    public GpuBackend(Settings settings, World world, AgentState state, string shaderDir)
    {
        s = settings; w = world;
        slots = new Slot[s.ReadbackDelay + 1];
        CreateDevice();
        long n = s.Agents;
        for (int b = 0; b < Bindings; b++)
        {
            ulong size = b switch
            {
                5 => (ulong)w.Settlements.Length * 4, 6 => (ulong)w.AdjOff.Length * 4, 7 => (ulong)Math.Max(1, w.Adj.Length) * 4,
                8 => Settings.FactionSlots * Settings.RegionCount * 4, 9 => (s.TableMask + 1UL) * 4,
                15 => s.EventCapacity * 8UL, 16 => 130 * 4, 17 => (ulong)new TickInput().Rumor.Length * 4,
                _ => (ulong)n * 4
            };
            buffers[b] = Create(size, deviceLocal: true);
        }
        CreateLayout();
        move = Pipeline(Path.Combine(shaderDir, "move.spv"));
        insert = Pipeline(Path.Combine(shaderDir, "insert.spv"));
        spread = Pipeline(Path.Combine(shaderDir, "spread.spv"));
        for (int i = 0; i < slots.Length; i++) slots[i] = CreateSlot();
        Upload(state);
    }

    void CreateDevice()
    {
        var name = (byte*)SilkMarshal.StringToPtr("QuestSim.Population");
        var app = new ApplicationInfo { SType = StructureType.ApplicationInfo, PApplicationName = name, ApiVersion = Vk.Version12 };
        var info = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &app };
        Check(vk.CreateInstance(&info, null, out instance), "instance");
        uint count = 0;
        vk.EnumeratePhysicalDevices(instance, &count, null);
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices) vk.EnumeratePhysicalDevices(instance, &count, p);
        physical = devices.OrderByDescending(d => { vk.GetPhysicalDeviceProperties(d, out var pr); return pr.DeviceType == PhysicalDeviceType.DiscreteGpu; }).First();
        vk.GetPhysicalDeviceProperties(physical, out var props);
        DeviceName = SilkMarshal.PtrToString((nint)props.DeviceName) ?? "";
        timestampPeriod = props.Limits.TimestampPeriod;
        uint families = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physical, &families, null);
        var familyProps = new QueueFamilyProperties[families];
        fixed (QueueFamilyProperties* p = familyProps) vk.GetPhysicalDeviceQueueFamilyProperties(physical, &families, p);
        family = (uint)Array.FindIndex(familyProps, f => f.QueueFlags.HasFlag(QueueFlags.ComputeBit));
        float priority = 1;
        var queueInfo = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = family, QueueCount = 1, PQueuePriorities = &priority };
        var deviceInfo = new DeviceCreateInfo { SType = StructureType.DeviceCreateInfo, QueueCreateInfoCount = 1, PQueueCreateInfos = &queueInfo };
        Check(vk.CreateDevice(physical, &deviceInfo, null, out device), "device");
        vk.GetDeviceQueue(device, family, 0, out queue);
        var poolInfo = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = family, Flags = CommandPoolCreateFlags.ResetCommandBufferBit };
        Check(vk.CreateCommandPool(device, &poolInfo, null, out pool), "command pool");
    }

    Gpu Create(ulong size, bool deviceLocal)
    {
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo, Size = size, SharingMode = SharingMode.Exclusive,
            Usage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit
        };
        Check(vk.CreateBuffer(device, &info, null, out var buffer), "buffer");
        vk.GetBufferMemoryRequirements(device, buffer, out var req);
        vk.GetPhysicalDeviceMemoryProperties(physical, out var mem);
        int Find(MemoryPropertyFlags want)
        {
            for (int i = 0; i < mem.MemoryTypeCount; i++)
                if ((req.MemoryTypeBits & (1u << i)) != 0 && mem.MemoryTypes[i].PropertyFlags.HasFlag(want)) return i;
            return -1;
        }
        const MemoryPropertyFlags host = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        int type = deviceLocal ? Find(MemoryPropertyFlags.DeviceLocalBit) : Find(host | MemoryPropertyFlags.HostCachedBit) is >= 0 and var cached ? cached : Find(host);
        if (type < 0) throw new InvalidOperationException("No suitable memory type");
        var alloc = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = req.Size, MemoryTypeIndex = (uint)type };
        Check(vk.AllocateMemory(device, &alloc, null, out var memory), $"allocate {size} bytes");
        Check(vk.BindBufferMemory(device, buffer, memory, 0), "bind");
        void* mapped = null;
        if (!deviceLocal) Check(vk.MapMemory(device, memory, 0, size, 0, &mapped), "map");
        else DeviceBytes += (long)req.Size;
        owned.Add((buffer, memory));
        return new Gpu(buffer, memory, size, (nint)mapped);
    }

    void CreateLayout()
    {
        var bindings = new DescriptorSetLayoutBinding[Bindings];
        for (uint b = 0; b < Bindings; b++)
            bindings[b] = new DescriptorSetLayoutBinding { Binding = b, DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit };
        fixed (DescriptorSetLayoutBinding* p = bindings)
        {
            var info = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = Bindings, PBindings = p };
            Check(vk.CreateDescriptorSetLayout(device, &info, null, out setLayout), "set layout");
        }
        var range = new PushConstantRange { StageFlags = ShaderStageFlags.ComputeBit, Offset = 0, Size = (uint)sizeof(Push) };
        var l = setLayout;
        var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &l, PushConstantRangeCount = 1, PPushConstantRanges = &range };
        Check(vk.CreatePipelineLayout(device, &layoutInfo, null, out layout), "pipeline layout");
        var size = new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = (uint)(Bindings * slots.Length) };
        var poolInfo = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = (uint)slots.Length, PoolSizeCount = 1, PPoolSizes = &size };
        Check(vk.CreateDescriptorPool(device, &poolInfo, null, out descriptorPool), "descriptor pool");
    }

    Pipeline Pipeline(string spv)
    {
        var code = File.ReadAllBytes(spv);
        ShaderModule module;
        fixed (byte* p = code)
        {
            var info = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = (uint*)p };
            Check(vk.CreateShaderModule(device, &info, null, out module), "shader " + spv);
        }
        var entry = (byte*)SilkMarshal.StringToPtr("main");
        var stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit, Module = module, PName = entry };
        var pipelineInfo = new ComputePipelineCreateInfo { SType = StructureType.ComputePipelineCreateInfo, Stage = stage, Layout = layout };
        Check(vk.CreateComputePipelines(device, default, 1, &pipelineInfo, null, out var pipeline), "pipeline " + spv);
        vk.DestroyShaderModule(device, module, null);
        return pipeline;
    }

    Slot CreateSlot()
    {
        var slot = new Slot
        {
            Rumor = Create(buffers[17].Size, deviceLocal: false), Counters = Create(130 * 4, deviceLocal: false),
            Agg = Create(buffers[8].Size, deviceLocal: false), Events = Create(buffers[15].Size, deviceLocal: false)
        };
        var alloc = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
        Check(vk.AllocateCommandBuffers(device, &alloc, out slot.Command), "command buffer");
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        Check(vk.CreateFence(device, &fenceInfo, null, out slot.Fence), "fence");
        var queryInfo = new QueryPoolCreateInfo { SType = StructureType.QueryPoolCreateInfo, QueryType = QueryType.Timestamp, QueryCount = Timestamps };
        Check(vk.CreateQueryPool(device, &queryInfo, null, out slot.Queries), "query pool");
        var l = setLayout;
        var setInfo = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = descriptorPool, DescriptorSetCount = 1, PSetLayouts = &l };
        Check(vk.AllocateDescriptorSets(device, &setInfo, out slot.Set), "descriptor set");
        var infos = new DescriptorBufferInfo[Bindings];
        var writes = new WriteDescriptorSet[Bindings];
        fixed (DescriptorBufferInfo* pi = infos)
        {
            for (int b = 0; b < Bindings; b++)
            {
                infos[b] = new DescriptorBufferInfo { Buffer = b == 17 ? slot.Rumor.Buffer : buffers[b].Buffer, Offset = 0, Range = Vk.WholeSize };
                writes[b] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = slot.Set, DstBinding = (uint)b, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = pi + b };
            }
            fixed (WriteDescriptorSet* pw = writes) vk.UpdateDescriptorSets(device, Bindings, pw, 0, null);
        }
        return slot;
    }

    // Host-side staging copies through a temporary buffer, used only for upload/download (not per tick).
    void Transfer(int binding, uint[] data, bool toDevice)
    {
        ulong size = (ulong)data.Length * 4;
        if (size == 0) return;
        var staging = Create(size, deviceLocal: false);
        if (toDevice) new Span<uint>(data).CopyTo(new Span<uint>((void*)staging.Mapped, data.Length));
        OneShot(cmd =>
        {
            var copy = new BufferCopy { Size = size };
            if (toDevice) vk.CmdCopyBuffer(cmd, staging.Buffer, buffers[binding].Buffer, 1, &copy);
            else vk.CmdCopyBuffer(cmd, buffers[binding].Buffer, staging.Buffer, 1, &copy);
        });
        if (!toDevice) new Span<uint>((void*)staging.Mapped, data.Length).CopyTo(data);
        vk.DestroyBuffer(device, staging.Buffer, null);
        vk.FreeMemory(device, staging.Memory, null);
        owned.RemoveAt(owned.Count - 1);
    }

    void OneShot(Action<CommandBuffer> record)
    {
        var alloc = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
        Check(vk.AllocateCommandBuffers(device, &alloc, out var cmd), "one-shot");
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        vk.BeginCommandBuffer(cmd, &begin);
        record(cmd);
        vk.EndCommandBuffer(cmd);
        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
        Check(vk.QueueSubmit(queue, 1, &submit, default), "one-shot submit");
        vk.QueueWaitIdle(queue);
        vk.FreeCommandBuffers(device, pool, 1, &cmd);
    }

    void Upload(AgentState state)
    {
        uint[][] agents = [state.PosX, state.PosY, state.TgtX, state.TgtY, state.Info];
        for (int b = 0; b < 5; b++) Transfer(b, agents[b], true);
        Transfer(5, w.Settlements, true); Transfer(6, w.AdjOff, true);
        if (w.Adj.Length > 0) Transfer(7, w.Adj, true);
        Transfer(11, state.MaskLo, true); Transfer(12, state.MaskHi, true);
    }

    public AgentState Download()
    {
        WaitAll();
        var state = new AgentState(s.Agents);
        uint[][] agents = [state.PosX, state.PosY, state.TgtX, state.TgtY, state.Info];
        for (int b = 0; b < 5; b++) Transfer(b, agents[b], false);
        Transfer(11, state.MaskLo, false); Transfer(12, state.MaskHi, false);
        return state;
    }

    void WaitAll() { foreach (var slot in slots) if (slot.Busy) Wait(slot); }

    void Wait(Slot slot)
    {
        var fence = slot.Fence;
        Check(vk.WaitForFences(device, 1, &fence, true, ulong.MaxValue), "wait");
        slot.Busy = false;
    }

    public void Submit(TickInput input)
    {
        var slot = slots[input.Tick % slots.Length];
        if (slot.Busy || slot.Tick != uint.MaxValue) throw new InvalidOperationException($"Slot for tick {input.Tick} still holds tick {slot.Tick}; collect it first");
        clock.Restart();
        new Span<uint>(input.Rumor).CopyTo(new Span<uint>((void*)slot.Rumor.Mapped, input.Rumor.Length));
        var push = Push.For(s, w, input);
        var cmd = slot.Command;
        vk.ResetCommandBuffer(cmd, 0);
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        vk.BeginCommandBuffer(cmd, &begin);
        vk.CmdResetQueryPool(cmd, slot.Queries, 0, Timestamps);
        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.TopOfPipeBit, slot.Queries, 0);
        vk.CmdFillBuffer(cmd, buffers[8].Buffer, 0, Vk.WholeSize, 0);
        vk.CmdFillBuffer(cmd, buffers[9].Buffer, 0, Vk.WholeSize, uint.MaxValue);
        vk.CmdFillBuffer(cmd, buffers[16].Buffer, 0, Vk.WholeSize, 0);
        Barrier(cmd);
        var set = slot.Set;
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null);
        vk.CmdPushConstants(cmd, layout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(Push), &push);
        uint groups = (uint)((s.Agents + 255) / 256);
        Dispatch(cmd, move, groups, slot, 1);
        Dispatch(cmd, insert, groups, slot, 2);
        Dispatch(cmd, spread, groups, slot, 3);
        Copy(cmd, buffers[13], buffers[11]); Copy(cmd, buffers[14], buffers[12]);
        Copy(cmd, buffers[16], slot.Counters); Copy(cmd, buffers[8], slot.Agg); Copy(cmd, buffers[15], slot.Events);
        Barrier(cmd);
        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, slot.Queries, 4);
        vk.EndCommandBuffer(cmd);
        var fence = slot.Fence;
        vk.ResetFences(device, 1, &fence);
        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
        Check(vk.QueueSubmit(queue, 1, &submit, fence), "submit");
        slot.Busy = true; slot.Tick = input.Tick; slot.SubmittedAt = Stopwatch.GetTimestamp();
        LastTimingsMs["record+submit"] = clock.Elapsed.TotalMilliseconds;
    }

    void Dispatch(CommandBuffer cmd, Pipeline pipeline, uint groups, Slot slot, uint timestamp)
    {
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, pipeline);
        vk.CmdDispatch(cmd, groups, 1, 1);
        Barrier(cmd);
        vk.CmdWriteTimestamp(cmd, PipelineStageFlags.ComputeShaderBit, slot.Queries, timestamp);
    }

    void Copy(CommandBuffer cmd, Gpu from, Gpu to)
    {
        var copy = new BufferCopy { Size = Math.Min(from.Size, to.Size) };
        vk.CmdCopyBuffer(cmd, from.Buffer, to.Buffer, 1, &copy);
    }

    void Barrier(CommandBuffer cmd)
    {
        var barrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit | AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit | AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit | AccessFlags.HostReadBit
        };
        var stages = PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit;
        vk.CmdPipelineBarrier(cmd, stages, stages | PipelineStageFlags.HostBit, 0, 1, &barrier, 0, null, 0, null);
    }

    public Readback Collect(uint tick)
    {
        var slot = slots[tick % slots.Length];
        if (slot.Tick != tick) throw new InvalidOperationException($"No readback for tick {tick}");
        var fence = slot.Fence;
        double waitedMs = 0;
        if (slot.Busy && vk.GetFenceStatus(device, fence) != Result.Success)
        {
            long start = Stopwatch.GetTimestamp();
            Wait(slot);
            waitedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        slot.Busy = false;
        var r = new Readback { Tick = tick };
        new Span<uint>((void*)slot.Counters.Mapped, 130).CopyTo(r.Counters);
        new Span<uint>((void*)slot.Agg.Mapped, r.Agg.Length).CopyTo(r.Agg);
        int count = (int)Math.Min(r.Counters[0], s.EventCapacity);
        var raw = new Span<uint>((void*)slot.Events.Mapped, 2 * count);
        r.Events = new ulong[count];
        for (int i = 0; i < count; i++) r.Events[i] = ((ulong)raw[2 * i] << 6) | raw[2 * i + 1];
        Array.Sort(r.Events);
        ulong* stamps = stackalloc ulong[Timestamps];
        vk.GetQueryPoolResults(device, slot.Queries, 0, Timestamps, (nuint)(8 * Timestamps), stamps, 8, QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit);
        double Ms(int from, int to) => (stamps[to] - stamps[from]) * timestampPeriod / 1e6;
        LastTimingsMs["gpu.clear+move"] = Ms(0, 1); LastTimingsMs["gpu.insert"] = Ms(1, 2); LastTimingsMs["gpu.spread"] = Ms(2, 3);
        LastTimingsMs["gpu.copies"] = Ms(3, 4); LastTimingsMs["gpu.total"] = Ms(0, 4);
        LastTimingsMs["collect.wait"] = waitedMs;
        slot.Tick = uint.MaxValue;
        return r;
    }

    // Round trip of one tick: submit, wait for the fence, read the counters.
    public (double Ms, Readback Result) RoundTrip(TickInput input)
    {
        long start = Stopwatch.GetTimestamp();
        Submit(input);
        var r = Collect(input.Tick);
        return (Stopwatch.GetElapsedTime(start).TotalMilliseconds, r);
    }

    static void Check(Result result, string what)
    {
        if (result != Result.Success) throw new InvalidOperationException($"Vulkan {what} failed: {result}");
    }

    public void Dispose()
    {
        if (device.Handle == 0) return;
        vk.DeviceWaitIdle(device);
        foreach (var slot in slots.Where(x => x is not null))
        {
            vk.DestroyFence(device, slot.Fence, null);
            vk.DestroyQueryPool(device, slot.Queries, null);
        }
        foreach (var p in new[] { move, insert, spread }) vk.DestroyPipeline(device, p, null);
        vk.DestroyPipelineLayout(device, layout, null);
        vk.DestroyDescriptorPool(device, descriptorPool, null);
        vk.DestroyDescriptorSetLayout(device, setLayout, null);
        foreach (var (buffer, memory) in owned) { vk.DestroyBuffer(device, buffer, null); vk.FreeMemory(device, memory, null); }
        vk.DestroyCommandPool(device, pool, null);
        vk.DestroyDevice(device, null);
        vk.DestroyInstance(instance, null);
        device = default;
    }
}
