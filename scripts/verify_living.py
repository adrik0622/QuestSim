"""Living world over real ENet: population tiers, per-player NPC visibility, pistol combat, persisted deaths, 1M-agent tick budget."""
import math
import time
from verify_multiplayer import NetworkFixture, wait, read, atomic

checks = []


def passed(text):
    checks.append(text); print('PASS', text, flush=True)


def me(f, label):
    s = f.status(label)
    return next(a for a in s['snapshot']['Actors'] if a['Id'] == s['ownId'])


def npcs(f, label):
    return [a for a in f.status(label)['snapshot']['Actors'] if a['Id'].startswith('npc:')]


def run_combat():
    f = NetworkFixture('living'); f.population = 100_000
    try:
        f.launch('server'); f.ready('server', 0)
        f.launch('alice', 'alice'); f.ready('alice')
        wait(lambda: f.status('server')['npcs'] and npcs(f, 'alice'), 'NPCs promoted near alice', 20)
        mine = me(f, 'alice')
        assert all(math.hypot(n['X'] - mine['X'], n['Z'] - mine['Z']) <= 121 for n in npcs(f, 'alice'))
        assert f.status('alice')['own']['Inventory'].get('questsim:ammo-9mm') == 24
        passed(f"NPCs near the player are promoted and only nearby ones reach the client ({len(npcs(f, 'alice'))} visible)")
        killed, shots, results = None, 0, []
        while killed is None and shots < 24:
            mine = me(f, 'alice')
            target = min(npcs(f, 'alice'), key=lambda n: math.hypot(n['X'] - mine['X'], n['Z'] - mine['Z']))
            heading = math.atan2(-(target['X'] - mine['X']), -(target['Z'] - mine['Z']))
            f.command('alice', 0, 0, heading=heading); time.sleep(.25)
            f.command('alice', 0, 0, heading=heading, attack='shoot'); shots += 1
            wait(lambda: (r := f.status('alice')['lastAttack']) and f.status('alice')['own']['Inventory'].get('questsim:ammo-9mm', 0) == 24 - shots, 'shot reply')
            results.append(f.status('alice')['lastAttack']['Reason'])
            if f.status('alice')['lastAttack']['Killed']: killed = f.status('alice')['lastAttack']['TargetId']
            time.sleep(.45)
        assert killed, results
        wait(lambda: all(a['Id'] != killed for a in f.status('alice')['snapshot']['Actors']), 'dead NPC removed from snapshot')
        passed(f'aimed pistol shots spend one round each and kill an NPC ({shots} shots: {", ".join(results)})')
        # Leave the starter room (east through the partition, south out of the door), then ask the nearest friendly NPC.
        f.command('alice', 1, 0); wait(lambda: me(f, 'alice')['X'] > 2.8, 'reach door column', 15)
        f.command('alice', 0, 1); wait(lambda: me(f, 'alice')['Z'] > 8, 'out of the room', 15)
        def friendly():
            mine = me(f, 'alice')
            people = [n for n in npcs(f, 'alice') if n['Kind'] == 1 and n['Faction'] != 2]
            return min(people, key=lambda n: math.hypot(n['X'] - mine['X'], n['Z'] - mine['Z'])) if people else None
        deadline = time.monotonic() + 40
        while time.monotonic() < deadline:
            target, mine = friendly(), me(f, 'alice')
            if target is None: time.sleep(.3); continue
            dx, dz = target['X'] - mine['X'], target['Z'] - mine['Z']; d = math.hypot(dx, dz)
            if d < 2.2: break
            f.command('alice', dx / d, dz / d); time.sleep(.2)
        f.command('alice'); time.sleep(.3)
        f.command('alice', ask=True)
        answer = wait(lambda: (a := f.status('alice')['lastAsk']) and a, 'ask reply')
        assert answer['Reason'] in ('rumors', 'nothing') and answer['Speaker'], answer
        passed(f"walking up to an NPC and asking returns what that NPC has heard ({answer['Speaker']}: {answer['Lines'][0]})")
        f.close('alice'); wait(lambda: f.status('server')['active'] == 0, 'alice absent')
        deltas = f.status('server')['agentDeltas']
        f.close('server', server=True)
        assert any(d['Dead'] for d in read(f.save)['AgentDeltas'])
        f.launch('server-return'); f.ready('server-return', 0)
        assert f.status('server-return')['agentDeltas'] == deltas
        f.launch('alice-return', 'alice'); f.ready('alice-return')
        assert f.status('alice-return')['own']['Inventory'].get('questsim:ammo-9mm', 0) == 24 - shots
        assert all(a['Id'] != killed for a in f.status('alice-return')['snapshot']['Actors'])
        passed('the death and spent ammo survive a server restart (population regenerated from seed + deltas)')
        f.close('alice-return'); wait(lambda: f.status('server-return')['active'] == 0, 'absent'); f.close('server-return', server=True)
        print('EVIDENCE', f.directory, flush=True)
    finally:
        f.cleanup()


def run_budget():
    f = NetworkFixture('living-1m'); f.population = 1_000_000
    try:
        started = time.monotonic()
        f.launch('server'); f.ready('server', 0)
        startup = time.monotonic() - started
        f.launch('alice', 'alice'); f.ready('alice')
        t0 = f.status('server')['tick']; start = time.monotonic()
        f.command('alice', 1, 0)   # keep moving so tiers churn
        time.sleep(20)
        status = f.status('server')
        rate = (status['tick'] - t0) / (time.monotonic() - start)
        assert status['populationLagged'] == 0 and rate > 18, (status['populationLagged'], rate)
        passed(f"1,000,000 agents: server starts in {startup:.1f}s, holds {rate:.1f} ticks/s with the 1 Hz population tick never lagging; RSS {status['rssBytes'] / 2**20:.0f} MiB")
        atomic(f.directory / 'result.json', {'startupSeconds': startup, 'ticksPerSecond': rate, 'rssBytes': status['rssBytes'], 'npcs': status['npcs']})
        f.close('alice'); wait(lambda: f.status('server')['active'] == 0, 'absent'); f.close('server', server=True)
        print('EVIDENCE', f.directory, flush=True)
    finally:
        f.cleanup()


if __name__ == '__main__':
    run_combat()
    run_budget()
