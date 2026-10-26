"""Data-defined content over real ENet: mod starting kit, using a consumable, and refusing a world whose mod is missing."""
from verify_multiplayer import NetworkFixture, wait, read

f = NetworkFixture('content')
try:
    f.launch('server'); f.ready('server', 0)
    f.launch('alice', 'alice'); f.ready('alice', 1)
    inventory = f.status('alice')['own']['Inventory']
    assert inventory.get('rations:canned-beans') == 2, inventory
    f.command('alice', use=True)
    wait(lambda: f.status('alice')['lastInteract'] == 'used' and f.status('alice')['own']['Inventory'].get('rations:canned-beans') == 1, 'use reply')
    print('PASS the example mod adds canned beans to the starting kit; U consumes one over the network', flush=True)
    f.close('alice'); wait(lambda: f.status('server')['active'] == 0, 'absent'); f.close('server', server=True)
    saved = read(f.save)
    assert [m['Id'] for m in saved['Mods']] == ['questsim', 'rations']
    before = f.save.read_bytes()
    empty = f.directory / 'no-mods'; empty.mkdir()
    f.server_args = ['--mods=' + str(empty)]
    process = f.launch('server-without-mod')
    assert process.wait(timeout=20) == 1
    assert 'needs missing mods: rations' in (f.directory / 'server-without-mod.log').read_text()
    assert f.save.read_bytes() == before
    print('PASS a world made with a mod refuses to start without it, leaving the save byte-identical', flush=True)
    print('EVIDENCE', f.directory, flush=True)
finally:
    f.cleanup()
