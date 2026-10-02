import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from prepare_smoke_dataset import prepare, TRAINING
from smoke_contract import encode, parse_runtime_output, verify_bundle, verify_config
from reload_smoke import inference_messages, schema_prefix


class FakeTokenizer:
    def apply_chat_template(self, messages, tokenize, add_generation_prompt):
        return [1, 2, 3] if add_generation_prompt else [1, 2, 3, 4, 5]


class SmokeTests(unittest.TestCase):
    def test_saved_real_gpu_outputs_pass_frozen_contract_and_artifact_chain(self):
        proof = json.loads((HERE / "reports/t2-constrained-smoke-evaluation.json").read_text())
        run = json.loads((HERE / "reports/t2-run-metadata.json").read_text())
        hashes = json.loads((HERE / "reports/t2-artifact-checksums.json").read_text())
        self.assertEqual(proof["adapter_sha256"], hashes["adapter/adapter_model.safetensors"])
        self.assertEqual(run["resume_result"], {"checkpoint_step": 2, "final_step": 6, "passed": True})
        self.assertEqual(proof["training_steps_in_this_proof"], 0)
        self.assertFalse(proof["output_repaired"])
        self.assertIsNone(proof["quality_metric"])
        with tempfile.TemporaryDirectory() as temp:
            bundle = prepare(Path(temp) / "bundle")
            records, _ = verify_bundle(bundle)
            schemas = json.loads((bundle / "target.schemas.json").read_text())
            self.assertEqual(len(proof["representative_outputs"]), 5)
            for output in proof["representative_outputs"]:
                record = next(r for r in records if r["id"] == output["id"])
                parse_runtime_output(output["raw_output"], record, schemas, bundle / "runtime")
                self.assertTrue(output["parser_accepted"])

    def test_decoder_receives_exact_schema_not_expected_target(self):
        captured = []
        schema = {"type": "object", "required": ["claims"], "additionalProperties": False}
        def parser(value):
            captured.append(value)
            return "parser"
        def build(tokenizer, value):
            self.assertEqual((tokenizer, value), ("tokenizer", "parser"))
            return "prefix"
        with patch.dict(sys.modules, {
            "lmformatenforcer": SimpleNamespace(JsonSchemaParser=parser),
            "lmformatenforcer.integrations.transformers": SimpleNamespace(
                build_transformers_prefix_allowed_tokens_fn=build)}):
            self.assertEqual(schema_prefix("tokenizer", schema), "prefix")
        self.assertEqual(captured, [schema])

    def test_inference_receives_contract_schema_but_never_gold_target(self):
        record = {"contract": "claims", "expected": {"secretTarget": "DO_NOT_LEAK"},
                  "messages": [{"role": "system", "content": "Use evidence."},
                               {"role": "user", "content": "bounded evidence"},
                               {"role": "assistant", "content": "DO_NOT_LEAK"}]}
        original = copy.deepcopy(record)
        schema = {"claims": {"type": "object", "required": ["claims"]}}
        messages = inference_messages(record, schema)
        self.assertEqual([m["role"] for m in messages], ["system", "user"])
        self.assertIn(json.dumps(schema["claims"], separators=(",", ":")), messages[0]["content"])
        self.assertNotIn("DO_NOT_LEAK", json.dumps(messages))
        self.assertEqual(record, original)

    def test_gpu_launcher_and_checkpoint_pause_preserve_smoke_contract(self):
        launcher = (HERE / "run_kaggle.py").read_text()
        runner = (HERE / "train_smoke.py").read_text()
        self.assertIn("environment['CUDA_VISIBLE_DEVICES'] = '0'", launcher)
        self.assertIn("env=environment", launcher)
        self.assertIn("list(metadata['files'])", launcher)
        self.assertIn("including_emulation=False", runner)
        self.assertIn('first = trainer(model, data, config["max_steps"], checkpoints)', runner)
        self.assertIn("first.add_callback(CheckpointPause())", runner)
        self.assertIn("control.should_save = True", runner)
        self.assertIn("control.should_training_stop = True", runner)
        self.assertIn("resume_from_checkpoint=str(checkpoint)", runner)
        self.assertIn("IterableDataset.from_generator(smoke_stream)", runner)
        self.assertIn("yield from encoded", runner)

    def test_bundle_is_public_allowlist_with_all_explicit_smoke_markers(self):
        with tempfile.TemporaryDirectory() as temp:
            path = prepare(Path(temp) / "bundle")
            rows, manifest = verify_bundle(path)
            self.assertEqual(len(rows), 22)
            self.assertFalse(manifest["private_workbook_included"])
            self.assertFalse(manifest["protected_splits_modified"])
            self.assertEqual(len({r["task"] for r in rows}), 8)
            self.assertFalse(any("local-private" in p or p.endswith((".xlsx", ".pdf", ".bin", ".safetensors")) for p in manifest["files"]))
            self.assertEqual(set(r["messages"][-1]["role"] for r in rows), {"assistant"})

    def test_existing_bundle_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as temp:
            path = prepare(Path(temp) / "bundle")
            with self.assertRaises(ValueError):
                prepare(path)

    def test_bundle_tamper_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            path = prepare(Path(temp) / "bundle")
            (path / "smoke.jsonl").write_text("{}\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                verify_bundle(path)

    def test_gold_target_json_works_with_all_runtime_contracts(self):
        with tempfile.TemporaryDirectory() as temp:
            path = prepare(Path(temp) / "bundle")
            rows, _ = verify_bundle(path)
            schema = json.loads((path / "target.schemas.json").read_text())
            for record in rows:
                with self.subTest(example=record["id"]):
                    parse_runtime_output(json.dumps(record["expected"]), record, schema, path / "runtime")

    def test_model_prose_and_unretrieved_ids_are_not_repaired(self):
        with tempfile.TemporaryDirectory() as temp:
            path = prepare(Path(temp) / "bundle")
            rows, _ = verify_bundle(path)
            schema = json.loads((path / "target.schemas.json").read_text())
            record = next(r for r in rows if r["contract"] == "claims")
            for text in ('Here is the answer: {"claims":[]}', '{"claims":[{"factId":999}]}'):
                with self.assertRaises(ValueError):
                    parse_runtime_output(text, record, schema, path / "runtime")

    def test_mask_is_assistant_only_and_no_silent_truncation(self):
        record = {"messages": [{"role": "system"}, {"role": "user"}, {"role": "assistant"}]}
        tokens = encode(FakeTokenizer(), record, 5)
        self.assertEqual(tokens["labels"], [-100, -100, -100, 4, 5])
        self.assertEqual(tokens["input_ids"], [1, 2, 3, 4, 5])
        self.assertIsNone(encode(FakeTokenizer(), record, 4))

    def test_invalid_official_template_boundary_fails_closed(self):
        class Bad(FakeTokenizer):
            def apply_chat_template(self, messages, tokenize, add_generation_prompt):
                return [0] if add_generation_prompt else [1, 2]
        with self.assertRaises(ValueError):
            encode(Bad(), {"messages": [{}, {}, {}]}, 5)

    def test_exact_model_bounded_quota_and_smoke_config(self):
        config = json.loads((HERE / "training_config_smoke.json").read_text())
        verify_config(config)
        for key, value in (("revision", "main"), ("model_id", "another-model"),
                           ("max_steps", 100), ("batch_size", 2), ("max_sequence_length", 8192),
                           ("purpose", ["BLIND"]), ("max_new_tokens", 4096)):
            bad = copy.deepcopy(config)
            bad[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify_config(bad)

    def test_protected_split_bytes_unchanged_after_smoke_preparation(self):
        names = ("train.jsonl", "validation.jsonl", "blind.jsonl", "splits.json", "development.jsonl")
        before = {n: (TRAINING / "generated" / n).read_bytes() for n in names}
        with tempfile.TemporaryDirectory() as temp:
            prepare(Path(temp) / "bundle")
        self.assertEqual(before, {n: (TRAINING / "generated" / n).read_bytes() for n in names})


if __name__ == "__main__":
    unittest.main()
