from copy import deepcopy
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_v3_composite import citation_keys, deduplicate, group_sources, counts, bind_reviewed_context
from foundation import digest
from annotations import v3_reuse_context


def example(identifier='a', matter='W.P.(C) 123/2025', task='order_digest'):
    text = 'The Court records that the land has not yet been demarcated.'
    p = dict(passage_id='passage', source_version_id='source', matter_id=matter,
             order_date='2025-01-01', page=1, text=text, sha256='a' * 64,
             text_sha256=digest(text), url='https://delhihighcourt.nic.in/order.pdf')
    return dict(id=identifier, matter_id=matter, task=task, reuseOrigin='pilot-v1',
                state='VERIFIED_GOLD', language='English', outcome='SUPPORTED',
                target={'claims': [{'factId': 0}]}, provenance=[p], leakage_group='g',
                input={'question': 'What happened?', 'availableEvidence': [dict(
                    factId=0, text=text, category='COURT_OBSERVATION', scope='Current',
                    source=dict(orderDate='2025-01-01', page=1, evidence=text, officialUrl=p['url']))]})


def source(matter, text=None, sha=None, url=None):
    return dict(case=matter, sha256=sha or digest(matter), url=url or 'https://delhihighcourt.nic.in/' + digest(matter),
                pages={'1': 'O R D E R % 01.01.2025 ' + (text or ('A distinct hearing involving ' + matter))})


