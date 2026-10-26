"""Eight real headless clients against a 1M-agent server: tick rate, population lag, NPC detail, snapshot bandwidth.
Loopback only: this measures server and protocol cost, not WAN behaviour."""
import json
import math
import time
from verify_multiplayer import NetworkFixture, wait, atomic

PLAYERS = 8
f = NetworkFixture('load'); f.population = 1_000_000
try:
    f.launch('server'); f.ready('server', 0)
    labels = [f'p{i}' for i in range(PLAYERS)]
    for i, label in enumerate(labels):
        f.launch(label, label); f.ready(label)
    wait(lambda: f.status('server')['active'] == PLAYERS, 'all players joined', 30)
    # Leave the starter room through its door, then fan out across the plaza and beyond.
    for label in labels: f.command(label, 1, 0)
    time.sleep(3)
    for label in labels: f.command(label, 0, 1)
    time.sleep(4)
    for i, label in enumerate(labels):
        angle = 2 * math.pi * i / PLAYERS
        f.command(label, math.cos(angle), math.sin(angle))
    t0, start = f.status('server')['tick'], time.monotonic()
    sizes, npc_counts, json_sizes = [], [], []
    world_start = {label: f.status(label)['worldBytes'] for label in labels}
    for _ in range(25):
        time.sleep(1)
        for label in labels:
            status = f.status(label)
            if status['snapshot']:
                sizes.append(status['actorPacketBytes'])
                json_sizes.append(len(json.dumps(status['snapshot']['Actors'])))
                npc_counts.append(sum(1 for a in status['snapshot']['Actors'] if a['Id'].startswith('npc:')))
    world_rate = sum(f.status(label)['worldBytes'] - world_start[label] for label in labels) / len(labels) / (time.monotonic() - start)
    status = f.status('server')
    rate = (status['tick'] - t0) / (time.monotonic() - start)
    mean = sum(sizes) / len(sizes)
    result = {'players': PLAYERS, 'population': f.population, 'ticksPerSecond': round(rate, 2), 'populationLagged': status['populationLagged'],
              'detailedNpcs': status['npcs'], 'meanVisibleNpcs': round(sum(npc_counts) / len(npc_counts), 1),
              'meanActorPacketBytes': round(mean), 'meanActorsAsJsonBytes': round(sum(json_sizes) / len(json_sizes)),
              'worldJsonBytesPerSecond': round(world_rate), 'approxDownloadKiBps': round((mean * 10 + world_rate) / 1024, 1),
              'serverRssMiB': round(status['rssBytes'] / 2**20)}
    print(json.dumps(result), flush=True)
    assert rate > 18 and status['populationLagged'] == 0, result
    print(f"PASS {PLAYERS} players and 1,000,000 agents: {rate:.1f} ticks/s, no population lag, {status['npcs']} detailed NPCs, "
          f"~{result['approxDownloadKiBps']} KiB/s per client (binary actors {result['meanActorPacketBytes']} B vs {result['meanActorsAsJsonBytes']} B as JSON)", flush=True)
    atomic(f.directory / 'result.json', result)
    for label in labels: f.close(label)
    wait(lambda: f.status('server')['active'] == 0, 'all absent', 20)
    f.close('server', server=True)
    print('EVIDENCE', f.directory, flush=True)
finally:
    f.cleanup()
