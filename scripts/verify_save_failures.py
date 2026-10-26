"""Fault injection only in a fresh disposable world; never touches play saves."""
import hashlib
import json
from pathlib import Path
import socket
from verify_multiplayer import NetworkFixture, atomic


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def free_port():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]


f = NetworkFixture('save-failures')
try:
    seed = {'Version':1,'ContentId':'questsim:q01-room-v1','Tick':123,'HalfExtent':6,'Characters':[],'Blocks':[]}
    atomic(f.save, seed)
    initial = digest(f.save)
    primary = f.launch('primary'); f.ready('primary', 0)
    original_port = f.port
    f.port = free_port()
    duplicate = f.launch('duplicate-writer'); assert duplicate.wait(timeout=8) == 1
    assert digest(f.save) == initial
    print('PASS exclusive save lock rejects a second writer even on another port', flush=True)
    f.port = original_port
    other = f.directory / 'other.json'; atomic(other, seed); other_hash = digest(other)
    collision = f.launch('port-collision', save=other); assert collision.wait(timeout=8) == 1
    assert digest(other) == other_hash
    print('PASS port collision fails without writing the loaded save', flush=True)
    f.port = free_port()
    invalid_status = f.directory / 'status-failure.json'; invalid_status.mkdir()
    status_failure = f.launch('status-failure', save=other); assert status_failure.wait(timeout=8) == 1
    assert digest(other) == other_hash
    print('PASS diagnostic startup failure leaves the save unchanged', flush=True)
    Path(str(f.save) + '.tmp').mkdir()
    f.close('primary', server=True)
    assert primary.returncode == 1, primary.returncode
    assert digest(f.save) == initial
    print('PASS final-save failure exits nonzero and retains last valid save', flush=True)
    print('EVIDENCE', f.directory, flush=True)
finally:
    f.cleanup()
