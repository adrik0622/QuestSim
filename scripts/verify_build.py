"""Q02b: one-wall ENet race, durable build replay, lost reply, failed save and v2 migration."""
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import threading
import time
import uuid
from verify_multiplayer import NetworkFixture, wait, read, atomic, pause
from verify_items import reply

PANEL = 'questsim:wall-panel'
PANEL_CRATE = 'questsim:panel-crate'
SITE = 'questsim:build-cell-1'


def built(f, label, request):
    return wait(lambda: (s:=f.status(label)) and s['lastBuild'] and s['lastBuild']['RequestId']==request and s['lastBuild'], label+' build reply')


def panels(f, label):
    return f.status(label)['own']['Inventory'].get(PANEL, 0)


def walls(state):
    return [b for b in state['Blocks'] if b['Id']==SITE]


def take_panel(f, label):
    request=uuid.uuid4().hex; f.command(label,pickupRequestId=request,containerId=PANEL_CRATE)
    assert reply(f,label,request)['Success'] and panels(f,label)==1


def clean_logs(f):
    for label in f.processes:
        text=(f.directory/(label+'.log')).read_text()
        assert 'ERROR:' not in text and 'WARNING:' not in text,(label,text)


def run_race():
    f=NetworkFixture('build-race'); checks=[]
    def passed(text):
        checks.append(text); print('PASS',text,flush=True)
    try:
        f.launch('server'); f.ready('server',0)
        f.launch('alice','alice'); f.ready('alice',1)
        f.launch('bob','bob'); f.ready('bob',2)
        wait(lambda: f.status('alice')['active']==2,'both registered')
        for label in ['alice','bob']: take_panel(f,label)
        assert read(f.save)['Supplies'][1]['Count']==0
        ids={'alice':uuid.uuid4().hex,'bob':uuid.uuid4().hex}
        barrier=threading.Barrier(2)
        def attempt(label):
            barrier.wait(timeout=3); f.command(label,buildRequestId=ids[label])
        with ThreadPoolExecutor(max_workers=2) as pool:
            list(pool.map(attempt,['alice','bob']))
        results={label:built(f,label,ids[label]) for label in ids}
        winner=next(label for label,r in results.items() if r['Success'])
        loser=next(label for label,r in results.items() if not r['Success'])
        assert results[loser]['Reason']=='occupied' and results[winner]['Cost']==1 and results[loser]['Cost']==0
        wait(lambda: all(len(walls(f.status(label)['snapshot']))==1 for label in ['server','alice','bob']),'wall converges')
        saved=read(f.save)
        assert len(walls(saved))==1 and len(saved['BuildReceipts'])==2
        assert panels(f,winner)==0 and panels(f,loser)==1
        assert sum(c['Inventory'].get(PANEL,0) for c in saved['Characters'])==1
        passed('simultaneous clients place exactly one saved wall; only the winner spends a panel')
        for label in ids: f.command(label,retryBuild=True)
        wait(lambda: all((f.directory/(label+'.log')).read_text().count('BUILD_REPLY')>=2 for label in ids),'both build retries')
        assert all(f.status(label)['lastBuild']==results[label] for label in ids)
        assert len(read(f.save)['BuildReceipts'])==2 and panels(f,winner)==0 and panels(f,loser)==1
        passed('winner and loser build retries return identical persisted results without another cost')
        f.command(winner,buildRequestId=ids[winner],siteId='questsim:other-cell')
        wait(lambda: f.status(winner)['lastBuild']['Reason']=='invalid_request','conflicting build rejected')
        assert len(walls(read(f.save)))==1 and len(read(f.save)['BuildReceipts'])==2
        passed('conflicting build payload cannot overwrite a successful receipt')
        f.command(winner,buildRequestId=ids[winner])
        wait(lambda: f.status(winner)['lastBuild']==results[winner],'original build still valid')
        identity=f.status(winner)['ownId']
        f.close(winner); wait(lambda: f.status(loser)['active']==1,'winner absent')
        returned=winner+'-return'; f.launch(returned,winner); f.ready(returned,2)
        assert f.status(returned)['ownId']==identity and panels(f,returned)==0 and len(walls(f.status(returned)['snapshot']))==1
        f.command(returned,retryBuild=True)
        assert built(f,returned,ids[winner])==results[winner]
        passed('reconnect shows the wall and replays the saved build request')
        f.close(returned); f.close(loser)
        wait(lambda: f.status('server')['active']==0,'empty world'); f.close('server',server=True)
        f.launch('server-return'); f.ready('server-return',0)
        for label in ids:
            client=label+'-restart'; f.launch(client,label); f.ready(client)
            f.command(client,buildRequestId=ids[label])
            assert built(f,client,ids[label])==results[label]
        assert panels(f,winner+'-restart')==0 and panels(f,loser+'-restart')==1
        assert len(walls(read(f.save)))==1 and len(read(f.save)['BuildReceipts'])==2
        passed('server restart preserves the wall, spent/retained panels and both build receipts')
        for label in ids: f.close(label+'-restart')
        wait(lambda: f.status('server-return')['active']==0,'restart clients absent')
        f.close('server-return',server=True)
        clean_logs(f)
        atomic(f.directory/'result.json',{'checks':checks,'winner':winner})
        print('EVIDENCE',f.directory,flush=True)
    finally:
        f.cleanup()


