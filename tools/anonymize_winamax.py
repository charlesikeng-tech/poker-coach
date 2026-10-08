#!/usr/bin/env python3
"""Anonymize Winamax hand-history and tournament-summary files before committing them as golden files.

Player names are replaced by deterministic pseudonyms (same input name -> same pseudonym across all
files of one run). The hero becomes "Hero". Pseudonyms keep the characteristics that matter to the
parser: names with spaces stay with spaces, names with a leading dot keep it, names with dashes or
dots keep one. Everything else (amounts, cards, ids, timestamps) is left untouched.

Names are only replaced where the format puts a player name, never by blind text substitution, so a
player called "Ace" cannot corrupt "High card : Ace". The script fails if any original name is still
present in the output.

Usage: anonymize_winamax.py --hero "<hero name>" --out <dir> <file> [<file> ...]
"""
import argparse
import pathlib
import re
import sys

SEAT = re.compile(r'^(Seat \d+: )(.+?)( \(\d+(?:, [\d.]+€ bounty)?\))$')
SUMMARY_SEAT = re.compile(r'^(Seat \d+: )(.+?)( \((?:button|small blind|big blind)\))?( (?:won|showed) .*)$')
DEALT = re.compile(r'^(Dealt to )(.+?)( \[[^\]]+\])$')
PLAYER_LINE = re.compile(
    r'^(.+?)( (?:folds|checks|calls \d+|bets \d+|raises \d+ to \d+|posts (?:ante|small blind|big blind) \d+'
    r'|shows \[|collected \d+ from|mucks|sits out|is (?:dis)?connected).*)$')
SUMMARY_PLAYER = re.compile(r'^(Player : )(.+)$')


def pseudonym_factory(hero: str):
    mapping = {hero: 'Hero'}

    def get(name: str) -> str:
        if name not in mapping:
            n = len(mapping)
            if ' ' in name:
                alias = f'Villain {n:02d} x'
            elif name.startswith('.'):
                alias = f'.Villain{n:02d}'
            elif '-' in name:
                alias = f'Villain-{n:02d}'
            elif '.' in name:
                alias = f'Villain.{n:02d}'
            else:
                alias = f'Villain{n:02d}'
            mapping[name] = alias
        return mapping[name]

    return mapping, get


def collect_names(lines):
    names = set()
    for line in lines:
        for pattern in (SEAT, DEALT, SUMMARY_PLAYER):
            m = pattern.match(line)
            if m:
                names.add(m.group(2))
    return names


def anonymize_line(line, names, get):
    for pattern in (SEAT, DEALT, SUMMARY_PLAYER):
        m = pattern.match(line)
        if m and m.group(2) in names:
            return m.group(1) + get(m.group(2)) + ''.join(g or '' for g in m.groups()[2:])
    m = SUMMARY_SEAT.match(line)
    if m and m.group(2) in names:
        return m.group(1) + get(m.group(2)) + (m.group(3) or '') + m.group(4)
    # Player-prefixed action lines: match the longest known name, not a regex guess.
    for name in sorted(names, key=len, reverse=True):
        if line.startswith(name + ' ') and PLAYER_LINE.match(line):
            return get(name) + line[len(name):]
    return line


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--hero', required=True)
    parser.add_argument('--out', required=True, type=pathlib.Path)
    parser.add_argument('files', nargs='+', type=pathlib.Path)
    args = parser.parse_args()

    sources = {f: f.read_text(encoding='utf-8') for f in args.files}
    names = set()
    for content in sources.values():
        names |= collect_names(content.split('\n'))
    names.add(args.hero)

    mapping, get = pseudonym_factory(args.hero)
    args.out.mkdir(parents=True, exist_ok=True)
    for path, content in sources.items():
        output = '\n'.join(anonymize_line(line, names, get) for line in content.split('\n'))
        # Fails closed: a short name that also appears as ordinary text (e.g. "Ace") needs a human look.
        leaked = sorted(n for n in names if n in output)
        if leaked:
            print(f'{path.name}: original names still present: {leaked}', file=sys.stderr)
            return 1
        target = args.out / path.name.split('-', 1)[-1] if re.match(r'^[0-9a-f]{8}-', path.name) else args.out / path.name
        target.write_text(output, encoding='utf-8', newline='\n')
        print(f'{path.name} -> {target}')
    print(f'{len(mapping)} players anonymized')
    return 0


if __name__ == '__main__':
    sys.exit(main())
