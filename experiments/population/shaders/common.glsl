// Shared by all kernels. Integer-only so results match the C# reference bit for bit (Sim.cs).
layout(local_size_x = 256) in;

layout(push_constant) uniform Params {
    uint agents, settlements, tick, seed;
    uint stepHuman, stepZombie, wanderRadius, zombieSpread;
    uint travelThreshold, hordePeriod, worldMax, regionShift;
    uint regionsPerAxis, regionCount, tableMask, contactRadius;
    uint fadeThreshold, activeLo, activeHi, seedCount;
    uint eventCapacity;
} p;

layout(std430, binding = 0) buffer PosX { uint posX[]; };
layout(std430, binding = 1) buffer PosY { uint posY[]; };
layout(std430, binding = 2) buffer TgtX { uint tgtX[]; };
layout(std430, binding = 3) buffer TgtY { uint tgtY[]; };
layout(std430, binding = 4) buffer Info { uint info[]; };            // kind:1 mode:1 faction:6 anchor:24
layout(std430, binding = 5) buffer Settle { uint settle[]; };        // x0,y0,x1,y1...
layout(std430, binding = 6) buffer AdjOff { uint adjOff[]; };
layout(std430, binding = 7) buffer Adj { uint adj[]; };
layout(std430, binding = 8) buffer Agg { uint agg[]; };              // faction * regionCount + region
layout(std430, binding = 9) buffer Heads { uint heads[]; };
layout(std430, binding = 10) buffer Next { uint nextAgent[]; };
layout(std430, binding = 11) buffer MaskLo { uint maskLo[]; };
layout(std430, binding = 12) buffer MaskHi { uint maskHi[]; };
layout(std430, binding = 13) buffer NextLo { uint nextLo[]; };
layout(std430, binding = 14) buffer NextHi { uint nextHi[]; };
layout(std430, binding = 15) buffer Events { uint events[]; };       // agent, slot pairs
layout(std430, binding = 16) buffer Counters { uint counters[]; };   // 0 events, 1 overflow, 2+ carriers, 66+ heard
layout(std430, binding = 17) buffer Rumor { uint rumor[]; };         // 0-63 credibility16, 64-127 trust16 (8x8), 128+ seeds (x, y, radius256, slot)

const uint NONE = 0xFFFFFFFFu;

uint mix32(uint x) {
    x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
    return x;
}
uint hash4(uint a, uint b, uint c, uint d) { return mix32(a ^ mix32(b ^ mix32(c ^ mix32(d)))); }

uint isqrt(uint n) {
    uint res = 0u, bit = 1u << 30;
    while (bit > n) bit >>= 2;
    while (bit != 0u) {
        if (n >= res + bit) { n -= res + bit; res = (res >> 1) + bit; } else res >>= 1;
        bit >>= 2;
    }
    return res;
}

uint cellOf(uint cx, uint cy) { return mix32(cx * 0x9E3779B1u ^ mix32(cy)) & p.tableMask; }
uint cellHash(uint x, uint y) { return cellOf(x >> 16, y >> 16); }
