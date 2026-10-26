"""Bounded X11 control of a QuestSim child only; no daemon or sibling writes."""
import os
from pathlib import Path
import re
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
ENGINE = ROOT / '.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64'
OUT = ROOT / '.artifacts/desktop'


def run(*args):
    return subprocess.check_output([str(a) for a in args], text=True, stderr=subprocess.STDOUT, timeout=10).strip()


class QuestSimWindow:
    def __init__(self, name, *args, scene='Bootstrap.tscn'):
        if not re.fullmatch(r'[a-z0-9_-]+', name):
            raise ValueError('Invalid evidence name')
        if scene not in {'Bootstrap.tscn', 'Lab.tscn'}:
            raise ValueError('Unknown QuestSim scene')
        OUT.mkdir(parents=True, exist_ok=True)
        self.log = (OUT / f'{name}.log').open('w')
        env = dict(os.environ, DOTNET_CLI_HOME=str(ROOT / '.local/dotnet'), NUGET_PACKAGES=str(ROOT / '.local/nuget'))
        self.process = subprocess.Popen([str(ENGINE), '--path', str(ROOT / 'experiments/presentation'), 'res://' + scene, '--', *args],
                                        cwd=ROOT, env=env, stdout=self.log, stderr=subprocess.STDOUT)
        self.window = None
        try:
            deadline = time.monotonic() + 15
            while time.monotonic() < deadline and self.process.poll() is None:
                try:
                    ids = run('xdotool', 'search', '--onlyvisible', '--pid', self.process.pid).splitlines()
                    found = [w for w in ids if run('xdotool', 'getwindowname', w) in {'QuestSim Presentation Lab', 'QuestSim Presentation Lab (DEBUG)'}]
                    if len(found) == 1:
                        self.window = found[0]
                        self.focus()
                        return
                except subprocess.CalledProcessError:
                    pass
                time.sleep(.1)
            raise RuntimeError('No unique owned QuestSim window; inspect log')
        except BaseException:
            self.close()
            raise

    def verify(self):
        if self.process.poll() is not None or self.window is None:
            raise RuntimeError('QuestSim child is not running')
        if run('xdotool', 'getwindowpid', self.window) != str(self.process.pid):
            raise RuntimeError('Window owner changed')
        if run('xdotool', 'getwindowname', self.window) not in {'QuestSim Presentation Lab', 'QuestSim Presentation Lab (DEBUG)'}:
            raise RuntimeError('Window title changed')

    def focus(self):
        self.verify()
        run('xdotool', 'windowactivate', '--sync', self.window)
        if run('xdotool', 'getactivewindow') != self.window:
            raise RuntimeError('QuestSim focus not confirmed')

    def screenshot(self, name):
        if not re.fullmatch(r'[a-z0-9_-]+', name):
            raise ValueError('Invalid screenshot name')
        self.focus()
        time.sleep(.15)
        path = OUT / f'{name}.png'
        run('import', '-window', self.window, path)
        return str(path)

    def key(self, key, seconds=.12):
        if key not in {'space', 'w', 'a', 's', 'd', 'q', 'e', 'Up', 'Down', 'c', 'r', 'Escape', '1', '2', '3'}:
            raise ValueError('Key outside experiment controls')
        if not 0 < seconds <= 3:
            raise ValueError('Input hold exceeds bound')
        self.focus()
        try:
            run('xdotool', 'keydown', key)
            time.sleep(seconds)
        finally:
            run('xdotool', 'keyup', key)

    def click(self, x, y):
        if not (0 <= x < 960 and 0 <= y < 540):
            raise ValueError('Click outside experiment viewport')
        self.focus()
        run('xdotool', 'mousemove', '--window', self.window, x, y)
        run('xdotool', 'click', '1')

    def close(self):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=5)
        self.log.close()


if __name__ == '__main__':
    window = QuestSimWindow('bootstrap-proof')
    try:
        print(window.screenshot('bootstrap-before'))
        window.key('space')
        print(window.screenshot('bootstrap-after'))
        assert 'QUESTSIM_PROBE_SPACE 1' in (OUT / 'bootstrap-proof.log').read_text()
        print('PASS owned QuestSim window, screenshot, focused Space input and log acknowledgement')
    finally:
        window.close()
