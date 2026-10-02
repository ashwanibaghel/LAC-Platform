from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from acquire_v3_sources import excluded
from v3_candidates import CANDIDATES
from semantics import identity


class V3AcquisitionTests(unittest.TestCase):
    def test_reservations_exclude_all_used_v1_v2_and_known_related_matters(self):
        protected = excluded()
        self.assertTrue(protected)
        for _, case, _ in CANDIDATES:
            self.assertNotIn(identity(case), protected)
        for case in ('W.P.(C) 6384/2024', 'W.P.(C) 847/2025', 'W.P.(C) 940/2015',
                     'W.P.(C) 2686/2018', 'W.P.(C) 9093/2022'):
            self.assertIn(identity(case), protected)

    def test_five_candidate_holdouts_reserved_before_outputs_not_gold(self):
        # Includes replacement reservations, NOT five certified/frozen groups.
        self.assertGreaterEqual(sum(split == 'validation' for _, _, split in CANDIDATES), 5)
        self.assertGreaterEqual(sum(split == 'blind' for _, _, split in CANDIDATES), 5)
        self.assertEqual(len(CANDIDATES), len({identifier for identifier, _, _ in CANDIDATES}))
        self.assertEqual(len(CANDIDATES), len({identity(case) for _, case, _ in CANDIDATES}))

    def test_acquisition_no_model_or_application_action(self):
        source = (Path(__file__).resolve().parents[1] / 'acquire_v3_sources.py').read_text()
        self.assertIn("record['training_eligible'] = False", source)
        self.assertIn("record['audit_state'] = 'UNREVIEWED_NOT_GOLD'", source)
        self.assertNotIn('provider.extract(', source)
        self.assertNotIn('.post(', source)
        self.assertNotIn('localhost', source)

    def test_native_decision_date_never_uses_upload_or_signing_date(self):
        from build_v3_targeted import native_order_date
        self.assertEqual(native_order_date('Date of decision: 13th February, 2026 Uploaded on: 17th February, 2026'), '2026-02-13')
        self.assertEqual(native_order_date('O R D E R % 09.01.2026 Downloaded on 03/10/2026'), '2026-01-09')
        with self.assertRaises(ValueError):
            native_order_date('Signing Date:17.02.2026 Uploaded on: 17th February, 2026')


if __name__ == '__main__':
    unittest.main()
