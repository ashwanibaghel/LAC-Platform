"""Source-only V3 additions to frozen TRAIN context; never alter legacy labels.

Reviewed against existing native pages on 2026-10-03. OPEN means an operative
requirement in the bounded supplied-order snapshot, NOT live office completion
status. No deadline expiry/silence is used to infer completion. Actor evidence
is a bounded contiguous exact passage on the SAME page, when expressly needed.
Unknown chains stay UNKNOWN. No validation/blind targets or model output used.
"""
REVIEW = {'reviewed_on': '2026-10-03', 'method': 'Existing native pages plus original independent source annotations',
          'version': 'v3-reused-train-context-2', 'targets_changed': False}

# Explicit new/renewed directions and listing instructions from supplied TRAIN
# sources. No role/field changes: a filing direction remains field=filing.
OPEN_AS_ISSUED = {
    'wpc4806-2014-feb2020-p1', 'wpc4806-2014-apr2022-p1',
    'wpc4806-2014-p2', 'wpc4806-2014-p3', 'wpc4806-2014-p4',
    'wpc9093-2022-p2', 'wpc9093-2022-p3', 'wpc9093-2022-p4', 'wpc9093-2022-p6',
    'wpc6108-2015-nov2025-p0', 'wpc6108-2015-nov2025-p1',
    'wpc6108-2015-feb25-p3', 'wpc6108-2015-feb25-p4',
    'wpc6108-2015-p1', 'wpc4255-2016-p1',
    'wpc13932-2025-p4', 'wpc13932-2025-p5',
}

# Later explicit renewal replaces these earlier opportunities in this exact
# supplied chain. No automatic "latest date implies superseded" rule.
SUPERSEDED_AT = {
    'wpc4806-2014-feb2020-p1': '2022-04-11',
    'wpc4806-2014-apr2022-p1': '2026-02-07',
}

# February templates are expressly renewed on March 20, with unchanged April 30
# deadline. November Master List is a DIFFERENT duty: later silence cannot mark
# it completed, superseded or conclusively still active.
CONTINUES_THROUGH = {
    'wpc6108-2015-feb25-p3': '2026-03-20',
    'wpc6108-2015-feb25-p4': '2026-03-20',
}

# Exact same-page expanded excerpts bind dependent actor/condition statements.
# Original bounded provenance and target text are preserved unchanged.
ACTOR_CONTEXT = {
    'wpc4806-2014-p3': ('wpc4806-2014-p2', 'wpc4806-2014-p3'),
    'wpc9093-2022-p3': ('wpc9093-2022-p2', 'wpc9093-2022-p3'),
}

# Paragraphs 5-7 establish the service evidence and the Court's conclusion,
# including the distinction between service affidavits and publication service.
# Review is attached to the source passage, not any question/selected answer.
# This is service completion only, never completed LAC filing or impleadment.
NATIVE_CONTEXT = {
    'wpc13932-2025-feb03-p1': (
        'wpc13932-2025-feb03', 2,
        '5. Affidavits of service were filed in respect of service of all the above Respondents except Respondent Nos.4, 7, 15, 16.',
        '7. Thus, all the original owners stood served in terms of the amended memo parties attached with application CM APPL. 61807/2025.',
    ),
}


def enrich(example, passages, source_records):
    """No target read. Bind explicit source-reviewed context to supplied facts."""
    from copy import deepcopy
    import re
    result = deepcopy(example)
    if 'availableEvidence' not in result['input']:
        return result
    as_of = max(p['order_date'] for p in result['provenance'])
    for p, entry in zip(result['provenance'], result['input']['availableEvidence']):
        pid = p['passage_id']
        if pid in NATIVE_CONTEXT:
            version, page, first, last = NATIVE_CONTEXT[pid]
            if version != p['source_version_id'] or page != p['page']:
                raise ValueError('Native context must stay on the exact source/page')
            native = re.sub(r'\s+', ' ', source_records[version]['pages'][str(page)]).strip()
            start, end = native.find(first), native.find(last)
            if start < 0 or end < start:
                raise ValueError('Reviewed native context not found on exact source page')
            excerpt = native[start:end + len(last)]
            if len(excerpt) > 900 or p['text'] not in excerpt:
                raise ValueError('Native context is not bounded/source-confirmed')
            entry['source']['evidence'] = excerpt
            entry['originalSourceEvidence'] = p['text']
            entry['sourceContextReview'] = REVIEW['version']
        if pid in OPEN_AS_ISSUED:
            state = 'OPEN' if as_of == p['order_date'] else 'UNKNOWN'
            if pid in SUPERSEDED_AT and as_of >= SUPERSEDED_AT[pid]:
                state = 'SUPERSEDED'
            if pid in CONTINUES_THROUGH and p['order_date'] <= as_of <= CONTINUES_THROUGH[pid]:
                state = 'OPEN'
            entry.update(directionLifecycle=state, lifecycleAsOf=as_of,
                         lifecycleBasis='Bounded supplied-order review; not current canonical office status')
        if pid in ACTOR_CONTEXT:
            first, last = (passages[x] for x in ACTOR_CONTEXT[pid])
            if first[0] != last[0] or first[1] != last[1] or last[0] != p['source_version_id']:
                raise ValueError('Actor context must stay on same exact source/page')
            native = re.sub(r'\s+', ' ', source_records[first[0]]['pages'][str(first[1])]).strip()
            start, end = native.find(first[2]), native.find(last[2])
            if start < 0 or end < start:
                raise ValueError('Adjacent actor context not found in exact native source')
            excerpt = native[start:end + len(last[2])]
            if len(excerpt) > 900 or p['text'] not in excerpt:
                raise ValueError('Expanded actor context not bounded/source-confirmed')
            entry['source']['evidence'] = excerpt
            entry['originalSourceEvidence'] = p['text']
            entry['actorContextReview'] = REVIEW['version']
    return result
