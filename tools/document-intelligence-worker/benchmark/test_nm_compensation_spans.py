import unittest

from decimal import Decimal

from nm_semantics import NmColumnSchema, NmToken, detect_column_schema, normalize_money_amount, semantic_owner_blocks


SCHEMA = NmColumnSchema(1, (
    ("owner", 0, 200), ("land_compensation", 200, 300),
    ("solatium", 300, 400), ("additional_compensation", 400, 500),
    ("interest", 500, 600), ("grand_total", 600, 800),
), "test")


def token(text, x, y):
    return NmToken(1, text, x, y, 40, 10)


class CompensationSpanTests(unittest.TestCase):
    def test_header_phrases_create_independent_structure_total_and_statutory_bands(self):
        headers = [
            NmToken(1, "Owner", 10, 10, 50, 10), NmToken(1, "Khasra", 120, 10, 60, 10), NmToken(1, "Area", 220, 10, 40, 10),
            NmToken(1, "Land", 500, 10, 40, 10), NmToken(1, "Compensation", 550, 12, 90, 10),
            NmToken(1, "Structure etc", 650, 10, 90, 10), NmToken(1, "Compensation", 750, 12, 90, 10),
            NmToken(1, "Total", 870, 10, 45, 10), NmToken(1, "Solatium @ 30%", 950, 10, 100, 10),
            NmToken(1, "Compensation a12%", 1060, 10, 130, 10), NmToken(1, "Interest a 9%", 1200, 10, 100, 10),
            NmToken(1, "Final Total", 1350, 10, 90, 10), NmToken(1, "Owner One S/o Parent", 20, 100, 120, 10),
        ]
        schema = detect_column_schema(1, headers, 1500)
        self.assertIsNotNone(schema)
        roles = {role for role, _, _ in schema.bands}
        self.assertTrue({"structure_compensation", "base_total", "solatium", "additional_compensation", "interest", "grand_total"}.issubset(roles))
        self.assertEqual("structure_compensation", schema.column_for(NmToken(1, "Rs0.00", 700, 150, 40, 10)))
        self.assertEqual("base_total", schema.column_for(NmToken(1, "Rs10.00", 875, 150, 40, 10)))
        self.assertEqual("solatium", schema.column_for(NmToken(1, "Rs3.00", 975, 150, 40, 10)))
        self.assertEqual("additional_compensation", schema.column_for(NmToken(1, "Rs2.00", 1100, 150, 40, 10)))

    def test_summary_money_stays_with_its_owner_span(self):
        blocks = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), token("Rs100.00", 220, 160),
            token("Owner Two S/o Parent", 20, 200), token("Rs200.00", 220, 260),
        ], SCHEMA)
        self.assertEqual("Rs100.00", blocks[0].components["land_compensation"][0])
        self.assertEqual("Rs200.00", blocks[1].components["land_compensation"][0])

    def test_column_bands_map_statutory_components_and_zero(self):
        blocks = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), token("Rs0.00", 220, 150),
            token("Rs30.00", 320, 150), token("Rs12.00", 420, 150),
            token("Rs9.00", 520, 150), token("Rs51.00", 620, 150),
        ], SCHEMA)
        values = {key: value for key, (value, _) in blocks[0].components.items()}
        self.assertEqual("Rs0.00", values["land_compensation"])
        self.assertEqual("Rs30.00", values["solatium"])
        self.assertEqual("Rs12.00", values["additional_compensation"])
        self.assertEqual("Rs9.00", values["interest"])
        self.assertEqual("Rs51.00", values["grand_total"])

    def test_running_total_is_not_grand_total(self):
        blocks = semantic_owner_blocks([token("Owner One S/o Parent", 20, 100), token("Running Total Rs999.00", 620, 150)], SCHEMA)
        self.assertNotIn("grand_total", blocks[0].components)

    def test_token_crossing_compensation_boundary_is_unresolved(self):
        blocks = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), NmToken(1, "Rs0.00 Rs20.00", 280, 150, 100, 10),
        ], SCHEMA)
        self.assertEqual({}, blocks[0].components)

    def test_header_money_like_fragment_is_not_owner_component(self):
        blocks = semantic_owner_blocks([
            token("Interest @ 9%", 520, 90), token("Owner One S/o Parent", 20, 100),
        ], SCHEMA)
        self.assertNotIn("interest", blocks[0].components)

    def test_no_fallback_when_position_is_ambiguous(self):
        blocks = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), NmToken(1, "Rs0.00", 280, 150, 40, 10),
        ], SCHEMA)
        self.assertNotIn("land_compensation", blocks[0].components)
        self.assertNotIn("solatium", blocks[0].components)

    def test_split_money_pieces_join_only_inside_one_cell_and_keep_provenance(self):
        pieces = [NmToken(1, "Rs", 220, 150, 10, 10), NmToken(1, "15,73", 232, 150, 20, 10), NmToken(1, "9.70", 254, 150, 20, 10)]
        blocks = semantic_owner_blocks([token("Owner One S/o Parent", 20, 100), *pieces], SCHEMA)
        cell = blocks[0].components["land_compensation"]
        self.assertEqual("Rs15,739.70", cell.raw_amount)
        self.assertEqual(tuple(pieces), cell.tokens)

    def test_pieces_from_different_rows_or_columns_never_join(self):
        different_rows = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), NmToken(1, "Rs", 220, 150, 10, 10), NmToken(1, "15.00", 232, 190, 25, 10),
        ], SCHEMA)
        different_columns = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), NmToken(1, "Rs", 220, 150, 10, 10), NmToken(1, "15.00", 320, 150, 25, 10),
        ], SCHEMA)
        self.assertEqual({}, different_rows[0].components)
        self.assertNotIn("land_compensation", different_columns[0].components)
        self.assertEqual("15.00", different_columns[0].components["solatium"].raw_amount)

    def test_incomplete_prefix_with_same_cell_continuation_is_unresolved(self):
        blocks = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), NmToken(1, "Rs15.73", 220, 150, 35, 10), NmToken(1, "9.70", 257, 150, 20, 10),
        ], SCHEMA)
        self.assertNotIn("land_compensation", blocks[0].components)

    def test_boundary_graze_uses_unambiguous_majority_ownership(self):
        blocks = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), NmToken(1, "Rs0.00", 295, 150, 20, 10),
        ], SCHEMA)
        self.assertEqual("Rs0.00", blocks[0].components["solatium"].raw_amount)

    def test_complete_money_normalizes_without_changing_raw_cell_or_component(self):
        self.assertEqual(Decimal("590229.17"), normalize_money_amount("Rs590,229.17"))
        self.assertEqual(Decimal("0.00"), normalize_money_amount("Rs0.00"))
        self.assertEqual(Decimal("475062.51"), normalize_money_amount("475,062.51"))
        blocks = semantic_owner_blocks([token("Owner One S/o Parent", 20, 100), token("Rs590,229.17", 220, 150)], SCHEMA)
        cell = blocks[0].components["land_compensation"]
        self.assertEqual("Rs590,229.17", cell.raw_amount)
        self.assertEqual("land_compensation", cell.column)
        self.assertEqual(Decimal("590229.17"), normalize_money_amount(cell.raw_amount))
        self.assertEqual("Rs590,229.17", cell.tokens[0].text)

    def test_malformed_money_never_normalizes(self):
        self.assertIsNone(normalize_money_amount("Rs15.739.70"))

    def test_summary_cluster_allows_skewed_final_total_but_excludes_running_total(self):
        blocks = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), token("Kita", 20, 150),
            token("Rs10.00", 220, 160), token("Rs3.00", 320, 170), token("Rs2.00", 420, 180),
            token("Rs1.00", 520, 190), token("RsX", 520, 205), token("Rs16.00", 620, 220),
            token("Running Total", 620, 270), token("Rs999.00", 620, 270),
        ], SCHEMA)
        self.assertEqual("Rs16.00", blocks[0].components["grand_total"].raw_amount)
        self.assertNotEqual("Rs999.00", blocks[0].components["grand_total"].raw_amount)

    def test_malformed_interest_cell_stays_unresolved(self):
        blocks = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), token("Kita", 20, 140), token("Rs15.739.70", 520, 150),
        ], SCHEMA)
        self.assertNotIn("interest", blocks[0].components)


if __name__ == "__main__":
    unittest.main()
