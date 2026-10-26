"""Real ENet integration: one dedicated server, independent headless clients.
Intent files feed the same client RPC path as keyboard input; no server-state injection.
World edits/metadata fixtures are applied only while every process is stopped.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / '.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64'


def read(path):
    try:
        return json.loads(path.read_text())
    except (FileNotFoundError, json.JSONDecodeError):
        return None


def wait(predicate, description, timeout=10):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(.05)
    raise AssertionError('Timeout: ' + description)


def atomic(path, value):
    tmp = path.with_suffix(path.suffix + '.writing')
    tmp.write_text(json.dumps(value))
    tmp.replace(path)


class NetworkFixture:
    def __init__(self, tag='network'):
        self.directory = ROOT / '.artifacts/q01' / (tag + '-' + str(time.time_ns()))
        self.directory.mkdir(parents=True)
        self.processes = {}
        self.serials = {}
        self.startups = {}
        self.started = {}
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
            probe.bind(('127.0.0.1', 0))
            self.port = probe.getsockname()[1]
        self.save = self.directory / 'world.json'
        self.profiles = self.directory / 'profiles'

    def launch(self, label, profile=None, headless=True, profiles=None, save=None):
        output = (self.directory / (label + '.log')).open('w')
        args = [str(ENGINE)] + (['--headless'] if headless else []) + ['--path', str(ROOT / 'src/QuestSim.Game'), '--',
                '--port=' + str(self.port), '--status=' + str(self.directory / (label + '.json'))]
        if profile:
            args += ['--profile=' + profile, '--profiles-dir=' + str(profiles or self.profiles)]
            if headless:
                args += ['--intent-file=' + str(self.directory / (label + '-intent.json'))]
        else:
            args += ['--server', '--save=' + str(save or self.save), '--shutdown-file=' + str(self.directory / (label + '-stop'))]
        started = time.monotonic()
        process = subprocess.Popen(args, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
        self.processes[label] = (process, output)
        self.started[label] = started
        return process

    def status(self, label):
        return read(self.directory / (label + '.json'))

    def ready(self, label, count=None):
        def condition():
            status = self.status(label)
            return status if status and status['snapshot'] and (count is None or len(status['snapshot']['Actors']) == count) and (status['role'] == 'server' or status['ownId']) else None
        value = wait(condition, label + ' ready')
        self.startups[label] = round(time.monotonic() - self.started[label], 3)
        return value

    def command(self, label, x=0, z=0, **extra):
        self.serials[label] = self.serials.get(label, 0) + 1
        atomic(self.directory / (label + '-intent.json'), {'serial': self.serials[label], 'x': x, 'z': z, **extra})

    def close(self, label, abrupt=False, server=False):
        process, output = self.processes[label]
        if process.poll() is None:
            if abrupt:
                process.kill()
            elif server:
                (self.directory / (label + '-stop')).touch()
            else:
                self.command(label, quit=True)
            process.wait(timeout=8)
        output.close()

    def cleanup(self):
        for process, output in self.processes.values():
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait(timeout=5)
            output.close()


def actor(status, identity):
    return next(a for a in status['snapshot']['Actors'] if a['Id'] == identity)


def main():
    f = NetworkFixture()
    checks = []
    def passed(message):
        checks.append(message)
        print('PASS', message, flush=True)
    try:
        server = f.launch('server')
        status = f.ready('server', 0)
        assert status['display'] == 'headless' and status['tick'] == 0
        time.sleep(.4)
        assert f.status('server')['tick'] == 0
        gpu_fds = []
        for fd in Path(f'/proc/{server.pid}/fd').iterdir():
            try:
                target = os.readlink(fd)
                if target.startswith(('/dev/nvidia', '/dev/dri')):
                    gpu_fds.append(target)
            except OSError:
                pass
        assert not gpu_fds, gpu_fds
        passed('CPU headless server starts paused with no GPU device handles')
        f.launch('alice', 'alice'); a = f.ready('alice', 1); aid = a['ownId']
        f.launch('bob', 'bob'); b = f.ready('bob', 2); bid = b['ownId']
        wait(lambda: f.status('alice')['active'] == 2 and f.status('server')['active'] == 2, 'late join visible to first client and server telemetry')
        assert aid != bid
        passed('two independent clients join, including late join, with distinct stable characters')
        profiles_hash = hashlib.sha256((f.profiles / 'alice.json').read_bytes()).hexdigest()
        clone = f.directory / 'clone'; clone.mkdir()
        shutil.copy2(f.profiles / 'alice.json', clone / 'alice.json')
        f.launch('duplicate', 'alice', profiles=clone)
        wait(lambda: (s := f.status('duplicate')) and s['connection'].startswith('Join rejected'), 'duplicate identity rejected')
        assert f.status('server')['active'] == 2
        f.processes['duplicate'][0].terminate(); f.processes['duplicate'][0].wait(timeout=5)
        assert hashlib.sha256((f.profiles / 'alice.json').read_bytes()).hexdigest() == profiles_hash
        passed('duplicate active identity rejected without takeover or profile replacement')
        start_a = actor(f.status('alice'), aid); start_b = actor(f.status('bob'), bid)
        f.command('alice', 1, 0); f.command('bob', 0, -1)
        time.sleep(.8)
        f.command('alice'); f.command('bob')
        time.sleep(.5)
        a, b = f.status('alice'), f.status('bob')
        assert actor(a, aid)['X'] - start_a['X'] > 1
        assert actor(b, bid)['Z'] - start_b['Z'] < -1
        def converged():
            a, b, s = f.status('alice'), f.status('bob'), f.status('server')
            return a['snapshot']['Actors'] == b['snapshot']['Actors'] == s['snapshot']['Actors']
        wait(converged, 'both clients converge on server positions')
        passed('both clients submit movement and converge exactly on authoritative positions')
        before = actor(f.status('alice'), aid)
        f.command('alice', 999, 0); time.sleep(.45)
        assert actor(f.status('alice'), aid) == before
        f.command('alice', 1, 0, sequence=1); time.sleep(.45)
        assert actor(f.status('alice'), aid) == before
        f.command('alice'); time.sleep(.25)
        passed('invalid magnitude and stale-sequence movement rejected over ENet')
        f.command('alice', 1, 0); time.sleep(3.1); f.command('alice'); time.sleep(.4)
        a = actor(f.status('alice'), aid)
        assert 5.5 <= a['X'] <= 5.75 and abs(a['Z']) < .01, a
        passed('server movement crosses doorway and respects room bounds')
        f.close('bob', abrupt=True)
        wait(lambda: f.status('alice')['active'] == 1 and f.status('server')['active'] == 1, 'abrupt leave removes remote body', 10)
        t = f.status('server')['tick']; time.sleep(.4)
        assert f.status('server')['tick'] > t
        passed('abrupt disconnect removes body; remaining player keeps clock advancing')
        f.close('alice')
        wait(lambda: f.status('server')['active'] == 0, 'last client leaves')
        tick = f.status('server')['tick']; time.sleep(.6)
        assert f.status('server')['tick'] == tick
        saved = read(f.save)
        assert saved['Tick'] == tick and len(saved['Characters']) == 2
        assert all('Active' not in c for c in saved['Characters'])
        passed('last disconnect freezes clock and persists absent character records')
        metrics = {'port': f.port, 'startup_seconds': dict(f.startups), 'server_rss_bytes': f.status('server')['rssBytes'],
                   'client_rss_bytes': [f.status('alice')['rssBytes'], f.status('bob')['rssBytes']],
                   'server_display': f.status('server')['display'], 'gpu_device_handles': gpu_fds,
                   'tick_rate': 20, 'snapshot_rate': 10, 'network': 'ENet UDP loopback', 'build': 'Debug'}
        f.close('server', server=True)
        # Deliberate offline save fixture: changed geometry and retained non-empty character data.
        saved = read(f.save)
        old = next(c for c in saved['Characters'] if c['Id'] == aid)
        old_position = (old['X'], old['Z'])
        old['Inventory'] = {'questsim:bandage': 3}; old['Experience'] = 42
        saved['Blocks'].append({'Id':'questsim:changed-world','X':old['X'],'Z':old['Z'],'Width':1,'Depth':1,'Height':1})
        atomic(f.save, saved)
        f.launch('restarted-server'); status = f.ready('restarted-server', 0)
        assert status['tick'] == tick
        time.sleep(.4); assert f.status('restarted-server')['tick'] == tick
        passed('server restart retains frozen world ticks and no disconnected bodies')
        f.launch('alice-return', 'alice'); a = f.ready('alice-return', 1)
        restored = actor(a, aid)
        assert a['ownId'] == aid and a['own']['Inventory'] == {'questsim:bandage': 3} and a['own']['Experience'] == 42
        assert (restored['X'], restored['Z']) != old_position
        assert abs(restored['X']-old_position[0]) >= .75 or abs(restored['Z']-old_position[1]) >= .75
        wait(lambda: f.status('restarted-server')['tick'] > tick, 'clock resumes on rejoin')
        passed('reconnect preserves identity/inventory/progression, safely relocates changed-world position, resumes time')
        f.launch('bob-return', 'bob'); b = f.ready('bob-return', 2)
        assert b['ownId'] == bid
        wait(lambda: f.status('alice-return')['active'] == 2, 'second reconnect converges')
        passed('both clients reconnect to the restarted server with original identities')
        f.close('alice-return'); f.close('bob-return')
        wait(lambda: f.status('restarted-server')['active'] == 0, 'second empty pause')
        f.close('restarted-server', server=True)
        bad = read(f.save); bad['Version'] = 999
        bad_path = f.directory / 'unsupported-world.json'; atomic(bad_path, bad)
        before_hash = hashlib.sha256(bad_path.read_bytes()).hexdigest()
        p = f.launch('unsupported', save=bad_path); assert p.wait(timeout=8) == 1
        assert hashlib.sha256(bad_path.read_bytes()).hexdigest() == before_hash
        passed('incompatible save fails startup without overwriting it')
        for label in f.processes:
            if label == 'unsupported':
                continue
            log = (f.directory / (label + '.log')).read_text()
            assert 'ERROR:' not in log and 'WARNING:' not in log, (label, log)
        metrics['checks'] = checks
        atomic(f.directory / 'result.json', metrics)
        print('EVIDENCE', f.directory, flush=True)
    finally:
        f.cleanup()


if __name__ == '__main__':
    main()
