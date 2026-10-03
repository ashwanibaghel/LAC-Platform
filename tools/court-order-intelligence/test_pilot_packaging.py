import unittest
import hashlib
from pathlib import Path
import tempfile
from types import SimpleNamespace
from unittest.mock import Mock, patch
from package_pilot_v1 import PinnedSource, SHARDS


class RangeTests(unittest.TestCase):
    def source(self, status=206, content_range=None, chunks=(b"abcd",)):
        source = object.__new__(PinnedSource)
        source.session = Mock()
        filename = next(iter(SHARDS))
        response = Mock(status_code=status)
        response.headers = {"Content-Range": content_range or f"bytes 8-11/{SHARDS[filename][0]}"}
        response.iter_content.return_value = iter(chunks)
        response.__enter__ = Mock(return_value=response)
        response.__exit__ = Mock(return_value=False)
        source.session.get.return_value = response
        return source, source.url(filename)

    def test_exact_pinned_range(self):
        source, url = self.source()
        self.assertEqual(b"abcd", source.range(url, 8, 4))
        self.assertIn("cdbee75f17c01a7cc42f958dc650907174af0554", url)
        self.assertEqual("bytes=8-11", source.session.get.call_args.kwargs["headers"]["Range"])

    def test_unpinned_url_rejected_without_request(self):
        source, url = self.source()
        for bad in [url.replace("cdbee75f17c01a7cc42f958dc650907174af0554", "main"),
                    url.replace("huggingface.co", "example.org")]:
            with self.assertRaises(ValueError):
                source.range(bad, 8, 4)
        source.session.get.assert_not_called()

    def test_full_response_or_wrong_range_rejected(self):
        for options in [{"status": 200}, {"content_range": "bytes 0-3/100"}]:
            source, url = self.source(**options)
            with self.assertRaises(ValueError):
                source.range(url, 8, 4)

    def test_truncated_and_oversized_ranges_rejected(self):
        for chunks in [(b"abc",), (b"abcde",)]:
            source, url = self.source(chunks=chunks)
            with self.assertRaises(ValueError):
                source.range(url, 8, 4)

    def test_invalid_bounds_and_unapproved_files_rejected(self):
        source, url = self.source()
        for start, size in [(-1, 4), (0, -1), (0, 0), (SHARDS[next(iter(SHARDS))][0], 4)]:
            with self.assertRaises(ValueError):
                source.range(url, start, size)
        with self.assertRaises(ValueError):
            source.url("../secret")
        source.session.get.assert_not_called()

    def test_large_tensor_is_chunked_bit_exact_and_temporary(self):
        data = b"abcd" * (1024 * 1024 + 1)
        source = object.__new__(PinnedSource)
        url = source.url(next(iter(SHARDS)))
        tensor = SimpleNamespace(url=url, offset_start=8, size=len(data))
        source.tensor_keys = {(url, 8, len(data)): "test.weight"}
        source.tensor_hashes = {}
        source.range = Mock(side_effect=lambda url, start, size: bytearray(data[start-8:start-8+size]))
        with tempfile.TemporaryDirectory() as directory:
            mapped = source.tensor_data(tensor, directory)
            try:
                self.assertEqual(data, mapped.tobytes())
                self.assertEqual(hashlib.sha256(data).hexdigest(), source.tensor_hashes["test.weight"])
                self.assertTrue(all(call.args[2] <= 4 * 1024 * 1024 for call in source.range.call_args_list))
            finally:
                mapped._mmap.close()
                mapped._pilot_stream.close()
            self.assertEqual([], [p for p in Path(directory).rglob("*") if p.is_file()])

    def test_tensor_download_failure_cleans_temporary_file(self):
        source = object.__new__(PinnedSource)
        source.range = Mock(side_effect=ValueError("Invalid range"))
        tensor = SimpleNamespace(url=source.url(next(iter(SHARDS))), offset_start=8, size=4 * 1024 * 1024 + 1)
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                source.tensor_data(tensor, directory)
            self.assertEqual([], [p for p in Path(directory).rglob("*") if p.is_file()])

    def test_transport_retry_is_bounded_and_integrity_errors_not_retried(self):
        import requests
        source = object.__new__(PinnedSource)
        source._range_once = Mock(side_effect=requests.ConnectionError("fixture transport error"))
        with patch("package_pilot_v1.time.sleep"):
            with self.assertRaises(requests.ConnectionError):
                source.range("fixture", 0, 4)
        self.assertEqual(3, source._range_once.call_count)
        source._range_once = Mock(side_effect=ValueError("Wrong range"))
        with self.assertRaises(ValueError):
            source.range("fixture", 0, 4)
        self.assertEqual(1, source._range_once.call_count)

    def test_verified_range_cache_reused_and_tampering_fails_closed(self):
        source = object.__new__(PinnedSource)
        source.range = Mock(return_value=b"abcd")
        url = source.url(next(iter(SHARDS)))
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(b"abcd", source.cached_range(url, 0, 4, directory))
            self.assertEqual(b"abcd", source.cached_range(url, 0, 4, directory))
            self.assertEqual(1, source.range.call_count)
            next(Path(directory).glob("*.bin")).write_bytes(b"xxxx")
            with self.assertRaises(ValueError):
                source.cached_range(url, 0, 4, directory)
            self.assertEqual(1, source.range.call_count)

    def test_incomplete_cache_is_preserved_without_overwrite(self):
        source = object.__new__(PinnedSource)
        source.range = Mock(return_value=b"abcd")
        url = source.url(next(iter(SHARDS)))
        with tempfile.TemporaryDirectory() as directory:
            key = hashlib.sha256(f"{url}:0:4".encode()).hexdigest()
            partial = Path(directory) / (key + ".bin")
            partial.write_bytes(b"ab")
            with self.assertRaises(ValueError):
                source.cached_range(url, 0, 4, directory)
            self.assertEqual(b"ab", partial.read_bytes())
            source.range.assert_not_called()


if __name__ == "__main__":
    unittest.main()
