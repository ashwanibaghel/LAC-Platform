import copy
import unittest
from anchors import anchors_for, expand
from preselection import select_candidates, bounded_chunks


class PreselectionTests(unittest.TestCase):
    def test_exact_metadata_and_adjacent_context_are_not_rewritten(self):
        pages = {1: 'The petitioner submits compensation was paid. '*30,
                 2: 'The previous order reads: “The LAC shall file a report.” The Court directs the LAC to examine documents.'}
        anchors = anchors_for(pages)
        before = copy.deepcopy(anchors)
        selected, audit = select_candidates(anchors, pages, budget=6)
        self.assertEqual(before, anchors)
        self.assertTrue(all(a in before for a in selected))
        self.assertGreater(audit['omittedAnchorCount'], 0)
        self.assertFalse(audit['wholeDocumentCoverage'])
        self.assertNotIn('facts', audit)

    def test_current_operative_overflow_is_not_silently_dropped(self):
        pages = {1: ' '.join(f'{n}. The LAC shall file report {n}.' for n in range(1, 32))}
        anchors = anchors_for(pages)
        selected, audit = select_candidates(anchors, pages, budget=24)
        self.assertEqual(len(anchors), len(selected))
        self.assertTrue(audit['criticalBudgetOverflow'])

    def test_quoted_direction_still_fails_current_guard(self):
        pages = {1: 'The previous order reads: “The compensation is disputed. The LAC shall forward the reference.”'}
        selected, _ = select_candidates(anchors_for(pages), pages)
        quoted = next(a for a in selected if a['quoted'])
        with self.assertRaisesRegex(ValueError, 'source quotation'):
            expand({'facts': [{'anchorId': quoted['anchorId'], 'category': 'COURT_DIRECTION',
                              'field': 'referenceToAdj', 'scope': 'Current'}], 'needsReview': False}, selected, pages)

    def test_final_disposition_and_conditional_lac_candidates_survive_noise(self):
        pages = {1: ' '.join(f'{n}. An earlier award record concerns a village.' for n in range(1, 40)),
                 2: 'The LAC submits that the compensation remains disputed.',
                 3: 'After examining documents, the LAC may release compensation. If required, the LAC to make a reference. The petition is disposed of.'}
        anchors = anchors_for(pages)
        selected, audit = select_candidates(anchors, pages)
        for a in anchors:
            if a['page'] in (2, 3): self.assertIn(a, selected)
        self.assertLessEqual(len(bounded_chunks(selected)), 4)
        self.assertEqual(20, audit['selectedAnchorCount'])

    def test_small_sources_remain_unchanged_and_selection_deterministic(self):
        pages = {1: 'The petitioner seeks compensation. The LAC shall file a reply.'}
        anchors = anchors_for(pages)
        self.assertEqual(anchors, select_candidates(anchors, pages)[0])
        self.assertEqual(select_candidates(anchors, pages), select_candidates(anchors, pages))

    def test_petition_relief_survives_final_page_priority(self):
        pages = {1: 'The present petition seeks directions for compensation under the Act. The petitioner submits possession remains disputed.',
                 2: ' '.join('The old award concerns compensation records.' for _ in range(40)),
                 3: 'The LAC submits compensation remains unpaid. The petition is disposed of.'}
        anchors = anchors_for(pages)
        selected, _ = select_candidates(anchors, pages)
        for anchor in anchors:
            if anchor['page'] == 1:
                self.assertIn(anchor, selected)


if __name__ == '__main__': unittest.main()
