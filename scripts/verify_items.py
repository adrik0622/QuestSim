"""One-item ENet race, durable replay, saved-request recovery and failure-before-ack."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import signal
from pathlib import Path
import threading
import time
import uuid
from verify_multiplayer import NetworkFixture, wait, read, atomic

ITEM = 'questsim:bandage'
CRATE = 'questsim:starter-crate'  # F would choose the nearest stocked crate; item tests target bandages.


def reply(f, label, request):
    return wait(lambda: (s:=f.status(label)) and s['lastPickup'] and s['lastPickup']['RequestId']==request and s['lastPickup'], label+' item reply')


def quantity(f, label):
    return f.status(label)['own']['Inventory'].get(ITEM, 0)


def run_race():
    f = NetworkFixture('items-race')
    checks = []
    def passed(text):
        checks.append(text); print('PASS',text,flush=True)
    try:
        f.launch('server'); f.ready('server',0)
        f.launch('alice','alice'); f.ready('alice',1)
        f.launch('bob','bob'); f.ready('bob',2)
        wait(lambda: f.status('alice')['active']==2 and f.status('server')['active']==2,'both registered')
        # A local write failure must prevent even sending the pickup request.
        blocked = Path(str(f.profiles/'alice.json')+'.tmp'); blocked.mkdir()
        f.command('alice',pickupRequestId=uuid.uuid4().hex,containerId=CRATE)
        wait(lambda: f.status('alice')['itemMessage'].startswith('Could not save pickup request'),'local request save failure')
        assert f.status('server')['snapshot']['Supplies'][0]['Count']==1 and len(f.status('server')['receipts'])==0
        blocked.rmdir()
        passed('client request persistence failure sends no item transaction')
        ids = {'alice':uuid.uuid4().hex,'bob':uuid.uuid4().hex}
        barrier=threading.Barrier(2)
        def attempt(label):
            barrier.wait(timeout=3); f.command(label,pickupRequestId=ids[label],containerId=CRATE)
        with ThreadPoolExecutor(max_workers=2) as pool:
            list(pool.map(attempt,['alice','bob']))
        results={label:reply(f,label,ids[label]) for label in ids}
        assert sorted(r['Success'] for r in results.values())==[False,True]
        winner=next(label for label,r in results.items() if r['Success'])
        loser=next(label for label,r in results.items() if not r['Success'])
        assert results[loser]['Reason']=='empty'
        wait(lambda: all(f.status(label)['snapshot']['Supplies'][0]['Count']==0 for label in ['server','alice','bob']), 'empty crate converges')
        assert quantity(f,winner)==1 and quantity(f,loser)==0
        saved=read(f.save)
        assert len(saved['Receipts'])==2 and saved['Supplies'][0]['Count']==0
        assert sum(c['Inventory'].get(ITEM,0) for c in saved['Characters'])==1
        passed('simultaneous independent clients get exactly one saved transfer and both see empty stock')
        for label in ids:
            f.command(label,retryPickup=True)
        wait(lambda: all((f.directory/(label+'.log')).read_text().count('ITEM_REPLY')>=2 for label in ids),'both retry acknowledgements')
        assert all(f.status(label)['lastPickup']==results[label] for label in ids)
        assert quantity(f,winner)==1 and quantity(f,loser)==0 and len(read(f.save)['Receipts'])==2
        passed('winner and loser retries return identical persisted results without another transfer')
        original=results[winner]
        f.command(winner,pickupRequestId=ids[winner],containerId='questsim:wrong-target')
        wait(lambda: f.status(winner)['lastPickup']['Reason']=='invalid_request','conflicting request rejected')
        assert quantity(f,winner)==1 and len(read(f.save)['Receipts'])==2
        passed('conflicting request payload cannot overwrite a successful receipt')
        # Restore the real last attempt, then reconnect with the same persisted request identity.
        f.command(winner,pickupRequestId=ids[winner],containerId=CRATE)
        wait(lambda: f.status(winner)['lastPickup']==original,'original request still valid')
        identity=f.status(winner)['ownId']
        f.close(winner)
        wait(lambda: f.status(loser)['active']==1,'winner absent')
        returned=winner+'-return'
        f.launch(returned,winner); f.ready(returned,2)
        assert f.status(returned)['ownId']==identity and quantity(f,returned)==1
        f.command(returned,retryPickup=True)
        assert reply(f,returned,ids[winner])==original and len(read(f.save)['Receipts'])==2
        passed('reconnect restores held item and can replay the saved client request')
        f.close(returned); f.close(loser)
        wait(lambda: f.status('server')['active']==0,'empty world')
        f.close('server',server=True)
        f.launch('server-return'); f.ready('server-return',0)
        for label in ids:
            client=label+'-restart'; f.launch(client,label); f.ready(client)
            f.command(client,pickupRequestId=ids[label],containerId=CRATE)
            assert reply(f,client,ids[label])==results[label]
        assert quantity(f,winner+'-restart')==1 and quantity(f,loser+'-restart')==0
        assert read(f.save)['Supplies'][0]['Count']==0 and len(read(f.save)['Receipts'])==2
        passed('server restart preserves the one-item result and both successful/failed receipts')
        for label in ids: f.close(label+'-restart')
        wait(lambda: f.status('server-return')['active']==0,'restart clients absent')
        f.close('server-return',server=True)
        for label in f.processes:
            text=(f.directory/(label+'.log')).read_text()
            assert 'ERROR:' not in text and 'WARNING:' not in text,(label,text)
        atomic(f.directory/'result.json',{'checks':checks,'winner':winner,'dispatch':'two client intent files written behind thread barrier'})
        print('EVIDENCE',f.directory,flush=True)
    finally:
        f.cleanup()


def run_recovery(fail_save):
    f=NetworkFixture('items-save-failure' if fail_save else 'items-lost-ack')
    try:
        f.launch('server'); f.ready('server',0)
        f.launch('alice','alice'); f.ready('alice',1)
        identity=f.status('alice')['ownId']; request=uuid.uuid4().hex
        if fail_save:
            obstruction=Path(str(f.save)+'.tmp'); obstruction.mkdir()
        else:
            # Stop server first: the client cannot possibly receive a reply yet.
            f.processes['server'][0].send_signal(signal.SIGSTOP)
        f.command('alice',pickupRequestId=request,containerId=CRATE)
        if not fail_save:
            wait(lambda: json.loads((f.profiles/'alice.json').read_text()).get('LastPickupRequestId')==request, 'pending request durable')
            time.sleep(.2)  # Let the client flush the reliable packet while server is stopped.
            f.processes['alice'][0].send_signal(signal.SIGSTOP)
            f.processes['server'][0].send_signal(signal.SIGCONT)
        if fail_save:
            assert f.processes['server'][0].wait(timeout=8)==1
            wait(lambda: f.status('alice')['snapshot'] is None,'failed-save server disconnected')
            assert f.status('alice')['lastPickup'] is None
            saved=read(f.save)
            assert saved['Supplies'][0]['Count']==1 and not saved['Receipts'] and not saved['Characters'][0]['Inventory']
            assert 'Session.TakeItem' in (f.directory/'server.log').read_text()
            obstruction.rmdir()
            print('PASS failed server save never acknowledges success or replaces the valid world',flush=True)
        else:
            wait(lambda: (s:=read(f.save)) and any(r['RequestId']==request and r['Success'] for r in s['Receipts']), 'pickup committed')
            f.close('server',abrupt=True)
            assert f.status('alice')['lastPickup'] is None and 'ITEM_REPLY' not in (f.directory/'alice.log').read_text()
            print('PASS stopped client receives no acknowledgement before abrupt server termination preserves the committed tuple',flush=True)
        p,out=f.processes['alice']
        if fail_save: p.terminate()
        else: p.kill()  # The deliberately stopped client cannot handle a graceful signal.
        p.wait(timeout=5); out.close()
        profile=json.loads((f.profiles/'alice.json').read_text())
        assert profile['LastPickupRequestId']==request
        f.launch('server-return'); f.ready('server-return',0)
        f.launch('alice-return','alice'); f.ready('alice-return',1)
        assert f.status('alice-return')['ownId']==identity
        f.command('alice-return',retryPickup=True)
        result=reply(f,'alice-return',request)
        assert result['Success'] and quantity(f,'alice-return')==1
        assert read(f.save)['Supplies'][0]['Count']==0 and len(read(f.save)['Receipts'])==1
        print('PASS', 'retry after failed commit transfers once' if fail_save else 'lost-reply retry after restart returns the prior result without duplication',flush=True)
        f.close('alice-return'); wait(lambda:f.status('server-return')['active']==0,'recovered client absent')
        f.close('server-return',server=True)
        print('EVIDENCE',f.directory,flush=True)
    finally:
        f.cleanup()


def run_migration():
    f=NetworkFixture('items-migration')
    try:
        profile_id=uuid.uuid4().hex; actor_id='actor:'+uuid.uuid4().hex
        f.profiles.mkdir()
        atomic(f.profiles/'legacy.json',{'Id':profile_id,'Name':'legacy'})
        original={'Version':1,'ContentId':'questsim:q01-room-v1','Tick':70,'HalfExtent':6,
                  'Characters':[{'Id':actor_id,'ProfileId':profile_id,'Name':'legacy','X':-3,'Z':0,'Heading':-1.5707963267948966,
                                 'Inventory':{ITEM:4,'example:keepsake':2},'Experience':9}],
                  'Blocks':[{'Id':'example:retained-block','X':4,'Z':4,'Width':1,'Depth':1,'Height':1}]}
        atomic(f.save,original); original_bytes=f.save.read_bytes()
        f.launch('server'); f.ready('server',0)
        backups=list(f.directory.glob('world.json.v1-*.bak'))
        assert len(backups)==1 and backups[0].read_bytes()==original_bytes and f.save.read_bytes()==original_bytes
        f.launch('legacy','legacy'); client=f.ready('legacy',1)
        assert client['ownId']==actor_id and client['own']['Inventory']=={ITEM:4,'example:keepsake':2} and client['own']['Experience']==9
        migrated=read(f.save)
        assert migrated['Version']==4 and migrated['Blocks'][:len(original['Blocks'])]==original['Blocks'] and len(migrated['Blocks'])==len(original['Blocks'])+5 and migrated['Tick']>=70
        request=uuid.uuid4().hex; f.command('legacy',pickupRequestId=request,containerId=CRATE)
        assert reply(f,'legacy',request)['Success'] and quantity(f,'legacy')==5
        f.close('legacy'); wait(lambda:f.status('server')['active']==0,'legacy client absent');f.close('server',server=True)
        f.launch('server-return');f.ready('server-return',0)
        f.launch('legacy-return','legacy');f.ready('legacy-return',1)
        f.command('legacy-return',retryPickup=True)
        assert reply(f,'legacy-return',request)['Success'] and quantity(f,'legacy-return')==5
        assert read(f.save)['Supplies'][0]['Count']==0 and len(list(f.directory.glob('*.bak')))==1
        print('PASS real Q01 save and two-field profile migrate with exact backup and retained identities/items/world geometry',flush=True)
        print('PASS migrated v2 restart never restocks or repeats the bandage transfer',flush=True)
        f.close('legacy-return');wait(lambda:f.status('server-return')['active']==0,'legacy return absent');f.close('server-return',server=True)
        for label in f.processes:
            text=(f.directory/(label+'.log')).read_text()
            assert 'ERROR:' not in text and 'WARNING:' not in text,(label,text)
        print('EVIDENCE',f.directory,flush=True)
    finally:
        f.cleanup()


if __name__=='__main__':
    run_race()
    run_recovery(False)
    run_recovery(True)
    run_migration()
