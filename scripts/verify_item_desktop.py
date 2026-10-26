"""Actual F/T keys in owned windows; a single world item, then reconnect/retry."""
import time
from desktop_test import QuestSimWindow, OUT
from verify_multiplayer import NetworkFixture, wait, read, atomic

f=NetworkFixture('items-desktop'); windows=[]; checks=[]


def launch(label, profile):
    w=QuestSimWindow('items-'+label,'--profile='+profile,'--port='+str(f.port),'--profiles-dir='+str(f.profiles),
                     '--status='+str(f.directory/(label+'.json')),game=True)
    windows.append(w)
    wait(lambda:(s:=f.status(label)) and s['ownId'] and s['snapshot'],label+' rendered ready')
    return w


def passed(message):
    checks.append(message);print('PASS',message,flush=True)


try:
    f.launch('server');f.ready('server',0)
    a=launch('alice','alice');b=launch('bob','bob')
    wait(lambda:f.status('alice')['active']==2 and f.status('bob')['active']==2,'two players see supply')
    print(a.screenshot('items-filled-crate'),flush=True)
    a.key('f')
    wait(lambda:f.status('alice')['lastPickup'] and f.status('alice')['lastPickup']['Success'],'F takes item')
    original_a=f.status('alice')['lastPickup'];aid=f.status('alice')['ownId']
    b.key('f')
    # Since Q02b, F skips the empty bandage crate and takes from the nearest stocked crate (panels).
    wait(lambda:f.status('bob')['lastPickup'] and f.status('bob')['lastPickup']['ItemId']=='questsim:wall-panel','other F takes a panel')
    original_b=f.status('bob')['lastPickup']
    assert f.status('alice')['own']['Inventory']['questsim:bandage']==1
    assert f.status('bob')['own']['Inventory'].get('questsim:bandage',0)==0
    wait(lambda:all(f.status(label)['snapshot']['Supplies'][0]['Count']==0 for label in ['alice','bob']),'both crate displays empty')
    print(a.screenshot('items-winner'),flush=True)
    print(b.screenshot('items-loser'),flush=True)
    passed('real F input takes the one bandage; the other player gets a panel instead')
    a.key('t');b.key('t')
    wait(lambda:all((OUT/('items-'+label+'.log')).read_text().count('ITEM_REPLY')>=2 for label in ['alice','bob']),'T acknowledgements')
    assert f.status('alice')['lastPickup']==original_a and f.status('bob')['lastPickup']==original_b
    assert len(read(f.save)['Receipts'])==2
    print(a.screenshot('items-retried'),flush=True)
    passed('real T input replays both original outcomes without another item or receipt')
    a.close();wait(lambda:f.status('bob')['active']==1,'winner absent')
    returned=launch('alice-return','alice')
    wait(lambda:f.status('alice-return')['active']==2,'return sees both')
    assert f.status('alice-return')['ownId']==aid and f.status('alice-return')['own']['Inventory']['questsim:bandage']==1
    returned.key('t')
    wait(lambda:f.status('alice-return')['lastPickup']==original_a,'reconnect T replays stored attempt')
    assert len(read(f.save)['Receipts'])==2
    print(returned.screenshot('items-returned'),flush=True)
    passed('reconnected window retains inventory and saved request for T replay')
    returned.close();b.close()
    wait(lambda:f.status('server')['active']==0,'all graphical clients absent')
    tick=f.status('server')['tick'];time.sleep(.4)
    assert f.status('server')['tick']==tick
    f.close('server',server=True)
    passed('item interaction leaves disconnect absence and empty-world clock intact')
    for label in ['alice','bob','alice-return']:
        text=(OUT/('items-'+label+'.log')).read_text()
        assert 'ERROR:' not in text and 'WARNING:' not in text,text
    atomic(f.directory/'result.json',{'checks':checks,'stock':read(f.save)['Supplies'][0]['Count'],'receipt_count':len(read(f.save)['Receipts'])})
    print('EVIDENCE',f.directory,flush=True)
finally:
    for w in windows:
        if not w.log.closed:w.close()
    f.cleanup()