def run_recovery(fail_save):
    f=NetworkFixture('build-save-failure' if fail_save else 'build-lost-ack')
    try:
        f.launch('server'); f.ready('server',0)
        f.launch('alice','alice'); f.ready('alice',1)
        take_panel(f,'alice')
        request=uuid.uuid4().hex
        if fail_save:
            obstruction=Path(str(f.save)+'.tmp'); obstruction.mkdir()
        else:
            pause(f.processes['server'][0])
        f.command('alice',buildRequestId=request)
        if fail_save:
            assert f.processes['server'][0].wait(timeout=8)==1
            wait(lambda: f.status('alice')['snapshot'] is None,'failed-save server disconnected')
            saved=read(f.save)
            assert f.status('alice')['lastBuild'] is None and not walls(saved) and not saved['BuildReceipts']
            assert saved['Characters'][0]['Inventory'][PANEL]==1 and 'Session.PlaceWall' in (f.directory/'server.log').read_text()
            obstruction.rmdir()
            print('PASS failed server save never acknowledges a wall or spends the saved panel',flush=True)
        else:
            wait(lambda: json.loads((f.profiles/'alice.json').read_text()).get('LastBuildRequestId')==request,'pending build durable')
            time.sleep(.2)
            pause(f.processes['alice'][0])
            pause(f.processes['server'][0], resume=True)
            wait(lambda: (s:=read(f.save)) and any(r['RequestId']==request and r['Success'] for r in s['BuildReceipts']),'build committed')
            f.close('server',abrupt=True)
            assert f.status('alice')['lastBuild'] is None and 'BUILD_REPLY' not in (f.directory/'alice.log').read_text()
            print('PASS stopped client receives no build acknowledgement before abrupt server termination',flush=True)
        p,out=f.processes['alice']
        if fail_save: p.terminate()
        else: p.kill()
        p.wait(timeout=5); out.close()
        f.launch('server-return'); f.ready('server-return',0)
        f.launch('alice-return','alice'); f.ready('alice-return',1)
        f.command('alice-return',retryBuild=True)
        assert built(f,'alice-return',request)['Success'] and panels(f,'alice-return')==0
        saved=read(f.save)
        assert len(walls(saved))==1 and len(saved['BuildReceipts'])==1
        print('PASS', 'build retry after failed commit places once' if fail_save else 'lost build reply retried after restart returns the prior result without a second cost',flush=True)
        f.close('alice-return'); wait(lambda:f.status('server-return')['active']==0,'recovered client absent')
        f.close('server-return',server=True)
        print('EVIDENCE',f.directory,flush=True)
    finally:
        f.cleanup()


def run_migration():
    f=NetworkFixture('build-migration')
    try:
        profile_id=uuid.uuid4().hex; actor_id='actor:'+uuid.uuid4().hex; request=uuid.uuid4().hex
        f.profiles.mkdir()
        atomic(f.profiles/'legacy.json',{'Id':profile_id,'Name':'legacy','LastPickupRequestId':request,'LastPickupContainerId':'questsim:starter-crate'})
        receipt={'RequestId':request,'ActorId':actor_id,'ContainerId':'questsim:starter-crate','ItemId':'questsim:bandage','Success':True,'Reason':'taken','Quantity':1}
        original={'Version':2,'ContentId':'questsim:q02-room-v1','Tick':90,'HalfExtent':6,
                  'Supply':{'Id':'questsim:starter-crate','ItemId':'questsim:bandage','X':-3,'Z':-1.5,'Count':0},'Receipts':[receipt],
                  'Characters':[{'Id':actor_id,'ProfileId':profile_id,'Name':'legacy','X':-3,'Z':0,'Heading':-1.5707963267948966,
                                 'Inventory':{'questsim:bandage':1,'example:keepsake':2},'Experience':9}],
                  'Blocks':[{'Id':'example:retained-block','X':4,'Z':4,'Width':1,'Depth':1,'Height':1}]}
        atomic(f.save,original); original_bytes=f.save.read_bytes()
        f.launch('server'); f.ready('server',0)
        backups=list(f.directory.glob('world.json.v2-*.bak'))
        assert len(backups)==1 and backups[0].read_bytes()==original_bytes and f.save.read_bytes()==original_bytes
        f.launch('legacy','legacy'); client=f.ready('legacy',1)
        assert client['ownId']==actor_id and client['own']['Inventory']=={'questsim:bandage':1,'example:keepsake':2}
        assert [s['Count'] for s in client['snapshot']['Supplies']]==[0,2]
        f.command('legacy',retryPickup=True)
        assert reply(f,'legacy',request)==receipt
        migrated=read(f.save)
        assert migrated['Version']==4 and migrated['Blocks'][:len(original['Blocks'])]==original['Blocks'] and len(migrated['Blocks'])==len(original['Blocks'])+5 and migrated['Receipts']==[receipt]
        print('PASS real Q02a save and profile migrate with exact backup; saved bandage replay unchanged',flush=True)
        take_panel(f,'legacy')
        build=uuid.uuid4().hex; f.command('legacy',buildRequestId=build)
        assert built(f,'legacy',build)['Success']
        f.close('legacy'); wait(lambda:f.status('server')['active']==0,'legacy absent'); f.close('server',server=True)
        f.launch('server-return'); f.ready('server-return',0)
        f.launch('legacy-return','legacy'); f.ready('legacy-return',1)
        f.command('legacy-return',retryBuild=True)
        assert built(f,'legacy-return',build)['Success'] and panels(f,'legacy-return')==0
        saved=read(f.save)
        assert [s['Count'] for s in saved['Supplies']]==[0,1] and len(walls(saved))==1 and len(list(f.directory.glob('*.bak')))==1
        print('PASS migrated v3 restart never restocks panels, repeats migration or rebuilds',flush=True)
        f.close('legacy-return'); wait(lambda:f.status('server-return')['active']==0,'legacy return absent'); f.close('server-return',server=True)
        clean_logs(f)
        print('EVIDENCE',f.directory,flush=True)
    finally:
        f.cleanup()


if __name__=='__main__':
    run_race()
    run_recovery(False)
    run_recovery(True)
    run_migration()
