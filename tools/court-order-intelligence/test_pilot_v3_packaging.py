"""Synthetic provenance fixtures only; never load weights or launch inference."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import package_pilot_v3 as packaging


class V3PackagingTests(unittest.TestCase):
    def fixture(self, root):
        fit=root/'fit'; evaluation=root/'evaluation'; (fit/'adapter').mkdir(parents=True); evaluation.mkdir()
        (fit/'adapter/adapter_model.safetensors').write_bytes(b'SYNTHETIC_NOT_MODEL_WEIGHTS')
        adapter=packaging.sha256(fit/'adapter/adapter_model.safetensors')
        config=dict(base_model_name_or_path=packaging.MODEL,revision=None,r=8,lora_alpha=16,
                    target_modules=['q_proj','v_proj','k_proj','o_proj'],bias='none',modules_to_save=None,use_dora=False)
        metadata=dict(model_id=packaging.MODEL,revision=packaging.REVISION,fit_complete=True,
                      adapter_reload_passed=True,purpose='V3_EXPERIMENT_VERIFIED_POOL_NOT_QUALITY_ACCEPTANCE')
        for name,data in [('adapter/adapter_config.json',config),('run-metadata.json',metadata)]:
            (fit/name).write_text(json.dumps(data))
        receipt={str(p.relative_to(fit)).replace('\\','/'):packaging.sha256(p) for p in fit.rglob('*') if p.is_file()}
        (fit/'artifact-checksums.json').write_text(json.dumps(receipt))
        (evaluation/'evaluation-metadata.json').write_text(json.dumps(dict(base=packaging.MODEL,
            revision=packaging.REVISION,adapter_sha256=adapter)))
        (evaluation/'artifact-checksums.json').write_text(json.dumps({'evaluation-metadata.json':
            packaging.sha256(evaluation/'evaluation-metadata.json')}))
        return fit,evaluation,adapter,packaging.sha256(fit/'artifact-checksums.json')

    def test_null_peft_revision_requires_checksum_bound_fit_and_evaluation_same_revision(self):
        with tempfile.TemporaryDirectory() as directory:
            fit,evaluation,adapter,receipt=self.fixture(Path(directory))
            before=(fit/'adapter/adapter_config.json').read_bytes()
            with patch.object(packaging,'V3_ADAPTER_SHA',adapter),patch.object(packaging,'V3_ARTIFACT_MANIFEST_SHA',receipt),patch('subprocess.run') as process:
                self.assertEqual(3,packaging.verify_v3(fit,evaluation))
                process.assert_not_called()
            self.assertEqual(before,(fit/'adapter/adapter_config.json').read_bytes())

    def test_adapter_tampering_fails_closed_without_conversion(self):
        with tempfile.TemporaryDirectory() as directory:
            fit,evaluation,adapter,receipt=self.fixture(Path(directory))
            (fit/'adapter/adapter_model.safetensors').write_bytes(b'WRONG_ADAPTER')
            with patch.object(packaging,'V3_ADAPTER_SHA',adapter),patch.object(packaging,'V3_ARTIFACT_MANIFEST_SHA',receipt):
                with self.assertRaisesRegex(ValueError,'integrity mismatch'): packaging.verify_v3(fit,evaluation)

    def test_different_evaluated_adapter_or_base_is_not_accepted(self):
        for field in ('adapter_sha256','revision','base'):
            with tempfile.TemporaryDirectory() as directory:
                fit,evaluation,adapter,receipt=self.fixture(Path(directory))
                path=evaluation/'evaluation-metadata.json'; data=json.loads(path.read_text()); data[field]='not-the-evaluated-identity'
                path.write_text(json.dumps(data))
                (evaluation/'artifact-checksums.json').write_text(json.dumps({path.name:packaging.sha256(path)}))
                with patch.object(packaging,'V3_ADAPTER_SHA',adapter),patch.object(packaging,'V3_ARTIFACT_MANIFEST_SHA',receipt):
                    with self.assertRaisesRegex(ValueError,'Not the preserved evaluated'): packaging.verify_v3(fit,evaluation)

    def test_existing_v3_directory_and_v1_directory_are_never_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); v1=root/'v1'; v1.mkdir(); existing=root/'existing'; existing.mkdir()
            marker=v1/'court-model-manifest.json'; marker.write_text('preserved')
            for destination in (existing,v1/'nested'):
                argv=['package_pilot_v3']
                for name,path in [('v3-root',root),('evaluation-root',root),('v1-manifest',marker),
                                  ('base',root),('converter',root),('base-config',root),('output-root',destination)]:
                    argv.extend(['--'+name,str(path)])
                with patch('sys.argv',argv),patch('subprocess.run') as process:
                    with self.assertRaisesRegex(ValueError,'NEW separate'): packaging.main()
                    process.assert_not_called()
            self.assertEqual('preserved',marker.read_text())


if __name__=='__main__': unittest.main()
