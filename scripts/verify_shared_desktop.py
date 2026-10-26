"""Two actual game windows, real X11 keys, shared authoritative state and reconnect."""
import json
import time
from desktop_test import QuestSimWindow, OUT
from verify_multiplayer import NetworkFixture, actor, wait, atomic

f = NetworkFixture('desktop')
windows = []
checks = []


def launch(label, profile):
    window = QuestSimWindow('shared-' + label, '--profile=' + profile, '--port=' + str(f.port),
                            '--profiles-dir=' + str(f.profiles), '--status=' + str(f.directory / (label + '.json')), game=True)
    windows.append(window)
    return window


def client_ready(label, count):
    return wait(lambda: (s := f.status(label)) and s['ownId'] and s['snapshot'] and s['active'] == count and s, label + ' rendered client ready')


def passed(message):
    checks.append(message)
    print('PASS', message, flush=True)


try:
    f.launch('server'); f.ready('server', 0)
    a = launch('alice', 'alice'); sa = client_ready('alice', 1); aid = sa['ownId']
    b = launch('bob', 'bob'); sb = client_ready('bob', 2); bid = sb['ownId']
    wait(lambda: f.status('alice')['active'] == 2, 'both windows see two players')
    print(a.screenshot('shared-alice-before'), flush=True)
    print(b.screenshot('shared-bob-before'), flush=True)
    passed('two owned rendered windows show two shared characters')
    start_x = actor(f.status('alice'), aid)['X']
    a.key('w', .8)
    wait(lambda: actor(f.status('bob'), aid)['X'] - start_x > 1, 'keyboard movement reaches observer')
    time.sleep(.4)
    wait(lambda: f.status('alice')['snapshot']['Actors'] == f.status('bob')['snapshot']['Actors'], 'rendered clients converge after input')
    print(a.screenshot('shared-alice-moved'), flush=True)
    print(b.screenshot('shared-bob-observes-alice'), flush=True)
    b.key('d', .6)
    wait(lambda: actor(f.status('alice'), bid)['Z'] > .8, 'second keyboard movement reaches first client')
    time.sleep(.3)
    print(a.screenshot('shared-alice-observes-bob'), flush=True)
    print(b.screenshot('shared-bob-moved'), flush=True)
    passed('WASD in each process moves its own character and the other client observes it')
    a.key('c'); print(a.screenshot('shared-alice-first-person'), flush=True)
    old_heading = actor(f.status('bob'), aid)['Heading']
    a.key('q', .35); a.key('Up', .2)
    wait(lambda: abs(actor(f.status('bob'), aid)['Heading'] - old_heading) > .1, 'turning replicated')
    print(a.screenshot('shared-alice-look'), flush=True)
    a.key('c'); a.key('r')
    passed('first/third camera switching and turning work; heading reaches observer')
    metrics = {'server_rss_bytes':f.status('server')['rssBytes'], 'rendered_clients_rss_bytes':[f.status('alice')['rssBytes'],f.status('bob')['rssBytes']],
               'rendered_clients_fps':[f.status('alice')['fps'],f.status('bob')['fps']], 'viewport':'960x540', 'max_fps':60, 'build':'Debug'}
    before_return = actor(f.status('alice'), bid)
    b.close()
    wait(lambda: f.status('alice')['active'] == 1, 'closed client absent from observer', 10)
    print(a.screenshot('shared-alice-after-bob-leaves'), flush=True)
    passed('closing one real window removes its character from the other')
    b2 = launch('bob-return', 'bob'); restored = client_ready('bob-return', 2)
    assert restored['ownId'] == bid
    wait(lambda: f.status('alice')['active'] == 2, 'reconnected character visible')
    after_return = actor(restored, bid)
    assert abs(after_return['X'] - before_return['X']) < .01 and abs(after_return['Z'] - before_return['Z']) < .01
    print(a.screenshot('shared-alice-after-bob-returns'), flush=True)
    print(b2.screenshot('shared-bob-return'), flush=True)
    passed('same profile reconnects with same character and safe last position in both windows')
    a.close(); b2.close()
    wait(lambda: f.status('server')['active'] == 0, 'all rendered clients absent', 10)
    tick = f.status('server')['tick']; time.sleep(.5)
    assert f.status('server')['tick'] == tick
    passed('closing both windows pauses world clock')
    f.close('server', server=True)
    for label in ['alice','bob','bob-return']:
        text = (OUT / ('shared-' + label + '.log')).read_text()
        assert 'ERROR:' not in text and 'WARNING:' not in text, text
    metrics['checks'] = checks
    atomic(f.directory / 'result.json', metrics)
    print('EVIDENCE', f.directory, flush=True)
finally:
    for window in windows:
        if not window.log.closed:
            window.close()
    f.cleanup()
