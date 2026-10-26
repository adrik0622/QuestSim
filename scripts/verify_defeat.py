"""Defeat outcomes over real ENet: captivity, teammate rescue with a single-claim reward, infection turning a player.
Captive/infected states are offline save fixtures applied while every process is stopped (never live injection)."""
import time
from verify_multiplayer import NetworkFixture, wait, read, atomic

checks = []


def passed(text):
    checks.append(text); print('PASS', text, flush=True)


def me(f, label):
    s = f.status(label)
    return next(a for a in s['snapshot']['Actors'] if a['Id'] == s['ownId'])


def stop_all(f, labels, server):
    for label in labels: f.close(label)
    wait(lambda: f.status(server)['active'] == 0, 'everyone absent')
    f.close(server, server=True)


f = NetworkFixture('defeat')
try:
    f.launch('server'); f.ready('server', 0)
    f.launch('alice', 'alice'); f.ready('alice', 1)
    f.launch('bob', 'bob'); f.ready('bob', 2)
    alice_id, bob_id = f.status('alice')['ownId'], f.status('bob')['ownId']
    stop_all(f, ['alice', 'bob'], 'server')
    world = read(f.save)
    alice = next(c for c in world['Characters'] if c['Id'] == alice_id)
    alice['Conditions'] = {'questsim:captive': 100000}
    world['Opportunities'] = [{'Id': 0, 'Kind': 'rescue', 'Subject': alice_id, 'Target': alice_id, 'RewardItem': 'questsim:ammo-9mm',
                               'RewardQuantity': 6, 'Status': 'open', 'ClaimedBy': '', 'CreatedTick': world['Tick'], 'Text': 'Alice is held. Free her.'}]
    atomic(f.save, world)

    f.launch('server-2'); f.ready('server-2', 0)
    f.launch('alice-2', 'alice'); f.ready('alice-2', 1)
    f.launch('bob-2', 'bob'); f.ready('bob-2', 2)
    wait(lambda: me(f, 'alice-2')['Condition'] == 'captive' and f.status('bob-2')['snapshot']['Opportunities'], 'captive state and opportunity visible')
    before = me(f, 'alice-2')
    f.command('alice-2', 1, 0); time.sleep(1); f.command('alice-2')
    after = me(f, 'alice-2')
    assert (before['X'], before['Z']) == (after['X'], after['Z'])
    passed('a captive player cannot move, and every client sees the rescue opportunity')
    f.command('bob-2', interact='rescue')
    wait(lambda: f.status('bob-2')['lastInteract'] == 'rescued', 'rescue reply')
    wait(lambda: me(f, 'alice-2')['Condition'] == '', 'alice freed')
    assert f.status('bob-2')['own']['Inventory']['questsim:ammo-9mm'] == 30
    f.command('bob-2', interact='rescue')
    wait(lambda: f.status('bob-2')['lastInteract'] == 'nobody', 'second rescue attempt')
    saved = read(f.save)
    assert saved['Opportunities'][0]['Status'] == 'claimed' and saved['Opportunities'][0]['ClaimedBy'] == bob_id
    assert sum(1 for t in saved['Transfers'] if t['Reason'] == 'reward:0') == 1
    passed('a teammate in reach frees the captive over the network; the reward is paid and saved exactly once')

    stop_all(f, ['alice-2', 'bob-2'], 'server-2')
    world = read(f.save)
    next(c for c in world['Characters'] if c['Id'] == alice_id)['Conditions'] = {'questsim:infected': 30}
    atomic(f.save, world)
    f.launch('server-3'); f.ready('server-3', 0)
    f.launch('alice-3', 'alice'); f.ready('alice-3', 1)
    wait(lambda: 'RETIRED' in (f.directory / 'alice-3.log').read_text(), 'infection turns alice', 15)
    wait(lambda: (s := read(f.save)) and any(c['Id'] == alice_id and c['Status'] == 'turned' for c in s['Characters']), 'turned saved')
    saved = read(f.save)
    assert any(k['Label'] == 'Remains of alice' and 'questsim:pistol' in k['Inventory'] for k in saved['Caches'])
    process, output = f.processes['alice-3']   # a retired client stops reading intents; end it like a closed window
    process.terminate(); process.wait(timeout=5); output.close()
    wait(lambda: f.status('server-3')['active'] == 0, 'turned player disconnected')
    f.launch('alice-4', 'alice'); f.ready('alice-4', 1)
    assert f.status('alice-4')['ownId'] != alice_id and f.status('alice-4')['own']['Inventory']['questsim:pistol'] == 1
    passed('an infection that runs out turns the player: disconnected with a message, gear left in remains, new character on rejoin')
    stop_all(f, ['alice-4'], 'server-3')
    for label in f.processes:
        text = (f.directory / (label + '.log')).read_text()
        assert 'ERROR:' not in text, (label, text)
    atomic(f.directory / 'result.json', {'checks': checks})
    print('EVIDENCE', f.directory, flush=True)
finally:
    f.cleanup()
