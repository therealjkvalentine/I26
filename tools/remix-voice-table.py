"""Writes the spoken lines' table (src/I76.Formats/Remix/Voice/lines.json, docs/remix-missions.md "Spoken lines") from the splice
work's approved set (approved-r4.json, approved-r5.json: the same schema). From each line it keeps our line text, its voice and rating,
and the numbers-only recipe (clip stems, offsets, gains, fades, the clips' checksums); the radio moments it may be said at and what
must be true of a job for it (its needs) are written here by hand, by line id. A line not listed here is kept as unused until it is.
No audio, no ratings file, no machine checks and no transcript of any original line are copied.

  python tools/remix-voice-table.py APPROVED.json [OUT.json]
"""
import json, os, sys

if len(sys.argv) < 2: sys.exit(__doc__)
SRC = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'I76.Formats', 'Remix', 'Voice', 'lines.json')

# The CRC-32 of the radio treatment's clips (the static bed's and the squelch bursts' sources), whose cuts carry none of their own.
RADIO_CRCS = {'i76:02sta10': 1726092972, 'nitro:06cri02': 478412519, 'nitro:16tab06': 2528391696, 'nitro:01tnk06': 631621047, 'nitro:01jad02': 2506801402}

# moment -> where a line may be said; needs -> what must be true of the job at that moment (RemixVoice.cs, RemixVoiceCheck.cs);
# a string: why the line is never said
U = {
 # Groove (the player's own reports, as the stock game has him report)
 'groove-106': 'says they got away', 'groove-62': 'says they got away',
 'groove-65': 'asks for an escort', 'groove-71': 'names Monkey Wrench',
 'groove-67': ('done', 'defended:truck stop clear'),
 'groove-107': ('contact', ''), 'groove-63': ('contact', ''), 'groove-64': ('contact reinforce', ''),
 'groove-68': ('targetsdown', ''), 'groove-70': ('return', 'home'),
 'groove-104': ('arrive', 'defended:warehouse'), 'groove-60': ('arrive', 'defended:truck stop'),
 'groove-61': ('arrive', 'defended:warehouse'), 'groove-69': ('done', 'defended:warehouse clear'), 'groove-105': ('done', 'defended:warehouse clear'),
 # Jade
 'jade-102': 'names Skeeter', 'jade-47': 'names Skeeter and a hangar', 'jade-46': 'names Vixen', 'jade-48': 'Jade fights beside him',
 'jade-49': 'soldier boys', 'jade-50': 'Jade is with him', 'jade-51': 'names the Riders', 'jade-121': 'Jade asks for help',
 'jade-57': 'says it is clear after the targets fall',
 'jade-103': ('targetsdown', ''), 'jade-122': ('lost done', 'clear'),
 'jade-53': ('getaway', 'lead'), 'jade-54': ('hurry', 'left:120'),
 'jade-55': ('lost', 'police more'), 'jade-101': ('police', 'police'), 'jade-56': ('police', 'police'),
 'jade-58': ('done', ''), 'jade-59': ('done', ''), 'jade-100': ('fight', 'plural'), 'jade-120': ('fight', 'plural'),
 # Taurus
 'taurus-00': ('go', 'place:truck stop'), 'taurus-01': 'stay close: Taurus is not there', 'taurus-27': ('lost return', 'place:hideout'),
 'taurus-72': 'stay close', 'taurus-03': ('go', 'place:truck stop guarded'),
 'taurus-04': 'airfield', 'taurus-12': 'airfield', 'taurus-39': 'airfield', 'taurus-40': 'depot',
 'taurus-05': ('go', 'place:truck stop'), 'taurus-06': ('go', 'place:tatum'), 'taurus-07': ('go', 'place:water tower'),
 'taurus-08': 'Tatum and 380 together (no place is both)',
 'taurus-10': ('lost next', 'place:rendezvous drove'), 'taurus-108': ('go', 'place:oil field'),
 'taurus-11': ('lost', 'place:hideout deadline'), 'taurus-111': ('go', 'place:water tower'),
 'taurus-113': ('hold', 'place:truck stop'), 'taurus-115': ('go', 'convoy'), 'taurus-116': 'civilians',
 'taurus-14': 'civilians', 'taurus-86': 'civilians', 'taurus-80': 'civilians',
 'taurus-117': ('return', 'place:truck stop'), 'taurus-119': ('return', 'clear'), 'taurus-25': ('return', 'clear'),
 'taurus-13': ('contact', 'convoy'), 'taurus-15': 'tanker', 'taurus-17': 'tanker',
 'taurus-16': 'guards are not the objective', 'taurus-18': ('contact', 'lead'),
 'taurus-19': ('reinforce', 'plural'), 'taurus-21': ('reinforce', 'plural from:east'), 'taurus-45': ('reinforce', 'plural from:west'),
 'taurus-24': 'names Skeeter', 'taurus-28': ('kill', ''), 'taurus-29': ('done', 'clear drove'),
 'taurus-30': ('targetsdown', ''), 'taurus-31': ('done', ''), 'taurus-33': ('hurry', 'left:45'),
 'taurus-34': ('hold go', 'defended:truck stop'), 'taurus-35': ('wiped', ''), 'taurus-36': ('getaway', 'exit:county line'),
 'taurus-37': 'north of Tatum (no truck stop there)', 'taurus-38': 'a turn the job does not know',
 'taurus-42': ('go contact', 'lead:truck'), 'taurus-43': ('count', 'count:2/3'), 'taurus-44': ('count', 'count:1/3'),
 'taurus-87': ('count', 'count:3/5'), 'taurus-73': ('go', 'place:truck stop'), 'taurus-74': ('go', 'place:oil field'),
 'taurus-75': ('go', 'place:bridge'), 'taurus-76': ('return', 'place:truck stop'), 'taurus-77': ('go', 'place:county line'),
 'taurus-78': '385 (no node table names it)', 'taurus-02': 'Taurus is not there to follow',
 'taurus-79': ('go', 'road:380:west'), 'taurus-82': ('hold', 'place:water tower'),
 'taurus-83': ('contact', 'plural'), 'taurus-85': ('getaway', 'convoy'), 'taurus-88': ('reinforce', 'plural'),
 'taurus-90': ('reinforce', 'plural'), 'taurus-22': ('reinforce', 'plural'), 'taurus-89': 'get over here',
 'taurus-93': ('lost', 'place:tatum clear'), 'taurus-94': ('return', 'place:rendezvous'), 'taurus-96': 'rendezvous after lost',
 'taurus-97': ('done', 'clear'), 'taurus-98': ('return', 'fought'), 'taurus-99': ('go', 'place:oil field'),
 'taurus-09': ('go', 'place:oil field'), 'taurus-109': ('go', 'place:truck stop'), 'taurus-110': ('go', 'place:county line'),
 'taurus-112': ('go', 'place:bridge'), 'taurus-81': ('go', 'place:bridge'), 'taurus-114': ('contact', 'plural'),
 'taurus-118': ('return', 'fought'), 'taurus-23': ('return', 'place:rendezvous fought'), 'taurus-26': 'lost them, then back',
 'taurus-32': 'leaving the map (the stock warning says it)', 'taurus-41': ('go contact', 'convoy'), 'taurus-84': ('go', 'convoy'),
 'jade-52': 'Jade asks for help',
 'taurus-91': 'clean, then the truck stop', 'taurus-92': ('return', 'place:hideout fought'), 'taurus-95': ('lost', 'place:hideout clear'),
}

