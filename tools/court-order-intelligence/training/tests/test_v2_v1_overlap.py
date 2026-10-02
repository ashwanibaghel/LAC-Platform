"""Protected regression/fresh-evaluation boundaries; synthetic sources only."""
import sys
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT.parent))
from audit_v2_v1_overlap import compare, bounded_duplicate
from leakage import near_duplicate


def source(case, split, text, sha):
    return {'case': case, 'reserved_split': split, 'sha256': sha,
            'pages': {'1': case + ' O R D E R % 01.01.2026 ' + text}}


class V1Boundary(unittest.TestCase):
    def test_v2_blind_cannot_overlap_v1_training(self):
        old = source('W.P.(C) 1/2020', 'train', 'Let the LAC file a status report within four weeks.', 'a')
        new = source('W.P.(C) 2/2020', 'blind', 'Let the LAC file a status report within four weeks.', 'b')
        result = compare({'new': new}, {'old': old}, {old['case']: {'split': 'train'}})
        self.assertTrue(result[0]['protected_boundary_violation'])

    def test_v2_training_cannot_overlap_v1_blind(self):
        old = source('W.P.(C) 1/2020', 'blind', 'Earlier matter W.P.(C) 2/2020.', 'a')
        new = source('W.P.(C) 2/2020', 'train', 'Different order text.', 'b')
        result = compare({'new': new}, {'old': old}, {old['case']: {'split': 'blind'}})
        self.assertTrue(result[0]['protected_boundary_violation'])

    def test_train_to_train_is_flagged_not_passed_as_fresh(self):
        old = source('W.P.(C) 1/2020', 'train', 'Order text.', 'a')
        new = source('W.P.(C) 1/2020', 'train', 'New order.', 'b')
        result = compare({'new': new}, {'old': old}, {old['case']: {'split': 'train'}})
        self.assertFalse(result[0]['protected_boundary_violation'])
        self.assertTrue(result[0]['fresh_training_requires_exclusion_or_review'])
        self.assertEqual(new['reserved_split'], 'train')

    def test_upper_bound_optimization_preserves_duplicate_decision(self):
        samples = ['Let the LAC file an affidavit within four weeks.',
                   ' '.join('word' + str(i) for i in range(250)), '',
                   'Let the LAC file an affidavit within three weeks.']
        for a in samples:
            for b in samples:
                self.assertEqual(bounded_duplicate(a, b), near_duplicate(a, b))


if __name__ == '__main__':
    unittest.main()
