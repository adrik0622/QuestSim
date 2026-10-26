"""Read-only world inspector: python3 scripts/inspect_world.py [path/to/world.json] [--facts N] [--actor NAME]
Summarises a save for debugging: characters, conditions, factions, opportunities, named NPCs, caches, the item ledger,
the settlement economy, recent facts, and the population snapshot header. Never writes anything."""
import argparse
import json
import struct
from collections import Counter
from pathlib import Path

FACTIONS = ["Survivors' Union", 'Free Traders', 'The Hollow Choir', 'Shambling Mass', 'The Pale Tide']
DEFAULT_STANDING = {0: 20, 1: 0, 2: -100}


def population_header(path):
    """QSPOP header: .NET BinaryWriter string (7-bit length + bytes), int version, ulong seed, int count, uint tick."""
    with open(path, 'rb') as f:
        length = f.read(1)[0]
        magic = f.read(length).decode()
        version, seed, count, tick = struct.unpack('<iQiI', f.read(20))
    return magic, version, seed, count, tick


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('save', nargs='?', default='.artifacts/play/world.json')
    parser.add_argument('--facts', type=int, default=15)
    parser.add_argument('--actor', default='')
    args = parser.parse_args()
    path = Path(args.save)
    w = json.loads(path.read_text())
    names = {c['Id']: c['Name'] for c in w['Characters']}
    print(f"World {path}  v{w['Version']} {w['ContentId']}  seed {w.get('WorldSeed')}  tick {w['Tick']} ({w['Tick'] / 20 / 60:.1f} online min)")
    print(f"Population {w.get('Population', 0):,} agents, {len(w.get('AgentDeltas', []))} deltas "
          f"({sum(1 for d in w.get('AgentDeltas', []) if d['Dead'])} dead); permadeath {w.get('Permadeath', False)}; "
          f"mods {', '.join(m['Id'] + '@' + m['Version'] for m in w.get('Mods', [])) or 'none'}")
    snapshot = w.get('PopulationFile')
    if snapshot and (path.parent / snapshot).exists():
        magic, version, seed, count, tick = population_header(path.parent / snapshot)
        print(f"Population snapshot {snapshot}: {magic} v{version}, {count:,} agents at population tick {tick}")

    print('\nCharacters')
    for c in w['Characters']:
        if args.actor and args.actor.lower() not in c['Name'].lower(): continue
        stats = c.get('Stats', {})
        conditions = ', '.join(f"{k.split(':')[1]} {v / 20 / 60:.1f}m" for k, v in c.get('Conditions', {}).items()) or '-'
        inventory = ', '.join(f"{k.split(':')[1]} {v}" for k, v in sorted(c['Inventory'].items())) or 'empty'
        print(f"  {c['Name']:<12} {c.get('Status', 'alive'):<7} health {stats.get('questsim:health', 100):>3}  at ({c['X']:.0f}, {c['Z']:.0f})  "
              f"conditions: {conditions}\n               inventory: {inventory}")

    print('\nFactions (standing toward players)')
    standing = {int(k): v for k, v in w.get('Standing', {}).items()}
    for i, name in enumerate(FACTIONS[:3]):
        value = standing.get(i, DEFAULT_STANDING[i])
        print(f"  {name:<18} {value:+4d}{'  HOSTILE' if value <= -50 else ''}")

    if w.get('Settlements'):
        goods, profiles = ['food', 'medicine', 'ammo', 'materials'], ['farm', 'workshop', 'clinic', 'mixed']
        st = w['Settlements']
        short = [x for x in st if x['Reported']]
        stock = ', '.join('%s %s' % (g, format(sum(x['Stock'][i] for x in st) // 1000, ',')) for i, g in enumerate(goods))
        print(f"\nEconomy: {len(st)} settlements, {w.get('EconomyTicks', 0) // 75} game hours; "
              f"stock (units; 1 food feeds one person a day) {stock}; "
              f"mean order {sum(x['Order'] for x in st) / len(st):.0f}, unrest {sum(1 for x in st if x['Order'] < 40)}, reporting shortages {len(short)}")
        for x in sorted(short, key=lambda x: x['Order'])[:8]:
            print(f"  settlement {x['Index']:<4} {profiles[x['Profile']]:<8} order {x['Order']:>3}  short of "
                  f"{', '.join(g for i, g in enumerate(goods) if x['Reported'] >> i & 1)}; hungry {x['HoursHungry']} h")

    if w.get('Opportunities'):
        print('\nOpportunities')
        for o in w['Opportunities']:
            claimed = f" by {names.get(o['ClaimedBy'], o['ClaimedBy'])}" if o['ClaimedBy'] else ''
            print(f"  #{o['Id']} {o['Kind']:<8} {o['Status']}{claimed}: {o['Text']}")
    if w.get('Named'):
        print('\nNamed NPCs')
        for n in w['Named']:
            print(f"  npc:{n['Index']} \"{n['Title']}\" victories {n['Victories']}, +{n['BonusHealth']} health, +{n['BonusDamage']} damage, carrying {n['Inventory'] or 'nothing'}")
    if w.get('Caches'):
        print('\nCaches')
        for k in w['Caches']:
            print(f"  cache:{k['Id']} {k['Label']} at ({k['X']:.0f}, {k['Z']:.0f}): {k['Inventory'] or 'empty'}")
    if w.get('Transfers'):
        reasons = Counter(t['Reason'].split(':')[0] for t in w['Transfers'])
        print(f"\nItem ledger: {len(w['Transfers'])} transfers ({', '.join(f'{r} {n}' for r, n in reasons.most_common())})")
    facts = w.get('Facts', [])
    if facts:
        print(f"\nLast {min(args.facts, len(facts))} of {len(facts)} facts")
        for f in facts[-args.facts:]:
            who = names.get(f['Subject'], f['Subject'])
            what = names.get(f['Object'], f['Object'])
            print(f"  #{f['Id']:<4} t{f['Tick']:<8} {f['Kind']:<12} {who} -> {what} at ({f['X']:.0f}, {f['Z']:.0f}) [{f['Provenance']}]")


if __name__ == '__main__':
    main()
