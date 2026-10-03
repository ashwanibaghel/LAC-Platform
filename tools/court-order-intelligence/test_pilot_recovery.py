"""Small synthetic recovery fixtures; no model inference or network requests."""
import hashlib
import json
from pathlib import Path
import tempfile
import subprocess
from types import SimpleNamespace
import unittest
from unittest.mock import Mock

import numpy as np
from gguf import GGMLQuantizationType as Kind, GGUFWriter, GGUFReader
from gguf.quants import quantize
from recover_pilot_v1 import header, to_bf16, from_bf16, complete_prefix, audit, write_base, write_sharded


class RecoveryTests(unittest.TestCase):
    def fixture(self, root):
        path = root / "original.gguf"
        raw = np.array([0x0000, 0x8000, 0x3f80, 0xbf80, 0x0080, 0x0001, 0x7f7f, 0x3eaa], dtype="<u2").tobytes()
        writer = GGUFWriter(str(path), "qwen3")
        writer.add_tensor("first.weight", np.frombuffer(raw, dtype=np.uint8).reshape(2, 8), raw_dtype=Kind.BF16)
        writer.add_tensor("second_norm.weight", np.frombuffer(from_bf16(raw, Kind.F32), dtype="<f4"))
        writer.add_tensor("third.weight", np.frombuffer(raw, dtype=np.uint8).reshape(2, 8), raw_dtype=Kind.BF16)
        writer.write_header_to_file()
        writer.write_kv_data_to_file()
        writer.write_tensors_to_file()
        writer.close()
        return path, raw

    def test_bounded_widening_matches_pinned_upstream_quantization_bit_exact(self):
        bits = np.arange(65536, dtype="<u2")
        finite = bits[(bits & 0x7f80) != 0x7f80].tobytes()
        widened = from_bf16(finite, Kind.F32)
        self.assertEqual(finite, to_bf16(widened, Kind.F32))
        self.assertEqual(finite, quantize(np.frombuffer(widened, dtype="<f4"), Kind.BF16).tobytes())

    def test_nonfinite_or_inexact_f32_is_rejected(self):
        for bits in (0x7f80, 0xff80, 0x7fc1):
            with self.assertRaises(ValueError):
                from_bf16(np.array([bits], dtype="<u2").tobytes(), Kind.BF16)
        with self.assertRaises(ValueError):
            to_bf16(np.array([0x3f800001], dtype="<u4").tobytes(), Kind.F32)

    def test_shards_and_pinned_quantizer_match_single_file_without_inference(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            original = root / "input.gguf"
            writer = GGUFWriter(str(original), "qwen3")
            writer.add_block_count(36)
            writer.add_head_count(32)
            writer.add_head_count_kv(8)
            writer.add_embedding_length(2560)
            writer.add_context_length(262144)
            writer.add_feed_forward_length(9728)
            writer.add_layer_norm_rms_eps(1e-6)
            writer.add_rope_freq_base(5000000)
            writer.add_key_length(128)
            writer.add_value_length(128)
            writer.add_vocab_size(151936)
            writer.add_file_type(32)  # MOSTLY_BF16
            names = ("blk.0.attn_v.weight", "blk.0.ffn_gate.weight", "blk.0.ffn_up.weight",
                     "blk.0.ffn_down.weight", "blk.0.attn_output.weight")
            rng = np.random.default_rng(42)
            for name in names:
                bf16 = (rng.normal(size=(8,256)).astype("<f4").view("<u4") >> 16).astype("<u2")
                writer.add_tensor(name, bf16.view(np.uint8).reshape(8,512), raw_dtype=Kind.BF16)
            writer.write_header_to_file()
            writer.write_kv_data_to_file()
            writer.write_tensors_to_file()
            writer.close()
            parsed = header(original)
            for record in parsed.records:
                record["source_name"] = record["name"]
                record["source"] = SimpleNamespace(size=record["bytes"])
            receipt = audit(original, parsed, parsed.records)
            source = SimpleNamespace(range=Mock())
            staged, overflow = root / "staged", root / "overflow"
            write_sharded(staged, overflow, original, parsed, parsed.records, receipt, source,
                          root / "unused-cache", split_size=4096)
            first = staged / "qwen3-4b-pinned-bf16-00001-of-00005.gguf"
            self.assertTrue((staged / "qwen3-4b-pinned-bf16-00004-of-00005.gguf").is_symlink())
            quantizer = Path("D:/LAC-CourtAI-V1-20261003/bin/llama-quantize.exe")
            if quantizer.exists():
                outputs = [root / "single-q4.gguf", root / "shards-q4.gguf"]
                for inp, out in zip((original, first), outputs):
                    run = subprocess.run([str(quantizer), "--max-buffer-size", "128", str(inp), str(out), "Q4_K_M", "1"],
                                         text=True, capture_output=True, timeout=60)
                    self.assertEqual(0, run.returncode, run.stderr[-2000:])
                left, right = [GGUFReader(str(p)) for p in outputs]
                self.assertEqual(len(left.tensors), len(right.tensors))
                for a,b in zip(left.tensors, right.tensors):
                    self.assertEqual((a.name,a.tensor_type), (b.name,b.tensor_type))
                    self.assertEqual(a.data.tobytes(), b.data.tobytes())
                left.data._mmap.close()
                right.data._mmap.close()
            source.range.assert_not_called()
            self.assertEqual(receipt["partialSha256"], hashlib.sha256(original.read_bytes()).hexdigest())
            parsed.data._mmap.close()

    def test_only_complete_prefix_is_reused_and_old_partial_unchanged(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            full, raw = self.fixture(root)
            parsed = header(full)
            data = full.read_bytes()
            partial = root / "partial.gguf"
            partial.write_bytes(data[:parsed.records[1]["offset"] + 4])
            parsed.data._mmap.close()
            parsed = header(partial)
            for record in parsed.records:
                record["source_name"] = record["name"]
                record["source"] = SimpleNamespace(size=len(raw), url="fixture", offset_start=0)
            recovered = complete_prefix(parsed.records, len(partial.read_bytes()))
            self.assertEqual(["first.weight"], [r["name"] for r in recovered])
            receipt = audit(partial, parsed, parsed.records)
            source = SimpleNamespace(cached_range=Mock(return_value=raw))
            output = root / "recovered.gguf"
            original_hash = hashlib.sha256(partial.read_bytes()).hexdigest()
            write_base(output, partial, parsed, parsed.records, receipt, source, root / "cache")
            self.assertEqual(data, output.read_bytes())
            self.assertEqual(original_hash, hashlib.sha256(partial.read_bytes()).hexdigest())
            self.assertEqual(2, source.cached_range.call_count)
            completed = GGUFReader(str(output))
            self.assertEqual(3, len(completed.tensors))
            completed.data._mmap.close()
            checkpoints = list(root.glob("*.checkpoint-*.json"))
            self.assertEqual(3, len(checkpoints))
            self.assertEqual(3, json.loads(Path(str(output) + ".provenance.json").read_text())["sourceTensorCount"])
            with self.assertRaises(ValueError):
                write_base(output, partial, parsed, parsed.records, receipt, source, root / "cache")
            parsed.data._mmap.close()


if __name__ == "__main__":
    unittest.main()
