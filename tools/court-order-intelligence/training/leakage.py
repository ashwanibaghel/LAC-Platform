"""Conservative deterministic matter/source/relationship/passage grouping."""
from collections import defaultdict
from difflib import SequenceMatcher
import re
from foundation import digest


def normalized(text):
    return re.sub(r"\W+", " ", text.casefold()).strip()


def shingles(text):
    words = normalized(text).split()
    return {tuple(words[i:i + 4]) for i in range(max(0, len(words) - 3))}


def near_duplicate(a, b):
    # Exact duplicates always group, even short directions. Approximate grouping
    # is deliberately conservative; false-positive overgrouping is safer than
    # letting a repeated operative passage cross evaluation boundaries.
    if normalized(a) == normalized(b):
        return True
    left, right = shingles(a), shingles(b)
    return bool(len(left) >= 8 and len(right) >= 8 and (
        len(left & right) / len(left | right) >= .65 or
        SequenceMatcher(None, normalized(a), normalized(b), autojunk=False).ratio() >= .9))


def groups(examples, matters):
    parent = {e["matter_id"]: e["matter_id"] for e in examples}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        a, b = find(a), find(b)
        parent[max(a, b)] = min(a, b)

    for matter in parent:
        for related in matters[matter].get("connections", []):
            if related in parent:
                union(matter, related)
    owners, common = {}, {}
    passages = {}
    for example in examples:
        matter = example["matter_id"]
        for judgment in matters[matter].get("common_judgments", []):
            if judgment in common:
                union(matter, common[judgment])
            common[judgment] = matter
        for p in example["provenance"]:
            if p["sha256"] in owners:
                union(matter, owners[p["sha256"]])
            owners[p["sha256"]] = matter
            passages[(matter, p["text"])] = True
    pairs = sorted(passages)
    for i, (a, text) in enumerate(pairs):
        for b, other in pairs[i + 1:]:
            if a != b and near_duplicate(text, other):
                union(a, b)
    members = defaultdict(list)
    for matter in sorted(parent):
        members[find(matter)].append(matter)
    ids = {matter: "group-" + digest("\n".join(group))[:16]
           for group in members.values() for matter in group}
    return {example["id"]: ids[example["matter_id"]] for example in examples}


def require_one_split(group_by_example, split_by_example):
    assigned = {}
    for key, group in group_by_example.items():
        split = split_by_example[key]
        if group in assigned and assigned[group] != split:
            raise ValueError("Leakage group occurs across different splits")
        assigned[group] = split