d = json.load(open(SRC, encoding='utf-8'))
crcs = dict(RADIO_CRCS)
lines = []
radio = None
for l in d['lines']:
    r = l['recipe']
    rd = dict(r['radio']); seed = rd.pop('seed'); rd.pop('note', None); rd.pop('applies', None)
    radio = radio or rd
    if rd != radio: sys.exit(f"{l['id']}: a radio treatment unlike the others'")
    u = U.get(l['id'], 'not yet placed: give it moments and needs in tools/remix-voice-table.py')
    pieces = []
    for p in r['pieces']:
        k = f"{p['game']}:{p['clip']}"
        if crcs.setdefault(k, p['crc32']) != p['crc32']: sys.exit(f"{l['id']}: {k} with two checksums")
        q = {'game': p['game'], 'clip': p['clip'], 'start': p['start'], 'end': p['end'], 'at': p['at'], 'gain_db': p['gain_db'],
             'fade_in': p['fade_in'], 'fade_out': p['fade_out']}
        if p.get('tilt_db'): q['tilt_db'] = p['tilt_db']
        if p.get('stretch'): q['stretch'] = p['stretch']
        if p.get('match'): q['match'] = p['match']
        if p.get('fade_before_match'): q['fade_before_match'] = True
        pieces.append(q)
    # a line never said that is one recorded span (no join) keeps no text: its words would be the take's own
    text = '' if isinstance(u, str) and l.get('joins', 1) == 0 else l['text']
    # good: heard and rated good by the owner (round 3 or 4, or put back as heard); unheard: rebuilt or reworded since, its pieces
    # heard but not the line; okay: rated okay (approved-r4's lines carry only owner_rating: good or okay)
    latest = l.get('owner_rating_latest') or ('good' if l['owner_rating'] == 'good' else 'okay')
    rating = 'good' if latest.startswith('good') else 'unheard' if latest in ('reworded-r5', 'okay-rebuilt-r5') else 'okay'
    e = {'id': l['id'], 'voice': l['voice'], 'text': text, 'rating': rating,
         'nitro': l['needs_nitro_pack']}
    if isinstance(u, str): e['unused'] = u
    else: e['moments'] = u[0].split(); e['needs'] = u[1].split()
    e |= {'total': r['total_samples'], 'gain': r['out_gain'], 'seed': seed}
    if r.get('silence_from') is not None: e['silence_from'] = int(r['silence_from'])
    e['pieces'] = pieces
    lines.append(e)
for g, c, *_ in radio['bed']['sources'] + [radio['head'], radio['tail']]:
    if f'{g}:{c}' not in crcs: sys.exit(f'{g}:{c}: no checksum for a radio clip (add it to RADIO_CRCS: i76 remix lines --crc prints it)')
doc = {
    'about': 'Spoken radio lines for generated jobs (docs/remix-missions.md "Spoken lines"): our line text, where each may be said, '
             "and a numbers-only recipe of cuts of the player's own clips (game, clip stem, start/end samples at 11025 Hz, gain dB, "
             'fades, optional high-shelf tilt, WSOLA stretch, EQ match), rebuilt at run time. No audio and no game text here. '
             'Written by tools/remix-voice-table.py.',
    'rate': 11025, 'lead': 330, 'radio': radio, 'clips': dict(sorted(crcs.items())), 'lines': lines,
}
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(doc, f, indent=1)
    f.write('\n')
print(len(lines), 'lines;', sum('moments' in x for x in lines), 'said by generated jobs;', len(crcs), 'clips; wrote', os.path.normpath(OUT))
