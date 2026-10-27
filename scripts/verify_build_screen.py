"""Q02b screen test: real graphical clients on a private Xvfb display, never the user's desktop.
Keys go through Godot's own input pipeline (Input.ParseInputEvent via the intent file);
viewport PNGs are captured by the client itself for inspection.
"""
import os
from pathlib import Path
import shutil
import sys
import threading
import time
from verify_multiplayer import NetworkFixture, wait, read, atomic, ROOT

if os.environ.get('QUESTSIM_XVFB') != '1':
    os.environ['QUESTSIM_XVFB'] = '1'
    os.execvp('xvfb-run', ['xvfb-run', '-a', '-s', '-screen 0 1280x720x24', sys.executable, __file__])

SITE = 'questsim:build-cell-1'
PANEL = 'questsim:wall-panel'
EVIDENCE = ROOT / 'src/QuestSim.Game/evidence/q02b'
# Software GL on Xvfb cannot change V-Sync; this line is expected only on the virtual display.
XVFB_WARNING = 'WARNING: Could not set V-Sync mode, as changing V-Sync mode is not supported by the graphics driver.'

f = NetworkFixture('build-screen'); checks = []; shots = []


def passed(message):
    checks.append(message); print('PASS', message, flush=True)


def press(label, *keys):
    f.command(label, keys=list(keys))


def capture(label, name):
    path = f.directory / (name + '.png')
    f.command(label, capture=str(path))
    wait(lambda: path.exists(), name + ' capture')
    shots.append(path); print('CAPTURE', path, flush=True)


def replies(label, kind):
    return (f.directory / (label + '.log')).read_text().count(kind)


def inventory(label):
    return f.status(label)['own']['Inventory']


try:
    f.launch('server'); f.ready('server', 0)
    for label in ['alice', 'bob']:
        f.launch(label, label, headless=False, intent=True); f.ready(label)
    wait(lambda: all(f.status(label)['active'] == 2 for label in ['alice', 'bob']), 'both graphical clients see two players')
    assert all(f.status(label)['display'] == 'X11' for label in ['alice', 'bob'])
    capture('alice', 'build-start')
    press('alice', 'F')
    wait(lambda: (p := f.status('alice')['lastPickup']) and p['Success'] and p['ItemId'] == 'questsim:bandage', 'alice F takes bandage')
    press('bob', 'F')
    wait(lambda: (p := f.status('bob')['lastPickup']) and p['Success'] and p['ItemId'] == PANEL, 'bob F skips empty crate for panel')
    press('alice', 'F')
    wait(lambda: inventory('alice').get(PANEL) == 1, 'alice second F takes panel')
    assert inventory('bob')[PANEL] == 1 and read(f.save)['Supplies'][1]['Count'] == 0
    passed('real F key events take the nearest stocked crate in reach for each player')
    barrier = threading.Barrier(2)
    def build(label):
        barrier.wait(timeout=3); press(label, 'B')
    threads = [threading.Thread(target=build, args=(label,)) for label in ['alice', 'bob']]
    for t in threads: t.start()
    for t in threads: t.join()
    results = {label: wait(lambda label=label: f.status(label)['lastBuild'], label + ' B reply') for label in ['alice', 'bob']}
    winner = next(label for label, r in results.items() if r['Success'])
    loser = next(label for label, r in results.items() if not r['Success'])
    assert results[loser]['Reason'] == 'occupied' and inventory(winner).get(PANEL) == 0 and inventory(loser)[PANEL] == 1
    wait(lambda: all(any(b['Id'] == SITE for b in f.status(label)['snapshot']['Blocks']) for label in ['alice', 'bob']), 'wall in both snapshots')
    passed('real B key race places one wall; loser keeps its panel')
    capture(winner, 'build-winner'); capture(loser, 'build-loser')
    press('alice', 'T'); press('bob', 'T')
    wait(lambda: all(replies(label, 'BUILD_REPLY') >= 2 for label in ['alice', 'bob']), 'T build replays')
    assert all(f.status(label)['lastBuild'] == results[label] for label in ['alice', 'bob'])
    assert len(read(f.save)['BuildReceipts']) == 2 and inventory(winner).get(PANEL) == 0
    passed('real T key replays each player\'s last build without another wall or cost')
    press(winner, 'C'); time.sleep(.5)  # One-slot intent file: let C be read and rendered before capturing.
    capture(winner, 'build-first-person')
    press(winner, 'C'); time.sleep(.5)
    # Walk Alice east through the partition doorway, then south out of the room's door into the city plaza.
    def position(label):
        status = f.status(label)
        return next(a for a in status['snapshot']['Actors'] if a['Id'] == status['ownId'])
    f.command('alice', 1, 0)
    wait(lambda: position('alice')['X'] > 2.8, 'alice reaches the south door column', 15)
    f.command('alice', 0, 1)
    wait(lambda: position('alice')['Z'] > 25, 'alice walks out into the plaza', 20)
    f.command('alice', holdKeys=['Down']); time.sleep(.9)   # tilt the camera toward the horizon
    f.command('alice'); time.sleep(.4)
    capture('alice', 'city-outside')
    passed('a player can leave the starter room through its door and see the generated city')
    # Debug console: the grave key opens it, game keys are ignored while it is open, and it closes again.
    replies_before = replies('bob', 'ITEM_REPLY')
    press('bob', 'Quoteleft')
    wait(lambda: f.status('bob')['consoleOpen'] is True, 'grave key opens the console')
    press('bob', 'F'); f.command('bob', console='help')
    wait(lambda: any(l.startswith('time') for l in f.status('bob')['consoleLines']), 'help reply')
    capture('bob', 'console-open')
    assert replies('bob', 'ITEM_REPLY') == replies_before
    press('bob', 'Quoteleft')
    wait(lambda: f.status('bob')['consoleOpen'] is False, 'grave key closes the console')
    passed('the grave key opens and closes the debug console; F does nothing while it is open; help answers')
    for label in ['alice', 'bob']: f.close(label)
    wait(lambda: f.status('server')['active'] == 0, 'clients absent')
    f.close('server', server=True)
    for label in f.processes:
        text = (f.directory / (label + '.log')).read_text().replace(XVFB_WARNING, '')
        assert 'ERROR:' not in text and 'WARNING:' not in text, (label, text)
    passed('graphical clients and server log no errors (Xvfb V-Sync notice excluded)')
    EVIDENCE.mkdir(exist_ok=True)
    for path in shots: shutil.copy2(path, EVIDENCE / path.name)
    atomic(EVIDENCE / 'screen-result.json', {'checks': checks, 'winner': winner, 'display': 'Xvfb 1280x720x24, Mesa llvmpipe',
                                             'input': 'Input.ParseInputEvent via intent file', 'raw': str(f.directory.relative_to(ROOT))})
    print('EVIDENCE', f.directory, flush=True)
finally:
    f.cleanup()
