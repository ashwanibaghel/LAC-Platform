"""Source-only V3 additions to frozen TRAIN context; never alter legacy labels.

Reviewed against existing native pages on 2026-10-03. OPEN means an operative
requirement in the bounded supplied-order snapshot, NOT live office completion
status. No deadline expiry/silence is used to infer completion. Actor evidence
is a bounded contiguous exact passage on the SAME page, when expressly needed.
Unknown chains stay UNKNOWN. No validation/blind targets or model output used.
"""
REVIEW = {'reviewed_on': '2026-10-03', 'method': 'Existing native pages plus original independent source annotations',
          'version': 'v3-reused-train-context-3', 'targets_changed': False}

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
    'wpc10308-2024-p3', 'wpc10308-2024-p4',
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
    'wpc6108-2015-nov2025-p1': ('wpc6108-2015-nov2025-p0', 'wpc6108-2015-nov2025-p1'),
}

# Paragraphs 5-7 establish the service evidence and the Court's conclusion,
# including the distinction between service affidavits and publication service.
# Review is attached to the source passage, not any question/selected answer.
# This is service completion only, never completed LAC filing or impleadment.
NATIVE_CONTEXT = {
    'wpc6108-2015-feb25-p1': (
        'wpc6108-2015-feb25', 37,
        '3. In terms of the above order, Ms. Smita Mann, ld. Counsel for the Petitioner and Ms. Malvika Kapila, ld. Counsel for the Respondent/Delhi Development Authority (hereinafter, ‘DDA’) were directed to prepare a ‘Blank Template’, after mutual consultation, so that all the basic facts in each of these matters can be compiled and understood in an easy manner, at the time of final hearing.',
        '4. Today, both the ld. Counsels have placed before the Court their respective ‘Blank Template’.',
    ),
    'wpc13932-2025-feb03-p1': (
        'wpc13932-2025-feb03', 2,
        '5. Affidavits of service were filed in respect of service of all the above Respondents except Respondent Nos.4, 7, 15, 16.',
        '7. Thus, all the original owners stood served in terms of the amended memo parties attached with application CM APPL. 61807/2025.',
    ),
}

# This cited earlier paragraph identifies the service actor and original-owner
# group; it is NOT selectable completion evidence. Actual service completion is
# only the original later paragraph. No future/other-case actor inference.
SUPPORT_CONTEXT = {
    'wpc13932-2025-feb03-p1': (
        'wpc13932-2025-feb03', 1,
        '3. Vide order dated 10th September, 2025, the submission of ld. Sr. Counsel for the Petitioner had been recorded that the original owners of the property would be impleaded in this case. Accordingly, the Petitioner had then filed three applications.',
    ),
}


def enrich(example, passages, source_records):
    """No target read. Bind explicit source-reviewed context to supplied facts."""
    from copy import deepcopy
    import re
    result = deepcopy(example)
    # All windows come from exact native pages, independent of selected target
    # IDs. In particular, unselected paragraphs remain in the actual adjacency.
    if 'anchors' in result['input']:
        windows = {}
        for p, anchor in zip(result['provenance'], result['input']['anchors']):
            native = re.sub(r'\s+', ' ', source_records[p['source_version_id']]['pages'][str(p['page'])]).strip()
            start = native.find(p['text'])
            if start < 0:
                raise ValueError('Exact native anchor unavailable')
            prefix = native[max(0, start - 240):start]
            excerpt = prefix + p['text']
            if len(excerpt) > 900:
                raise ValueError('Native anchor context exceeds source bound')
            anchor['precedingContext'] = prefix
            windows[str(anchor['anchorId'])] = dict(page=p['page'], evidence=excerpt,
                                                  sha256=p['sha256'], orderDate=p['order_date'])
        result['input']['sourceWindows'] = windows
        result['input']['sourceSha256'] = result['provenance'][0]['sha256']
        return result
    if 'availableEvidence' not in result['input']:
        return result
    as_of = max(p['order_date'] for p in result['provenance'])
    for p, entry in zip(result['provenance'], result['input']['availableEvidence']):
        pid = p['passage_id']
        entry['source']['sha256'] = p['sha256']
        record = source_records.get(p['source_version_id'])
        if not record or str(p['page']) not in record['pages']:
            raise ValueError('Exact native claim source/page unavailable')
        native = re.sub(r'\s+', ' ', record['pages'][str(p['page'])]).strip()
        start = native.find(p['text'])
        if start < 0:
            raise ValueError('Exact claim native passage unavailable')
        # Bounded native prefix is source-only actor/attribution context, not
        # an additional selectable fact or substitute performance sentence.
        prefix = native[max(0, start - 420):start]
        entry['sourceContext'] = [dict(page=p['page'], evidence=prefix + p['text'],
            sha256=p['sha256'], officialUrl=p['url'], orderDate=p['order_date'])] if prefix and len(prefix + p['text']) <= 900 else []
        if pid in SUPPORT_CONTEXT:
            version, page, text = SUPPORT_CONTEXT[pid]
            if version != p['source_version_id'] or not 0 < len(text) <= 900 or text not in re.sub(
                    r'\s+', ' ', source_records[version]['pages'][str(page)]).strip():
                raise ValueError('Reviewed support context source binding failed')
            entry['sourceContext'].append(dict(page=page, evidence=text, sha256=p['sha256'],
                                               officialUrl=p['url'], orderDate=p['order_date']))
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
