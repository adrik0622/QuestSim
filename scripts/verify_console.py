"""Debug console over real ENet: server-checked admin rights from <save>.admins, world speed and always-on worlds."""
import time
from verify_multiplayer import NetworkFixture, wait, read

f = NetworkFixture('console')
f.population = 20000
try:
    f.launch('server'); f.ready('server', 0)
    f.launch('alice', 'alice'); f.ready('alice', 1)
    alice = f.status('alice')['ownId']

    def ask(line, expect):
        before = len(f.status('alice')['consoleLines'])
        f.command('alice', console=line)
        return wait(lambda: (lines := f.status('alice')['consoleLines'])[before:] and expect(lines[before:]) and lines[before:], line)

    def rate(label, seconds=2.0):
        start = f.status(label)['tick']; time.sleep(seconds)
        return (f.status(label)['tick'] - start) / seconds

    ask('speed 8', lambda new: 'needs admin' in new[0])
    assert f.status('server')['worldSpeed'] == 1
    print('PASS with no admins file the server refuses speed and the world stays at 1x', flush=True)

    (f.directory / 'world.json.admins').write_text(f'# QuestSim admins\n{alice}\n')
    ask('speed 8', lambda new: new == ['Speed set to 8x.'])
    wait(lambda: f.status('alice')['worldSpeed'] == 8, 'speed reaches the client')
    measured = rate('server')
    assert measured > 5 * 20, measured
    print(f'PASS after the host lists her character ID, the player sets 8x and the world runs {measured / 20:.1f}x ({measured:.0f} ticks/s)', flush=True)
    time_line = ask('time', lambda new: new[0].startswith('Day '))[0]
    ask('speed 1', lambda new: new == ['Speed set to 1x.'])
    assert f'CONSOLE actor={alice} command=speed 8 reply=Speed set to 8x.' in (f.directory / 'server.log').read_text()
    print(f'PASS time answers ({time_line!r}) and every command is logged with the actor', flush=True)

    ask('alwayson on', lambda new: new[0].startswith('Always-on on'))
    f.close('alice'); wait(lambda: f.status('server')['active'] == 0, 'alice left')
    left = f.status('server')['tick']
    wait(lambda: f.status('server')['tick'] > left + 40, 'time runs with nobody connected')
    f.close('server', server=True)
    saved = read(f.save)['Tick']
    assert saved > left + 40, (saved, left)
    print(f'PASS always-on keeps the world running with nobody connected and saves it (tick {left} -> {saved})', flush=True)

    f.launch('paused'); f.ready('paused', 0)
    tick = f.status('paused')['tick']; time.sleep(1.5)
    assert f.status('paused')['tick'] == tick and f.status('paused')['alwaysOn'] is False
    f.close('paused', server=True)
    f.server_args = ['--always-on']
    f.launch('always'); f.ready('always', 0)
    tick = f.status('always')['tick']
    wait(lambda: f.status('always')['tick'] > tick + 20, 'always-on flag')
    f.close('always', server=True)
    print('PASS an empty world is paused by default after a restart; --always-on starts it running', flush=True)
    print('EVIDENCE', f.directory, flush=True)
finally:
    f.cleanup()
