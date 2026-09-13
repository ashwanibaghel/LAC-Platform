import unittest

from nm_semantics import NmColumnSchema, NmToken, semantic_owner_blocks


SCHEMA = NmColumnSchema(
    1,
    (
        ("owner", 0, 200),
        ("khasra", 200, 300),
        ("area", 300, 400),
        ("land_class", 400, 500),
        ("land_compensation", 500, 650),
    ),
    "test",
)


def token(text, x, y, page=1):
    return NmToken(page, text, x, y, 20, 10, 0.99)


def owner(name, y, page=1):
    return token(f"{name} S/o Parent", 20, y, page)


def explicit_parcel(khasra, area, y, page=1):
    return [token(khasra, 220, y, page), token(area, 320, y, page)]


class DittoParcelInheritanceTests(unittest.TestCase):
    def test_parcel_scope_ditto_creates_relation_with_dual_provenance(self):
        original_khasra, original_area = explicit_parcel("26/21/3", "1-10", 120)
        marker = token("-do-", 220, 220)
        blocks = semantic_owner_blocks(
            [owner("Owner One", 100), original_khasra, original_area, owner("Owner Two", 200), marker], SCHEMA
        )

        inherited = blocks[1].parcels[0]
        self.assertEqual(blocks[1].inherited_from_sequence, blocks[0].sequence)
        self.assertTrue(inherited.inherited)
        self.assertEqual(inherited.inherited_from_sequence, blocks[0].sequence)
        self.assertIs(inherited.ditto_token, marker)  # current owner marker provenance
        self.assertIs(inherited.khasra_token, original_khasra)  # original parcel provenance
        self.assertIs(inherited.area_token, original_area)

    def test_ditto_outside_parcel_scope_is_not_an_inheritance_event(self):
        blocks = semantic_owner_blocks(
            [owner("Owner One", 100), *explicit_parcel("26/21/3", "1-10", 120), owner("Owner Two", 200), token("do", 20, 220)], SCHEMA
        )

        self.assertIsNone(blocks[1].ditto_token)
        self.assertFalse(blocks[1].parcels)

    def test_identity_share_and_compensation_are_not_inherited(self):
        blocks = semantic_owner_blocks(
            [
                owner("Owner One", 100), token("Share 1/2", 20, 110), *explicit_parcel("26/21/3", "1-10", 120), token("Rs1,000.00", 520, 130),
                owner("Owner Two", 200), token("Share 1/3", 20, 210), token("ditto", 220, 220),
            ],
            SCHEMA,
        )

        second = blocks[1]
        self.assertEqual(second.recorded_name, "Owner Two")
        self.assertEqual(second.share_raw, "1/3")
        self.assertEqual(second.components, {})

    def test_only_immediately_previous_compatible_owner_is_inherited(self):
        blocks = semantic_owner_blocks(
            [
                owner("Owner One", 100), *explicit_parcel("26/21/3", "1-10", 120),
                owner("Owner Two", 200), *explicit_parcel("99/88/7", "2-05", 220),
                owner("Owner Three", 300), token("do", 220, 320),
            ],
            SCHEMA,
        )

        self.assertEqual(blocks[2].inherited_from_sequence, blocks[1].sequence)
        self.assertEqual(blocks[2].parcels[0].raw_khasra, "99/88/7")

    def test_missing_intervening_owner_does_not_allow_an_older_owner_to_be_used(self):
        blocks = semantic_owner_blocks(
            [
                owner("Owner One", 100), *explicit_parcel("26/21/3", "1-10", 120),
                owner("Owner Two", 200),
                owner("Owner Three", 300), token("do", 220, 320),
            ],
            SCHEMA,
        )

        self.assertIn("AmbiguousDittoScope", blocks[2].exceptions)
        self.assertFalse(blocks[2].parcels)

    def test_explicit_current_parcel_or_ambiguous_previous_blocks_inheritance(self):
        conflict = semantic_owner_blocks(
            [
                owner("Owner One", 100), *explicit_parcel("26/21/3", "1-10", 120),
                owner("Owner Two", 200), *explicit_parcel("99/88/7", "2-05", 210), token("-do-", 220, 220),
            ],
            SCHEMA,
        )
        ambiguous = semantic_owner_blocks(
            [owner("Owner One", 100), token("26/21/3", 220, 120), owner("Owner Two", 200), token("-do-", 220, 220)], SCHEMA
        )

        self.assertIn("AmbiguousDittoScope", conflict[1].exceptions)
        self.assertEqual([parcel.raw_khasra for parcel in conflict[1].parcels], ["99/88/7"])
        self.assertIn("AmbiguousDittoScope", ambiguous[1].exceptions)
        self.assertFalse(ambiguous[1].parcels)

    def test_cross_page_ditto_does_not_inherit(self):
        blocks = semantic_owner_blocks(
            [owner("Owner One", 100, 1), *explicit_parcel("26/21/3", "1-10", 120, 1), owner("Owner Two", 200, 2), token("-do-", 220, 220, 2)], SCHEMA
        )

        self.assertIn("AmbiguousDittoScope", blocks[1].exceptions)
        self.assertFalse(blocks[1].parcels)


if __name__ == "__main__":
    unittest.main()
