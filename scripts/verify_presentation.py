"""Real X11 input + screenshots + app acknowledgement, not injected game state."""
import json
from pathlib import Path
import time
from desktop_test import QuestSimWindow, OUT


def state(window, log):
    window.key('space', .05)
    time.sleep(.1)
    return json.loads([line[6:] for line in log.read_text().splitlines() if line.startswith('STATE ')][-1])


def click_point(window, point, prefix):
    x, y = round(point[prefix + '_screen_x']), round(point[prefix + '_screen_y'])
    window.click(x, y)
    time.sleep(.15)


def verify(mode):
    name = 'verify-' + mode
    log = OUT / (name + '.log')
    w = QuestSimWindow(name, '--mode=' + mode, '--camera=third', scene='Lab.tscn')
    try:
        time.sleep(.4)
        forward, left = ('d', 'w') if mode == '2d' else ('w', 'a')
        w.key(forward, 2.1)
        s = state(w, log)
        assert s['player_x'] > 1.5, s
        w.screenshot(name + '-doorway')
        w.key('r'); w.key(left, .65); w.key(forward, 2.0)
        s = state(w, log)
        assert -.7 < s['player_x'] < -.35 and s['blockedMoves'] > 0, s
        w.screenshot(name + '-wall')
        w.key('r'); w.key(forward, 2.2); w.key(left, 1.2)
        s = state(w, log)
        assert s['height'] > 0, s
        w.screenshot(name + '-platform')
        w.key('r')
        s = state(w, log)
        click_point(w, s, 'piece')
        s = state(w, log)
        assert s['selected'], s
        w.screenshot(name + '-selected')
        click_point(w, s, 'wall')
        s = state(w, log)
        assert s['rejectedCount'] == 1 and s['selected'], s
        w.screenshot(name + '-rejected')
        click_point(w, s, 'valid')
        s = state(w, log)
        assert s['placedCount'] == 1 and not s['selected'], s
        w.screenshot(name + '-placed')
        if mode != '2d':
            w.key('c'); w.key('e', .98); w.key('Up', .34)
            s = state(w, log)
            assert s['camera'] == 'first', s
            w.screenshot(name + '-first')
            click_point(w, s, 'piece')
            s = state(w, log)
            assert s['selected'], s
            click_point(w, s, 'valid')
            s = state(w, log)
            assert s['placedCount'] == 2, s
            w.screenshot(name + '-first-placed')
            w.key('r'); w.key(forward, 2.1)
            s = state(w, log)
            assert s['player_x'] > 1.5, s
            w.screenshot(name + '-first-doorway')
            w.key('q', .5); w.key('Down', .3)
            w.screenshot(name + '-first-look')
            w.key('c'); w.key('r'); w.key('q', 1.0); w.key('Down', .4)
            w.screenshot(name + '-third-orbit')
        print('PASS', mode, 'doorway, wall collision, raised steps, select/reject/place',
              '+ first-person controls/placement' if mode != '2d' else '', flush=True)
    finally:
        w.close()


if __name__ == '__main__':
    for candidate in ['2d', 'sprite3d', 'poly3d']:
        verify(candidate)
