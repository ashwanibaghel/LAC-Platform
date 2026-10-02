"""Read-only native-source boundary audit against the immutable V1 corpus.

V2 evaluation must be unseen by BOTH adapters. No split is moved to resolve a
collision. Report conservative body duplicates for exclusion/manual review.
"""
import json
from audit_v2_foundation import ROOT, related_identities
from audit_pilot_overlap import body
from foundation import write_json
from leakage import near_duplicate, normalized, shingles


def ordered_body(record):
    return body({**record, 'pages': {k: record['pages'][k] for k in sorted(record['pages'], key=int)}})


def bounded_duplicate(left, right):
    """Skip impossible comparisons without changing frozen duplicate thresholds.

    SequenceMatcher ratio cannot exceed 2*min(length)/(sum lengths). Jaccard
    cannot exceed min(set sizes)/max(set sizes). Large combined orders versus
    single-page orders therefore need no expensive character alignment.
    """
    a, b = normalized(left), normalized(right)
    if a == b:
        return True
    x, y = shingles(left), shingles(right)
    if len(x) < 8 or len(y) < 8:
        return False
    if min(len(x), len(y)) / max(len(x), len(y)) < .65 and 2 * min(len(a), len(b)) / (len(a) + len(b)) < .9:
        return False
    return near_duplicate(left, right)


def compare(v2, v1, inventory):
    left = [(v, r, related_identities(r), ordered_body(r)) for v, r in sorted(v2.items())]
    right = [(v, r, related_identities(r), ordered_body(r)) for v, r in sorted(v1.items())]
    collisions = []
    for version, record, cited, text in left:
        for previous, old, old_cited, old_text in right:
            same_case = record['case'] == old['case']
            common = sorted(cited & old_cited)
            same_sha = record['sha256'] == old['sha256']
            duplicate = None if same_case or common or same_sha else bounded_duplicate(text, old_text)
            if same_case or common or same_sha or duplicate:
                old_split = inventory[old['case']]['split']
                blocked = record['reserved_split'] != 'train' or old_split != 'train'
                collisions.append({'v2_source': version, 'v1_source': previous,
                    'v2_split': record['reserved_split'], 'v1_split': old_split,
                    'same_case': same_case, 'common_identities': common,
                    'same_sha': same_sha, 'near_duplicate_body': duplicate,
                    'protected_boundary_violation': blocked,
                    'fresh_training_requires_exclusion_or_review': not blocked})
    return collisions


def main():
    load = lambda folder: {p.stem: json.loads(p.read_text(encoding='utf-8')) for p in folder.glob('*.json')}
    v2 = load(ROOT / 'local-private/pilot-v2-source-audit')
    v1 = load(ROOT / 'local-private/pilot-source-audit')
    from annotations.pilot_review import SOURCES
    if set(v1) != set(SOURCES):
        raise ValueError('Complete frozen V1 native-source inventory is required')
    for version, source in SOURCES.items():
        if v1[version]['sha256'] != source['sha256']:
            raise ValueError('Frozen V1 source SHA differs; do not overwrite it')
    inventory = json.loads((ROOT / 'pilot-v1/splits.json').read_text())['matter_inventory']['matters']
    collisions = compare(v2, v1, inventory)
    report = {'v1_sources': len(v1), 'v2_sources': len(v2), 'collisions': collisions,
              'protected_boundary_pass': not any(c['protected_boundary_violation'] for c in collisions),
              'fresh_training_overlap_pass': not collisions, 'sources_or_splits_modified': False}
    write_json(ROOT / 'pilot-v2/v1-native-overlap-audit.json', report)
    print(json.dumps(report, indent=2))
    if not report['protected_boundary_pass']:
        raise ValueError('Protected native-source overlap; GPU launch remains prohibited')


if __name__ == '__main__':
    main()
