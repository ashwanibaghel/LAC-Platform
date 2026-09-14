import unittest

from nm_semantics import NmColumnSchema, NmToken, semantic_owner_blocks


SCHEMA = NmColumnSchema(1, (
    ("owner", 0, 200), ("khasra", 200, 300), ("area", 300, 400),
    ("land_class", 400, 500), ("land_compensation", 500, 650),
), "test")


def token(text, x, y):
    return NmToken(1, text, x, y, 30, 10, 0.99)


class PageOwnerSegmentationTests(unittest.TestCase):
    def test_two_source_sequential_owners_remain_separate_and_second_facts_do_not_leak(self):
        tokens = [
            token("Iqbal Singh S/o Aman Singh", 20, 100), token("1 / 3", 20, 120),
            token("26/21/3", 220, 150), token("3 -- 5", 320, 153), token("A", 420, 154),
            token("261/21/2", 220, 180), token("0 -- 9", 320, 183), token("A", 420, 184),
            token("31/11", 220, 210), token("2 -- 9", 320, 213), token("A", 420, 214),
            token("Kita", 20, 240), token("3", 220, 240), token("6 - 3", 320, 240),
            token("Narender Singh So Aman Singh", 20, 300), token("Share 1 / 3", 20, 320), token("-do-", 220, 340),
        ]

        first, second = semantic_owner_blocks(tokens, SCHEMA)

        self.assertEqual([first.recorded_name, second.recorded_name], ["Iqbal Singh", "Narender Singh"])
        self.assertEqual(first.share_raw, "1 / 3")
        self.assertEqual(second.share_raw, "1 / 3")
        self.assertEqual([parcel.raw_khasra for parcel in first.parcels], ["26/21/3", "261/21/2", "31/11"])
        self.assertEqual([parcel.raw_area for parcel in first.parcels], ["3 -- 5", "0 -- 9", "2 -- 9"])
        self.assertEqual([parcel.land_class for parcel in first.parcels], ["A", "A", "A"])
        self.assertEqual(first.parcel_count_as_recorded, "3")
        self.assertEqual(first.total_area_as_recorded, "6 - 3")
        self.assertIs(second.ditto_token, tokens[-1])
        self.assertFalse(any(parcel.khasra_token.y >= second.name_token.y for parcel in first.parcels))

    def test_kita_does_not_create_a_parcel_when_no_row_source_exists(self):
        block = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), token("Kita", 20, 140), token("3", 220, 140), token("6 - 3", 320, 140),
        ], SCHEMA)[0]
        self.assertEqual(block.parcel_count_as_recorded, "3")
        self.assertFalse(block.parcels)

    def test_area_is_row_local_and_missing_area_keeps_its_safe_khasra(self):
        block = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), token("26/21/3", 220, 150), token("3 - 5", 320, 153),
            token("31/1/1", 220, 210),
        ], SCHEMA)[0]
        self.assertEqual([parcel.raw_area for parcel in block.parcels], ["3 - 5", None])

    def test_two_part_source_khasra_is_not_repaired(self):
        block = semantic_owner_blocks([
            token("Owner One S/o Parent", 20, 100), token("31/11", 220, 150), token("2 - 9", 320, 153),
        ], SCHEMA)[0]
        self.assertEqual(block.parcels[0].raw_khasra, "31/11")
        self.assertNotEqual(block.parcels[0].raw_khasra, "31/1/1")


if __name__ == "__main__":
    unittest.main()