class V3CompositeTests(unittest.TestCase):
    def test_translation_and_id_change_do_not_pad_same_supervision(self):
        a, b = example(), example('b')
        b.update(language='Hindi', reuseOrigin='pilot-v2')
        b['input']['question'] = 'इस आदेश में क्या हुआ?'
        kept, duplicates = deduplicate([a, b])
        self.assertEqual(len(kept), 1)
        self.assertEqual(duplicates[0]['canonical_id'], 'a')

    def test_distinct_tasks_and_actual_order_dates_survive(self):
        a, b, c = example(), example('b', task='important_fact_selection'), example('c')
        c['provenance'][0]['order_date'] = '2025-02-01'
        self.assertEqual(len(deduplicate([a, b, c])[0]), 3)

    def test_protected_connection_excludes_transitive_train_group(self):
        a, b, c = 'W.P.(C) 123/2025', 'W.P.(C) 456/2025', 'W.P.(C) 789/2025'
        data = {'a': source(a, b), 'b': source(b, c), 'c': source(c)}
        ids, blocked, _, _ = group_sources(data, [example(matter=a)], {c})
        self.assertEqual(ids[a], ids[c])
        self.assertIn(ids[a], blocked)

    def test_same_sha_and_same_url_do_not_create_independent_groups(self):
        a, b = 'W.P.(C) 123/2025', 'W.P.(C) 456/2025'
        for override in ({'sha': 'f' * 64}, {'url': 'https://delhihighcourt.nic.in/joint.pdf'}):
            data = {'a': source(a, **override), 'b': source(b, **override)}
            ids, _, _, _ = group_sources(data, [], set())
            self.assertEqual(ids[a], ids[b])

    def test_common_authority_groups_without_same_case_caption(self):
        a, b = 'W.P.(C) 123/2025', 'W.P.(C) 456/2025'
        data = {'a': source(a, 'BSK Realtors'), 'b': source(b, 'B.S.K. Realtors')}
        ids, blocked, _, _ = group_sources(data, [], {b})
        self.assertEqual(ids[a], ids[b])
        self.assertIn(ids[a], blocked)

    def test_unconfirmed_native_boundary_fails_closed(self):
        a = 'W.P.(C) 123/2025'
        record = source(a)
        record['pages'] = {'1': 'No reliable order boundary'}
        ids, blocked, reasons, _ = group_sources({'a': record}, [], set())
        self.assertIn(ids[a], blocked)
        self.assertTrue(any(r['kind'] == 'unconfirmed_native_boundary' for r in reasons))

    def test_bind_metadata_from_review_not_target_and_preserve_original(self):
        a = example()
        before = deepcopy(a)
        p = a['provenance'][0]
        ledger = {'passage': ('source', 1, p['text'], 'COURT_OBSERVATION', 'observation', 'Current', 'Court')}
        record = source(a['matter_id'], p['text'], sha=p['sha256'], url=p['url'])
        result = bind_reviewed_context(a, ledger, {'source': record})
        self.assertEqual(a, before)
        self.assertEqual(result['target'], a['target'])
        self.assertEqual(result['provenance'], a['provenance'])
        self.assertEqual(result['input']['availableEvidence'][0]['actor'], 'Court')
        self.assertEqual(result['input']['chronologyState'][0]['directionLifecycle'], 'UNKNOWN')

    def test_unbound_or_changed_native_passage_not_reused(self):
        a = example()
        p = a['provenance'][0]
        ledger = {'passage': ('source', 1, p['text'], 'COURT_OBSERVATION', 'observation', 'Current', 'Court')}
        record = source(a['matter_id'], 'Different text', sha=p['sha256'], url=p['url'])
        with self.assertRaises(ValueError):
            bind_reviewed_context(a, ledger, {'source': record})

    def test_counter_counts_polarities_and_groups_separately(self):
        a, b = example(task='office_action_detection'), example('b', task='office_action_detection')
        b['target'] = {'claims': []}
        b['language'] = 'Hindi'
        stats = counts([a, b])
        self.assertEqual(stats['positive_lac_action'], {'examples': 1, 'groups': 1})
        self.assertEqual(stats['empty_lac_action'], {'examples': 1, 'groups': 1})
        self.assertEqual(stats['languages'], {'English': 1, 'Hindi': 1})

    def test_citation_keys_include_non_writ_connections_and_court_citation(self):
        keys = citation_keys('CONT.CAS(C) 777/2019 LPA 123/2024 CS(OS) 12/2022 Civil Appeal No. 44 of 2023 2025 INSC 321')
        self.assertTrue({'CONT.CAS(C) 777/2019', 'LPA 123/2024', 'CS(OS) 12/2022', 'CIVIL_APPEAL 44/2023'}.issubset(keys))
        self.assertIn('JUDGMENT 2025 insc 321', keys)

    def test_wrong_order_date_and_punctuation_change_fail_closed(self):
        a = example()
        p = a['provenance'][0]
        ledger = {'passage': ('source', 1, p['text'], 'COURT_OBSERVATION', 'observation', 'Current', 'Court')}
        record = source(a['matter_id'], p['text'], sha=p['sha256'], url=p['url'])
        for change in (record['pages']['1'].replace('01.01.2025', '02.01.2025'),
                       record['pages']['1'].replace('demarcated.', 'demarcated?')):
            with self.assertRaises(ValueError):
                bind_reviewed_context(a, ledger, {'source': dict(record, pages={'1': change})})

    def test_lifecycle_never_inferred_from_later_silence(self):
        a = example()
        a['provenance'].append(dict(a['provenance'][0], order_date='2025-02-01', passage_id='later'))
        a['input']['availableEvidence'].append(deepcopy(a['input']['availableEvidence'][0]))
        # No target is needed or permitted as a basis for enrichment.
        a.pop('target')
        with patch.object(v3_reuse_context, 'OPEN_AS_ISSUED', {'passage'}), \
             patch.object(v3_reuse_context, 'SUPERSEDED_AT', {}), \
             patch.object(v3_reuse_context, 'CONTINUES_THROUGH', {}):
            enriched = v3_reuse_context.enrich(a, {}, {})
        self.assertEqual(enriched['input']['availableEvidence'][0]['directionLifecycle'], 'UNKNOWN')

    def test_explicit_reviewed_renewal_not_invented_completion(self):
        a = example()
        a['provenance'].append(dict(a['provenance'][0], order_date='2025-02-01', passage_id='later'))
        a['input']['availableEvidence'].append(deepcopy(a['input']['availableEvidence'][0]))
        with patch.object(v3_reuse_context, 'OPEN_AS_ISSUED', {'passage'}), \
             patch.object(v3_reuse_context, 'SUPERSEDED_AT', {'passage': '2025-02-01'}):
            enriched = v3_reuse_context.enrich(a, {}, {})
        self.assertEqual(enriched['input']['availableEvidence'][0]['directionLifecycle'], 'SUPERSEDED')
        self.assertEqual(enriched['target'], a['target'])
        self.assertNotEqual(enriched['input']['availableEvidence'][0]['directionLifecycle'], 'COMPLETED')

    def test_actor_context_is_exact_same_page_and_keeps_original_evidence(self):
        a = example()
        text = a['provenance'][0]['text']
        before = 'LAC shall attend the meeting.'
        ledger = {'adjacent': ('source', 1, before, 'COURT_DIRECTION', 'direction', 'Current', 'LAC'),
                  'passage': ('source', 1, text, 'COURT_OBSERVATION', 'observation', 'Current', 'Court')}
        native = {'source': {'pages': {'1': before + ' ' + text}}}
        with patch.object(v3_reuse_context, 'ACTOR_CONTEXT', {'passage': ('adjacent', 'passage')}):
            result = v3_reuse_context.enrich(a, ledger, native)
            self.assertEqual(result['input']['availableEvidence'][0]['source']['evidence'], before + ' ' + text)
            self.assertEqual(result['input']['availableEvidence'][0]['originalSourceEvidence'], text)
            self.assertEqual(result['provenance'], a['provenance'])
            self.assertEqual(result['target'], a['target'])
            ledger['adjacent'] = ('source', 2, before, 'COURT_DIRECTION', 'direction', 'Current', 'LAC')
            with self.assertRaises(ValueError):
                v3_reuse_context.enrich(a, ledger, native)


if __name__ == '__main__':
    unittest.main()
