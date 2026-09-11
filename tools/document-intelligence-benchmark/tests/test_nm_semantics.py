import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "document-intelligence-worker"))
from benchmark.nm_semantics import NmToken, detect_column_schema, semantic_owner_blocks


def token(text, x, y=10):
    return NmToken(1, text, x, y, max(30, len(text) * 5), 12)


class NmSemanticsTests(unittest.TestCase):
    def test_column_tokens_do_not_contaminate_owner_name(self):
        values = [token("Name of Owner", 20), token("Khasra No", 300), token("Area", 430), token("Grand Total", 620), token("Ramesh Kumar S/o Mohan", 20, 80), token("12//2", 300, 80), token("0-14", 430, 80), token("Rs. 200", 620, 80)]
        schema = detect_column_schema(1, values[:4], 800)
        block = semantic_owner_blocks(values[4:], schema)[0]
        self.assertEqual("Ramesh Kumar", block.recorded_name)
        self.assertEqual("Mohan", block.parentage)
        self.assertEqual("12//2", block.parcels[0].raw_khasra)
        self.assertEqual("0-14", block.parcels[0].raw_area)
        self.assertNotIn("12//2", block.recorded_name)

    def test_ditto_inherits_parcels_only(self):
        values = [token("Name of Owner", 20), token("Khasra No", 300), token("Area", 430), token("Grand Total", 620), token("Ramesh Kumar S/o Mohan", 20, 80), token("1/3", 20, 96), token("12//2", 300, 80), token("0-14", 430, 80), token("Rs. 200", 620, 80), token("Suresh Kumar S/o Hari", 20, 120), token("1/6", 20, 136), token("-do-", 300, 120), token("Rs. 100", 620, 120)]
        schema = detect_column_schema(1, values[:4], 800)
        first, second = semantic_owner_blocks(values[4:], schema)
        self.assertEqual("Ramesh Kumar", first.recorded_name)
        self.assertEqual("Suresh Kumar", second.recorded_name)
        self.assertTrue(second.parcels[0].inherited)
        self.assertEqual("12//2", second.parcels[0].raw_khasra)
        self.assertNotEqual(first.parentage, second.parentage)
        self.assertEqual("1/3", first.share_raw)
        self.assertEqual("1/6", second.share_raw)
        self.assertEqual("-do-", second.ditto_token.text)
        self.assertEqual(first.parcels[0].khasra_token.region(), second.parcels[0].khasra_token.region())

    def test_each_core_fact_retains_its_own_source_token(self):
        values = [token("Name of Owner", 20), token("Khasra No", 300), token("Area", 430), token("Grand Total", 620), token("Ramesh Kumar S/o Mohan", 20, 80), token("1/3", 20, 96), token("12//2", 300, 80), token("0-14", 430, 80), token("Rs. 200", 620, 80)]
        schema = detect_column_schema(1, values[:4], 800)
        block = semantic_owner_blocks(values[4:], schema)[0]
        self.assertEqual("Ramesh Kumar S/o Mohan", block.name_token.text)
        self.assertEqual("1/3", block.share_token.text)
        self.assertEqual("12//2", block.parcels[0].khasra_token.text)
        self.assertEqual("0-14", block.parcels[0].area_token.text)
        self.assertEqual("Rs. 200", block.components["grand_total"][1].text)

    def test_ambiguous_ditto_is_exception(self):
        values = [token("Name of Owner", 20), token("Khasra No", 300), token("Area", 430), token("Grand Total", 620), token("Ramesh Kumar S/o Mohan", 20, 80), token("-do-", 300, 80), token("1/3", 20, 96)]
        schema = detect_column_schema(1, values[:4], 800)
        block = semantic_owner_blocks(values[4:], schema)[0]
        self.assertIn("AmbiguousDittoScope", block.exceptions)

    def test_missing_share_blocks_auto_structured(self):
        values = [token("Name of Owner", 20), token("Khasra No", 300), token("Area", 430), token("Grand Total", 620), token("Ramesh Kumar S/o Mohan", 20, 80), token("12//2", 300, 80), token("0-14", 430, 80)]
        schema = detect_column_schema(1, values[:4], 800)
        block = semantic_owner_blocks(values[4:], schema)[0]
        self.assertIn("MissingRequiredSourceEvidence", block.exceptions)
        self.assertFalse(block.auto_structured)

    def test_missing_schema_never_creates_auto_structured_block(self):
        self.assertEqual([], semantic_owner_blocks([token("Ramesh Kumar S/o Mohan", 20)], None))


if __name__ == "__main__":
    unittest.main()
