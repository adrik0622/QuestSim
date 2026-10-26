"""Populated city on a private Xvfb display: a graphical client leaves the starter room and sees NPCs in the street."""
import os
import shutil
import sys
import time
from verify_multiplayer import NetworkFixture, wait, atomic, ROOT

if os.environ.get('QUESTSIM_XVFB') != '1':
    os.environ['QUESTSIM_XVFB'] = '1'
    os.execvp('xvfb-run', ['xvfb-run', '-a', '-s', '-screen 0 1280x720x24', sys.executable, __file__])

EVIDENCE = ROOT / 'src/QuestSim.Game/evidence/nightly'
XVFB_WARNING = 'WARNING: Could not set V-Sync mode, as changing V-Sync mode is not supported by the graphics driver.'
f = NetworkFixture('city-screen'); f.population = 200_000
shots = []


def me():
    s = f.status('alice')
    return next(a for a in s['snapshot']['Actors'] if a['Id'] == s['ownId'])


def capture(name):
    path = f.directory / (name + '.png')
    f.command('alice', capture=str(path))
    wait(lambda: path.exists(), name)
    shots.append(path); print('CAPTURE', path, flush=True)


try:
    f.launch('server'); f.ready('server', 0)
    f.launch('alice', 'alice', headless=False, intent=True); f.ready('alice')
    f.command('alice', 1, 0); wait(lambda: me()['X'] > 2.8, 'door column', 15)
    f.command('alice'); time.sleep(.3)
    if me()['X'] > 3.4:   # a busy machine can overshoot toward the door jamb (x 4): step back into the gap
        f.command('alice', -1, 0); wait(lambda: me()['X'] < 3.3, 'back into the doorway', 10); f.command('alice'); time.sleep(.2)
    f.command('alice', 0, 1); wait(lambda: me()['Z'] > 30, 'into the plaza', 20)
    f.command('alice', holdKeys=['Down']); time.sleep(.9)
    f.command('alice'); time.sleep(1.5)
    npcs = [a for a in f.status('alice')['snapshot']['Actors'] if a['Id'].startswith('npc:')]
    assert npcs, 'no NPCs visible'
    capture('city-npcs')
    f.command('alice', keys=['C']); time.sleep(.6)
    capture('city-npcs-first-person')
    print(f'PASS a graphical client in the plaza renders the city with {len(npcs)} nearby NPCs', flush=True)
    settlements = f.status('alice')['snapshot']['Settlements']
    assert settlements and len(settlements) > 100 and all(len(s['Days10']) == 4 for s in settlements)
    f.command('alice', keys=['M']); time.sleep(.6)
    capture('city-map')
    f.command('alice', keys=['M']); time.sleep(.3)
    print(f'PASS M opens the debug map of {len(settlements)} settlements (stock, order, shortages, factions)', flush=True)
    f.close('alice'); wait(lambda: f.status('server')['active'] == 0, 'absent'); f.close('server', server=True)
    for label in f.processes:
        text = (f.directory / (label + '.log')).read_text().replace(XVFB_WARNING, '')
        assert 'ERROR:' not in text, (label, text)
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    for path in shots: shutil.copy2(path, EVIDENCE / path.name)
    atomic(EVIDENCE / 'city-screen.json', {'npcs': len(npcs), 'population': f.population})
finally:
    f.cleanup()
