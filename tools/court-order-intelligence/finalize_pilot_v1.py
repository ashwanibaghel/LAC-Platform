"""Validate converted files and emit a non-secret deployment manifest; no inference."""
import argparse
import hashlib
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
import sys
import zipfile
from package_pilot_v1 import ADAPTER_SHA, LLAMA_COMMIT, MODEL, REVISION, sha256, verify_adapter, verify_converter

RUNTIME_ZIP_SHA = "8f8c0c6501b075f52deff59537c05acd57d8621a0a7935f29b7d7c4812892569"


def write_manifest(path, manifest):
    """Write an exclusive manifest plus checksum; never mark runtime acceptance passed."""
    path = Path(path)
    checksum = path.with_name(path.name + ".sha256")
    if path.exists() or checksum.exists():
        raise ValueError("Refusing to overwrite a deployment manifest/checksum.")
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(manifest, stream, indent=2)
        stream.write("\n")
    digest = sha256(path)
    with checksum.open("x", encoding="ascii", newline="\n") as stream:
        stream.write(f"{digest}  {path.name}\n")
    return digest


def verify_lora(root, path):
    import numpy as np
    from safetensors.numpy import load_file
    from gguf import GGUFReader
    reader = GGUFReader(str(path))
    fields = {key: reader.fields[key].contents() for key in
              ("general.type", "general.architecture", "adapter.type", "adapter.lora.alpha")}
    if fields != {"general.type": "adapter", "general.architecture": "qwen3",
                  "adapter.type": "lora", "adapter.lora.alpha": 16.0}:
        raise ValueError("Wrong LoRA GGUF metadata.")
    source = load_file(str(Path(root) / "adapter/adapter_model.safetensors"))
    target = {tensor.name: tensor.data for tensor in reader.tensors}
    mapping = {"q_proj": "attn_q", "k_proj": "attn_k", "v_proj": "attn_v", "o_proj": "attn_output"}
    if len(source) != 288 or len(target) != 288:
        raise ValueError("Incomplete LoRA GGUF.")
    for name, value in source.items():
        match = re.fullmatch(r"base_model.model.model.layers.(\d+).self_attn.(q_proj|k_proj|v_proj|o_proj).lora_([AB]).weight", name)
        if not match:
            raise ValueError("Unexpected V1 adapter tensor.")
        key = f"blk.{match[1]}.{mapping[match[2]]}.weight.lora_{match[3].lower()}"
        if key not in target or not np.array_equal(value, target[key]):
            raise ValueError(f"V1 LoRA GGUF changed tensor: {name}")
    return len(source)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("v1-root", "converter", "base", "lora", "provenance", "server", "manifest"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--runtime-zip", type=Path)
    args = parser.parse_args()
    if args.manifest.exists():
        raise ValueError("Refusing to overwrite a deployment manifest.")
    verify_adapter(args.v1_root)
    verify_converter(args.converter)
    sys.path.insert(0, str(args.converter / "gguf-py"))
    from gguf import GGUFReader, LlamaFileType
    count = verify_lora(args.v1_root, args.lora)
    base = GGUFReader(str(args.base))
    if (base.fields["general.architecture"].contents() != "qwen3"
            or base.fields["general.file_type"].contents() != LlamaFileType.MOSTLY_Q4_K_M
            or len(base.tensors) != 398):
        raise ValueError("Base is not the expected Qwen3 Q4_K_M GGUF.")
    receipt = json.loads(args.provenance.read_text("utf-8"))
    if (receipt["model"] != MODEL or receipt["revision"] != REVISION
            or receipt["converterCommit"] != LLAMA_COMMIT
            or receipt["sourceTensorCount"] != 398 or len(receipt["sourceTensorSha256"]) != 398):
        raise ValueError("Pinned source conversion provenance is incomplete.")
    if receipt.get("baseBf16Shards"):
        shards = receipt["baseBf16Shards"]
        if receipt.get("baseBf16HashKind") != "ordered-shard-sha256-aggregate":
            raise ValueError("Unknown BF16 shard hash scheme.")
        counts = 0
        for shard in shards:
            path = Path(shard["path"])
            if sha256(path) != shard["sha256"]:
                raise ValueError("BF16 shard integrity mismatch.")
            checked = GGUFReader(str(path))
            if len(checked.tensors) != shard["tensorCount"]:
                raise ValueError("BF16 shard tensor count mismatch.")
            counts += len(checked.tensors)
            checked.data._mmap.close()
        aggregate = hashlib.sha256("".join(s["sha256"] for s in shards).encode("ascii")).hexdigest()
        if counts != 398 or aggregate != receipt["baseBf16Sha256"]:
            raise ValueError("BF16 shard aggregate integrity mismatch.")
    else:
        intermediate = args.provenance.with_name(args.provenance.name.removesuffix(".provenance.json"))
        if sha256(intermediate) != receipt["baseBf16Sha256"]:
            raise ValueError("BF16 intermediate integrity mismatch.")
    runtime_zip = args.runtime_zip or args.server.parent.parent / "runtime-verified-download.zip"
    if sha256(runtime_zip) != RUNTIME_ZIP_SHA:
        raise ValueError("Official CPU runtime archive checksum mismatch.")
    runtime_hashes = {}
    with zipfile.ZipFile(runtime_zip) as archive:
        for member in archive.infolist():
            if member.is_dir():
                continue
            path = (args.server.parent / member.filename).resolve()
            if not path.is_relative_to(args.server.parent.resolve()):
                raise ValueError("Unsafe runtime archive member.")
            with archive.open(member) as stream:
                digest = hashlib.sha256(stream.read()).hexdigest()
            if sha256(path) != digest:
                raise ValueError("Extracted runtime differs from the official archive.")
            runtime_hashes[member.filename] = digest
    version = subprocess.check_output([str(args.server), "--version"], stderr=subprocess.STDOUT, text=True).strip()
    if "build 11321" not in version or "commit b0aca3c65" not in version:
        raise ValueError("Wrong llama.cpp runtime version.")
    manifest = {
        "manifestVersion": 1, "modelFamily": MODEL, "baseRevision": REVISION, "pilotVersion": "V1",
        "modelVersion": "Pilot-V1-Qwen3-4B-Instruct-2507-Q4_K_M-LoRA-F32-b11321",
        "adapterSha256": ADAPTER_SHA, "baseGgufFile": args.base.name, "baseGgufSha256": sha256(args.base),
        "baseGgufBytes": args.base.stat().st_size, "baseTensorCount": len(base.tensors),
        "loraGgufFile": args.lora.name, "loraGgufSha256": sha256(args.lora), "loraTensorBitExactCount": count,
        "loraGgufBytes": args.lora.stat().st_size,
        "quantization": "Q4_K_M", "loraPrecision": "F32", "contextSize": 4096, "parallel": 1,
        "threads": 2, "gpuLayers": 0, "modelEndpoint": "http://127.0.0.1:8096",
        "questionEndpoint": "http://127.0.0.1:8097", "cloudFallback": False,
        "llamaCppVersion": version, "llamaCppCommit": LLAMA_COMMIT, "serverSha256": sha256(args.server),
        "runtimeArchiveSha256": RUNTIME_ZIP_SHA, "runtimeFileSha256": runtime_hashes,
        "packagingMethod": "B: pinned BF16 base to Q4_K_M plus bit-exact F32 V1 LoRA GGUF",
        "sourceProvenanceSha256": sha256(args.provenance), "intermediateBf16Sha256": receipt["baseBf16Sha256"],
        "intermediateBf16HashKind": receipt.get("baseBf16HashKind", "single-file-sha256"),
        "intermediateBf16Shards": [{"file":s["file"], "sha256":s["sha256"]} for s in receipt.get("baseBf16Shards", [])],
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "packagingValidationStatus": "PASS",
        "qualityStatus": "EXPERIMENTAL_OFFICER_VERIFICATION_REQUIRED",
        "runtimePreflightStatus": "NOT_RUN_AWAITING_RAM_CONFIRMATION", "realE2EStatus": "NOT_RUN",
        "modelLaunchRequiresUserRamConfirmation": True,
    }
    digest = write_manifest(args.manifest, manifest)
    print(json.dumps(manifest, indent=2))
    print(json.dumps({"manifestSha256": digest}))


if __name__ == "__main__":
    main()
