"""Actual documented shell launchers plus graceful Ctrl-C persistence."""
import signal
import subprocess
from verify_multiplayer import NetworkFixture, wait, read

f = NetworkFixture('terminal')
try:
    for label, script, extra in [('server','scripts/run_server.sh',['--save='+str(f.save)]),
                                 ('alice','scripts/run_client.sh',['alice','--profiles-dir='+str(f.profiles)]),
                                 ('bob','scripts/run_client.sh',['bob','--profiles-dir='+str(f.profiles)])]:
        output = (f.directory / (label + '.log')).open('w')
        process = subprocess.Popen(['bash', script, *extra, '--port='+str(f.port),
                                    '--status='+str(f.directory/(label+'.json')),
                                    '--shutdown-file='+str(f.directory/(label+'-stop'))], stdout=output, stderr=subprocess.STDOUT)
        f.processes[label] = (process, output)
        wait(lambda: (s:=f.status(label)) and (s['role']=='server' or s['ownId']), label+' launcher ready')
    wait(lambda: all(f.status(label)['active']==2 for label in ['server','alice','bob']), 'all launched processes see two players')
    for label in ['alice','bob']:
        process = f.processes[label][0]
        ids = subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(process.pid)],text=True).splitlines()
        assert any(subprocess.check_output(['xdotool','getwindowname',w],text=True).strip() == 'QuestSim Shared Room (DEBUG)' for w in ids)
    print('PASS documented shell launchers produce server and two owned client windows', flush=True)
    before = f.status('server')['tick']
    assert before > 0
    server = f.processes['server'][0]
    server.send_signal(signal.SIGINT)
    assert server.wait(timeout=5)==0, server.returncode
    saved = read(f.save)
    assert saved['Tick'] >= before and len(saved['Characters']) == 2
    print('PASS server Ctrl-C flushes current ticks/characters and exits successfully', flush=True)
    wait(lambda: all(f.status(label)['connection'].startswith('Server disconnected') and f.status(label)['snapshot'] is None for label in ['alice','bob']), 'clients clear active state on server shutdown')
    print('PASS rendered clients show disconnected and clear active snapshot', flush=True)
    # Quit via owned client's fixed stop marker, since the server is already gone.
    for label in ['alice','bob']:
        (f.directory/(label+'-stop')).touch()
        assert f.processes[label][0].wait(timeout=5)==0
    for label in f.processes:
        text=(f.directory/(label+'.log')).read_text()
        assert 'ERROR:' not in text and 'WARNING:' not in text, (label,text)
    print('EVIDENCE',f.directory,flush=True)
finally:
    f.cleanup()
