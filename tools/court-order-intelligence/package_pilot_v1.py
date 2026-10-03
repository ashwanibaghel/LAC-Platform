"""Pinned, tensor-streamed V1 packaging. This tool never starts inference."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import logging
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import time
from urllib.parse import urlparse

MODEL = "Qwen/Qwen3-4B-Instruct-2507"
REVISION = "cdbee75f17c01a7cc42f958dc650907174af0554"
ADAPTER_SHA = "09ebbd176cbdf0390bee3e47343b351da7a2fe80c72a8837e7de7f6eb291fb67"
V1_MANIFEST_SHA = "86cb5933597b6eafacc4c1c9f948a7c0e93c4cb865f8813bc48d8c69943b6271"
LLAMA_COMMIT = "b0aca3c6539e2dd55ea510bbb79591852a4d4b81"
SHARDS = {
    "model-00001-of-00003.safetensors": (3957900840, "75311d91bb08cf0b882913da464a1e722a31fb44db35208663487efb7a3d8ed6"),
    "model-00002-of-00003.safetensors": (3987450520, "0b48adbb1f60e901153d91907ba11ce63bd4b8b584482e730f48808d055dfba1"),
    "model-00003-of-00003.safetensors": (99630640, "7dd39ccca5e4de123c74c14af44c9bf2eb75df33b4614382af0134528e060d5d"),
}
CONFIG_NAMES = ("config.json", "generation_config.json", "tokenizer.json", "tokenizer_config.json",
                "vocab.json", "merges.txt", "model.safetensors.index.json", "README.md", "LICENSE")


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def verify_adapter(root):
    root = Path(root)
    if sha256(root / "artifact-checksums.json") != V1_MANIFEST_SHA:
        raise ValueError("V1 artifact manifest differs from the evaluated freeze report.")
    manifest = json.loads((root / "artifact-checksums.json").read_text("utf-8"))
    for name, expected in manifest.items():
        path = (root / name).resolve()
        if not path.is_relative_to(root.resolve()) or sha256(path) != expected:
            raise ValueError(f"V1 artifact checksum mismatch: {name}")
    config = json.loads((root / "adapter/adapter_config.json").read_text("utf-8"))
    metadata = json.loads((root / "run-metadata.json").read_text("utf-8"))
    if (config["base_model_name_or_path"] != MODEL or config["revision"] != REVISION
            or config["r"] != 8 or config["lora_alpha"] != 16
            or set(config["target_modules"]) != {"q_proj", "k_proj", "v_proj", "o_proj"}
            or config["modules_to_save"] is not None or config["use_dora"]
            or metadata["model_id"] != MODEL or metadata["revision"] != REVISION
            or not metadata["immutable_revision_verified"] or not metadata["adapter_reload_passed"]
            or "PILOT_V1" not in metadata["purpose"]
            or sha256(root / "adapter/adapter_model.safetensors") != ADAPTER_SHA):
        raise ValueError("Artifact is not the evaluated pinned Pilot V1.")
    return len(manifest)


def verify_converter(root):
    root = Path(root)
    head = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    dirty = subprocess.check_output(["git", "-C", str(root), "status", "--porcelain"], text=True).strip()
    if head != LLAMA_COMMIT or dirty:
        raise ValueError("Converter must be the clean, exact llama.cpp b11321 commit.")


class PinnedSource:
    def __init__(self):
        import requests
        self.session = requests.Session()
        self.session.trust_env = False
        self.session.headers.update({"User-Agent": "LAC-Pilot-V1-Packaging/1.0"})
        self.tensor_hashes = {}
        self.tensor_keys = {}

    def url(self, filename):
        if "/" in filename or filename not in (*CONFIG_NAMES, *SHARDS):
            raise ValueError("Unrecognized base file.")
        return f"https://huggingface.co/{MODEL}/resolve/{REVISION}/{filename}"

    def range(self, url, start, size):
        import requests
        for attempt in range(3):
            try:
                return self._range_once(url, start, size)
            except requests.RequestException:
                if attempt == 2:
                    raise
                print(json.dumps({"public_weight_transport_retry": attempt + 1,
                                  "offset": start, "bytes": size}), flush=True)
                time.sleep(attempt + 1)

    def _range_once(self, url, start, size):
        filename = urlparse(url).path.rsplit("/", 1)[-1]
        if url != self.url(filename) or filename not in SHARDS or start < 0 or size <= 0:
            raise ValueError("Unpinned or invalid tensor range.")
        end = start + size - 1
        if end >= SHARDS[filename][0]:
            raise ValueError("Tensor range exceeds source file.")
        # Unique cache keys prevent a CDN from returning another range's cached bytes.
        with self.session.get(url, params={"lac_range": f"{start}-{end}"},
                              headers={"Range": f"bytes={start}-{end}", "Accept-Encoding": "identity"},
                              stream=True, timeout=(20, 120)) as response:
            response.raise_for_status()
            expected = f"bytes {start}-{end}/{SHARDS[filename][0]}"
            if response.status_code != 206 or response.headers.get("Content-Range") != expected:
                raise ValueError("Server did not confirm the exact requested tensor range.")
            data = bytearray()
            for block in response.iter_content(1024 * 1024):
                data.extend(block)
                if len(data) > size:
                    raise ValueError("Oversized tensor range response.")
            if len(data) != size:
                raise ValueError("Truncated tensor range response.")
            name = getattr(self, "tensor_keys", {}).get((url, start, size))
            if name:
                self.tensor_hashes[name] = hashlib.sha256(data).hexdigest()
                if len(self.tensor_hashes) % 16 == 0:
                    print(json.dumps({"source_tensors_read": len(self.tensor_hashes)}), flush=True)
            return data

    def cached_range(self, url, start, size, cache):
        filename = urlparse(url).path.rsplit("/", 1)[-1]
        if url != self.url(filename) or filename not in SHARDS or start < 0 or size <= 0 or start + size > SHARDS[filename][0]:
            raise ValueError("Unpinned cached range.")
        key = hashlib.sha256(f"{url}:{start}:{size}".encode()).hexdigest()
        path = Path(cache) / (key + ".bin")
        receipt = Path(cache) / (key + ".sha256")
        if path.exists() and receipt.exists():
            data = path.read_bytes()
            if len(data) != size or hashlib.sha256(data).hexdigest() != receipt.read_text("ascii").strip():
                raise ValueError("Cached pinned tensor range integrity mismatch.")
            return data
        if path.exists() or receipt.exists():
            raise ValueError("Incomplete cached range is preserved, not overwritten; use a fresh cache location.")
        data = self.range(url, start, size)
        with path.open("xb") as stream:
            stream.write(data)
        with receipt.open("x", encoding="ascii") as stream:
            stream.write(hashlib.sha256(data).hexdigest())
        return data

    def prepare(self, destination):
        destination = Path(destination)
        destination.mkdir(parents=True, exist_ok=True)
        response = self.session.get(
            f"https://huggingface.co/api/models/{MODEL}/revision/{REVISION}",
            params={"blobs": "true"}, timeout=(20, 60))
        response.raise_for_status()
        metadata = response.json()
        if metadata["sha"] != REVISION:
            raise ValueError("Base revision mismatch.")
        files = {item["rfilename"]: item for item in metadata["siblings"]}
        for name, (size, expected) in SHARDS.items():
            if files[name]["size"] != size or files[name]["lfs"]["sha256"] != expected:
                raise ValueError("Pinned base source metadata mismatch.")
        checksums = {}
        for name in CONFIG_NAMES:
            item = files[name]
            path = destination / name
            if path.exists():
                data = path.read_bytes()
            else:
                response = self.session.get(self.url(name), timeout=(20, 120))
                response.raise_for_status()
                data = response.content
            if len(data) != item["size"]:
                raise ValueError(f"Base config length mismatch: {name}")
            digest = hashlib.sha256(data).hexdigest()
            if "lfs" in item:
                valid = digest == item["lfs"]["sha256"]
            else:
                blob = b"blob " + str(len(data)).encode() + b"\0" + data
                valid = hashlib.sha1(blob).hexdigest() == item["blobId"]
            if not valid:
                raise ValueError(f"Pinned config checksum mismatch: {name}")
            if not path.exists():
                with path.open("xb") as stream:
                    stream.write(data)
            checksums[name] = digest
        return checksums

    def tensors(self, gguf, config):
        index = json.loads((Path(config) / "model.safetensors.index.json").read_text("utf-8"))["weight_map"]
        result = {}
        for filename in sorted(SHARDS):
            url = self.url(filename)
            length = struct.unpack("<Q", self.range(url, 0, 8))[0]
            if length > 1024 * 1024:
                raise ValueError("Oversized tensor header.")
            header = json.loads(self.range(url, 8, length))
            for name, entry in sorted(header.items()):
                if name == "__metadata__":
                    continue
                if name in result or index.get(name) != filename:
                    raise ValueError("Tensor index mismatch.")
                start, end = entry["data_offsets"]
                result[name] = gguf.utility.RemoteTensor(entry["dtype"], tuple(entry["shape"]),
                                                       8 + length + start, end - start, url)
        if set(result) != set(index):
            raise ValueError("Incomplete pinned base tensor index.")
        self.tensor_keys = {(t.url, t.offset_start, t.size): name for name, t in result.items()}
        return result

    def tensor_data(self, tensor, scratch):
        import numpy as np
        chunk_size = 4 * 1024 * 1024
        if tensor.size <= chunk_size:
            return self.range(tensor.url, tensor.offset_start, tensor.size)
        stream = tempfile.TemporaryFile(dir=scratch)
        cache = Path(scratch) / "range-cache"
        cache.mkdir(exist_ok=True)
        digest = hashlib.sha256()
        try:
            with ThreadPoolExecutor(max_workers=8) as executor:
                for batch_start in range(0, tensor.size, 8 * chunk_size):
                    offsets = list(range(batch_start, min(batch_start + 8 * chunk_size, tensor.size), chunk_size))
                    futures = [executor.submit(self.cached_range, tensor.url, tensor.offset_start + offset,
                                               min(chunk_size, tensor.size - offset), cache) for offset in offsets]
                    for future in futures:
                        data = future.result()
                        stream.write(data)
                        digest.update(data)
                    print(json.dumps({"tensor_bytes_downloaded": min(batch_start + 8 * chunk_size, tensor.size),
                                      "tensor_total_bytes": tensor.size}), flush=True)
            stream.flush()
            mapped = np.memmap(stream, dtype=np.uint8, mode="r+", shape=(tensor.size,))
            # Keep the temporary file alive until the lazy writer releases this buffer.
            mapped._pilot_stream = stream
            name = self.tensor_keys[(tensor.url, tensor.offset_start, tensor.size)]
            self.tensor_hashes[name] = digest.hexdigest()
            # Only this tensor's verified download-cache files are removed.
            for offset in range(0, tensor.size, chunk_size):
                size = min(chunk_size, tensor.size - offset)
                key = hashlib.sha256(f"{tensor.url}:{tensor.offset_start + offset}:{size}".encode()).hexdigest()
                (cache / (key + ".bin")).unlink()
                (cache / (key + ".sha256")).unlink()
            return mapped
        except BaseException:
            stream.close()
            raise


def convert(source, config, converter, output, scratch):
    verify_converter(converter)
    if Path(output).exists():
        raise ValueError("Refusing to overwrite existing base GGUF.")
    os.environ["USE_TF"] = "0"
    os.environ["USE_FLAX"] = "0"
    os.environ["OMP_NUM_THREADS"] = "1"
    sys.path[:0] = [str(Path(converter) / "gguf-py"), str(converter)]
    import torch
    import gguf
    from conversion import ModelBase, ModelType, get_model_architecture, get_model_class
    torch.set_num_threads(1)
    tensors = source.tensors(gguf, config)
    gguf.utility.SafetensorRemote.get_list_tensors_hf_model = classmethod(lambda cls, model_id: tensors)
    gguf.utility.SafetensorRemote.get_data_by_range = classmethod(lambda cls, url, start, size=-1: source.range(url, start, size))
    gguf.utility.RemoteTensor.data = lambda tensor: source.tensor_data(tensor, scratch)
    hparams = ModelBase.load_hparams(Path(config), False)
    model_class = get_model_class(get_model_architecture(hparams, ModelType.TEXT))
    with torch.inference_mode():
        model = model_class(Path(config), gguf.LlamaFileType.MOSTLY_BF16, Path(output),
                            remote_hf_model_id=MODEL, eager=False)
        model.write()
    if set(source.tensor_hashes) != set(tensors):
        raise ValueError("Not every pinned source tensor was consumed.")
    receipt = {"model": MODEL, "revision": REVISION, "converterCommit": LLAMA_COMMIT,
               "sourceFilePublisherChecksums": {name: digest for name, (_, digest) in SHARDS.items()},
               "sourceTensorSha256": source.tensor_hashes, "sourceTensorCount": len(tensors),
               "baseBf16Sha256": sha256(output)}
    with Path(str(output) + ".provenance.json").open("x", encoding="utf-8") as stream:
        json.dump(receipt, stream, indent=2)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--v1-root", type=Path, required=True)
    parser.add_argument("--converter", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--scratch", type=Path)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO)
    print(json.dumps({"v1_verified_files": verify_adapter(args.v1_root), "model": MODEL,
                      "revision": REVISION, "adapter_sha256": ADAPTER_SHA}), flush=True)
    verify_converter(args.converter)
    source = PinnedSource()
    checksums = source.prepare(args.config)
    print(json.dumps({"pinned_config_sha256": checksums}), flush=True)
    if args.output:
        scratch = args.scratch or args.output.parent
        scratch.mkdir(parents=True, exist_ok=True)
        convert(source, args.config, args.converter, args.output, scratch)
        print(json.dumps({"base_bf16_sha256": sha256(args.output)}), flush=True)


if __name__ == "__main__":
    main()
