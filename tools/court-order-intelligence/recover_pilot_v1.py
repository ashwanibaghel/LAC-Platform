"""Read-only partial recovery and bounded BF16 writing; never loads an inference model."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from math import prod
import os
from pathlib import Path
import shutil
import sys

from package_pilot_v1 import (PinnedSource, MODEL, REVISION, LLAMA_COMMIT, SHARDS,
                             sha256, verify_adapter, verify_converter)

CHUNK = 4 * 1024 * 1024


def header(path):
    from gguf import GGUFReader, GGMLQuantizationType, GGUFEndian

    class HeaderOnly(GGUFReader):
        def _build_tensors(self, start, fields):
            self.records = []
            expected = start
            for field in fields:
                _, name, _, dims, dtype, offset = field.parts
                name = name.tobytes().decode("utf-8")
                shape = tuple(reversed(dims.tolist()))
                kind = GGMLQuantizationType(int(dtype[0]))
                if kind not in (GGMLQuantizationType.BF16, GGMLQuantizationType.F32):
                    raise ValueError("Recovery accepts only unquantized BF16/F32 tensors.")
                size = prod(shape) * (2 if kind == GGMLQuantizationType.BF16 else 4)
                position = start + int(offset[0])
                if position != expected or any(r["name"] == name for r in self.records):
                    raise ValueError("Non-contiguous or duplicate partial tensor metadata.")
                self.records.append({"name": name, "shape": shape, "kind": kind,
                                     "offset": position, "bytes": size})
                expected = position + size + (-size % int(self.alignment))
            self.expected_size = expected

    reader = HeaderOnly(str(path))
    if reader.endianess != GGUFEndian.LITTLE:
        raise ValueError("Only little-endian pinned conversion is supported.")
    return reader


def to_bf16(raw, kind):
    """Reverse upstream's exact finite BF16->F32 widening for 1D norms."""
    import numpy as np
    from gguf import GGMLQuantizationType
    if kind == GGMLQuantizationType.F32:
        values = np.frombuffer(raw, dtype="<u4")
        if np.any(values & 0xffff):
            raise ValueError("F32 tensor cannot be restored bit-exactly to BF16.")
        bits = (values >> 16).astype("<u2")
    elif kind == GGMLQuantizationType.BF16:
        bits = np.frombuffer(raw, dtype="<u2")
    else:
        raise ValueError("Unsupported recovery tensor type.")
    if np.any((bits & 0x7f80) == 0x7f80):
        raise ValueError("Non-finite source requires upstream conversion, not recovery.")
    return bits.tobytes()


def from_bf16(raw, kind):
    import numpy as np
    from gguf import GGMLQuantizationType
    checked = to_bf16(raw, GGMLQuantizationType.BF16)
    if kind == GGMLQuantizationType.BF16:
        return checked
    if kind == GGMLQuantizationType.F32:
        return (np.frombuffer(checked, dtype="<u2").astype("<u4") << 16).tobytes()
    raise ValueError("Unsupported output tensor type.")


def blocks(stream, start, size):
    stream.seek(start)
    remaining = size
    while remaining:
        data = stream.read(min(CHUNK, remaining))
        if not data:
            raise ValueError("Truncated recovery tensor.")
        remaining -= len(data)
        yield data


def plan(reader, tensors, config):
    import gguf
    if config["architectures"] != ["Qwen3ForCausalLM"] or config["torch_dtype"] != "bfloat16":
        raise ValueError("Recovery is restricted to the exact pinned BF16 Qwen3 architecture.")
    expected_fields = {
        "general.architecture": "qwen3", "general.file_type": int(gguf.LlamaFileType.MOSTLY_BF16),
        "qwen3.block_count": config["num_hidden_layers"],
        "qwen3.embedding_length": config["hidden_size"],
        "qwen3.feed_forward_length": config["intermediate_size"],
        "qwen3.context_length": config["max_position_embeddings"],
        "qwen3.attention.head_count": config["num_attention_heads"],
        "qwen3.attention.head_count_kv": config["num_key_value_heads"],
    }
    for name, expected in expected_fields.items():
        if name not in reader.fields or reader.fields[name].contents() != expected:
            raise ValueError(f"Pinned GGUF metadata mismatch: {name}")
    mapping = gguf.get_tensor_name_map(gguf.MODEL_ARCH.QWEN3, config["num_hidden_layers"])
    by_name = {}
    for name, source in tensors.items():
        target = mapping.get_name(name, try_suffixes=(".weight",))
        if not target or target in by_name or source.dtype != "BF16":
            raise ValueError("Unsupported or duplicate pinned source tensor mapping.")
        by_name[target] = (name, source)
    if len(by_name) != 398 or {r["name"] for r in reader.records} != set(by_name):
        raise ValueError("Partial header does not contain all 398 pinned tensors.")
    for record in reader.records:
        name, source = by_name[record["name"]]
        expected_kind = gguf.GGMLQuantizationType.F32 if len(source.shape) == 1 else gguf.GGMLQuantizationType.BF16
        if (tuple(source.shape) != record["shape"] or record["kind"] != expected_kind
                or source.size != prod(source.shape) * 2):
            raise ValueError("Pinned tensor shape/type/size mismatch.")
        record["source_name"] = name
        record["source"] = source
    return reader.records


def complete_prefix(records, file_size):
    result = []
    incomplete = False
    for record in records:
        complete = record["offset"] + record["bytes"] <= file_size
        if complete and incomplete:
            raise ValueError("Recovery tensor sequence is not a prefix.")
        if complete:
            result.append(record)
        else:
            incomplete = True
    return result


def audit(partial, reader, records):
    recovered = complete_prefix(records, Path(partial).stat().st_size)
    hashes = {}
    with Path(partial).open("rb") as stream:
        for record in recovered:
            digest = hashlib.sha256()
            for raw in blocks(stream, record["offset"], record["bytes"]):
                digest.update(to_bf16(raw, record["kind"]))
            hashes[record["source_name"]] = digest.hexdigest()
    return {"partialSha256": sha256(partial), "partialBytes": Path(partial).stat().st_size,
            "headerSha256": hashlib.sha256(reader.data[:reader.data_offset].tobytes()).hexdigest(),
            "recoveredTensorCount": len(recovered),
            "recoveredSourceBytes": sum(r["source"].size for r in recovered),
            "recoveredSourceTensorSha256": hashes}


def write_base(output, partial, reader, records, receipt, source, cache):
    """Fresh exclusive output; old partial/cache is read-only and never deleted."""
    output, cache = Path(output), Path(cache)
    if output.exists() or output.resolve() == Path(partial).resolve():
        raise ValueError("Refusing to overwrite any partial/model file.")
    cache.mkdir(parents=True, exist_ok=True)
    source_hashes = dict(receipt["recoveredSourceTensorSha256"])
    recovered = set(source_hashes)
    with output.open("xb", buffering=0) as target, Path(partial).open("rb") as original:
        target.write(reader.data[:reader.data_offset].tobytes())
        with ThreadPoolExecutor(max_workers=8) as executor:
            for index, record in enumerate(records):
                if target.tell() != record["offset"]:
                    raise ValueError("Output tensor offset mismatch.")
                if record["source_name"] in recovered:
                    digest = hashlib.sha256()
                    for raw in blocks(original, record["offset"], record["bytes"]):
                        digest.update(to_bf16(raw, record["kind"]))
                        target.write(raw)
                    if digest.hexdigest() != source_hashes[record["source_name"]]:
                        raise ValueError("Preserved tensor changed after audit.")
                else:
                    tensor = record["source"]
                    digest = hashlib.sha256()
                    for batch in range(0, tensor.size, CHUNK * 8):
                        offsets = range(batch, min(batch + CHUNK * 8, tensor.size), CHUNK)
                        futures = [executor.submit(source.cached_range, tensor.url, tensor.offset_start + pos,
                                                   min(CHUNK, tensor.size - pos), cache) for pos in offsets]
                        for future in futures:
                            if shutil.disk_usage(output.parent).free < 1024**3 or shutil.disk_usage(cache).free < 1024**3:
                                raise OSError("Packaging paused: less than 1 GiB disk reserve; all partial/cache retained.")
                            raw = future.result()
                            digest.update(raw)
                            target.write(from_bf16(raw, record["kind"]))
                    source_hashes[record["source_name"]] = digest.hexdigest()
                target.write(bytes(-record["bytes"] % int(reader.alignment)))
                os.fsync(target.fileno())
                checkpoint = {**receipt, "sourceTensorSha256": source_hashes,
                              "completedTensorCount": index + 1, "completedOutputBytes": target.tell()}
                with output.with_name(output.name + f".checkpoint-{index+1:03d}.json").open("x", encoding="utf-8") as stream:
                    json.dump(checkpoint, stream)
                print(json.dumps({"completed_tensors": index + 1, "total_tensors": len(records),
                                  "output_bytes": target.tell(), "reused": record["source_name"] in recovered}), flush=True)
        if target.tell() != reader.expected_size:
            raise ValueError("Final GGUF size mismatch.")
    if sha256(partial) != receipt["partialSha256"]:
        raise ValueError("Original partial changed during recovery.")
    provenance = {"model": MODEL, "revision": REVISION, "converterCommit": LLAMA_COMMIT,
                  "sourceFilePublisherChecksums": {n: h for n, (_, h) in SHARDS.items()},
                  "sourceTensorSha256": source_hashes, "sourceTensorCount": len(records),
                  "baseBf16Sha256": sha256(output), "recovery": receipt,
                  "conversionMethod": "Pinned upstream header/name/shape plan; bounded finite BF16 identity and F32 norm widening"}
    with Path(str(output) + ".provenance.json").open("x", encoding="utf-8") as stream:
        json.dump(provenance, stream, indent=2)


def existing_or_download(source, tensor, offset, size, cache):
    """Existing cache is preserved; new ranges stream directly into the new GGUF."""
    key = hashlib.sha256(f"{tensor.url}:{tensor.offset_start+offset}:{size}".encode()).hexdigest()
    if any((Path(cache) / (key + suffix)).exists() for suffix in (".bin", ".sha256")):
        return source.cached_range(tensor.url, tensor.offset_start + offset, size, cache)
    return source.range(tensor.url, tensor.offset_start + offset, size)


def write_sharded(directory, overflow, partial, reader, records, receipt, source, cache, split_size=2_000_000_000):
    """Standard upstream GGUF shards across two disks, with local file aliases."""
    import numpy as np
    import gguf
    from gguf.gguf_writer import WriterState
    directory, overflow = Path(directory), Path(overflow)
    if directory.exists() or overflow.exists():
        raise ValueError("Recovery shard directories must both be fresh.")
    directory.mkdir(parents=True)
    overflow.mkdir(parents=True)

    class ExclusiveWriter(gguf.GGUFWriter):
        def open_output_file(self, path=None):
            if self.state != WriterState.NO_FILE:
                raise ValueError("Output already opened.")
            names = self.format_shard_names(self.path)
            self.actual_paths = [p if i < 3 else overflow / p.name for i, p in enumerate(names)]
            if any(p.exists() for p in (*names, *self.actual_paths)):
                raise ValueError("Refusing existing shard or alias.")
            self.fout = [p.open("xb") for p in self.actual_paths]
            self.state = WriterState.EMPTY
            for alias, actual in zip(names, self.actual_paths):
                if alias != actual:
                    alias.symlink_to(actual.resolve())

    writer = ExclusiveWriter(directory / "qwen3-4b-pinned-bf16.gguf", "qwen3", split_max_size=split_size)
    for key, field in reader.fields.items():
        if key.startswith("GGUF.") or key == "general.architecture":
            continue
        writer.add_key_value(key, field.contents(), field.types[0],
                             field.types[1] if field.types[0] == gguf.GGUFValueType.ARRAY else None)
    for record in records:
        dtype = np.uint16 if record["kind"] == gguf.GGMLQuantizationType.BF16 else np.float32
        writer.add_tensor_info(record["name"], record["shape"], dtype, record["bytes"], record["kind"])
    # Require adequate space before any weights are written; no reliance on C: pagefile space.
    sizes = [sum(t.nbytes for t in group.values()) + 16*1024**2 for group in writer.tensors]
    for folder, required in ((directory, sum(sizes[:3])), (overflow, sum(sizes[3:]))):
        if shutil.disk_usage(folder).free < required + 1024**3:
            raise OSError("Insufficient staging space plus 1 GiB reserve; preserved files unchanged.")
    writer.write_header_to_file()
    writer.write_kv_data_to_file()
    writer.write_ti_data_to_file()
    hashes = dict(receipt["recoveredSourceTensorSha256"])
    recovered = set(hashes)
    shards = []
    completed = 0
    try:
        with Path(partial).open("rb") as original, ThreadPoolExecutor(max_workers=8) as executor:
            for path, target, group in zip(writer.actual_paths, writer.fout, writer.tensors):
                writer.write_padding(target, target.tell())
                for record in records:
                    if record["name"] not in group:
                        continue
                    digest = hashlib.sha256()
                    if record["source_name"] in recovered:
                        for raw in blocks(original, record["offset"], record["bytes"]):
                            digest.update(to_bf16(raw, record["kind"]))
                            if target.write(raw) != len(raw):
                                raise OSError("Short recovered tensor write.")
                        if digest.hexdigest() != hashes[record["source_name"]]:
                            raise ValueError("Preserved tensor changed after audit.")
                    else:
                        tensor = record["source"]
                        for batch in range(0, tensor.size, CHUNK*8):
                            futures = [executor.submit(existing_or_download, source, tensor, pos,
                                                       min(CHUNK, tensor.size-pos), cache)
                                       for pos in range(batch, min(batch+CHUNK*8, tensor.size), CHUNK)]
                            for future in futures:
                                if shutil.disk_usage(path.parent).free < 1024**3:
                                    raise OSError("Disk reserve reached; partial/cache retained.")
                                raw = future.result()
                                digest.update(raw)
                                widened = from_bf16(raw, record["kind"])
                                if target.write(widened) != len(widened):
                                    raise OSError("Short downloaded tensor write.")
                        hashes[record["source_name"]] = digest.hexdigest()
                    writer.write_padding(target, record["bytes"])
                    target.flush()
                    os.fsync(target.fileno())
                    completed += 1
                    checkpoint = {**receipt, "completedTensorCount": completed,
                                  "sourceTensorSha256": hashes, "currentShard": str(path),
                                  "currentShardBytes": target.tell()}
                    with (directory / f"checkpoint-{completed:03d}.json").open("x", encoding="utf-8") as stream:
                        json.dump(checkpoint, stream)
                    print(json.dumps({"completed_tensors": completed, "total_tensors": len(records),
                                      "shard_bytes": target.tell(), "reused": record["source_name"] in recovered}), flush=True)
        writer.close()
        if sha256(partial) != receipt["partialSha256"]:
            raise ValueError("Preserved partial changed during recovery.")
        for path, group in zip(writer.actual_paths, writer.tensors):
            checked = gguf.GGUFReader(str(path))
            if len(checked.tensors) != len(group):
                raise ValueError("Incomplete BF16 shard.")
            checked.data._mmap.close()
            shards.append({"path": str(path.resolve()), "file": path.name,
                           "sha256": sha256(path), "tensorCount": len(group)})
        aggregate = hashlib.sha256("".join(s["sha256"] for s in shards).encode("ascii")).hexdigest()
        provenance = {"model": MODEL, "revision": REVISION, "converterCommit": LLAMA_COMMIT,
                      "sourceFilePublisherChecksums": {n:h for n,(_,h) in SHARDS.items()},
                      "sourceTensorSha256": hashes, "sourceTensorCount": len(records),
                      "baseBf16Sha256": aggregate, "baseBf16HashKind": "ordered-shard-sha256-aggregate",
                      "baseBf16Shards": shards, "recovery": receipt,
                      "conversionMethod": "Pinned upstream GGUF shard metadata; bounded finite BF16 identity/F32 norm widening"}
        with (directory / "base.provenance.json").open("x", encoding="utf-8") as stream:
            json.dump(provenance, stream, indent=2)
        print(json.dumps({"shards_complete": len(shards), "tensor_count": completed}), flush=True)
    finally:
        writer.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("v1-root", "converter", "config", "partial", "audit"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--cache", type=Path)
    parser.add_argument("--shard-directory", type=Path)
    parser.add_argument("--overflow-directory", type=Path)
    args = parser.parse_args()
    verify_adapter(args.v1_root)
    verify_converter(args.converter)
    sys.path.insert(0, str(args.converter / "gguf-py"))
    source = PinnedSource()
    source.prepare(args.config)
    import gguf
    tensors = source.tensors(gguf, args.config)
    reader = header(args.partial)
    records = plan(reader, tensors, json.loads((args.config / "config.json").read_text("utf-8")))
    template = json.loads((args.config / "tokenizer_config.json").read_text("utf-8"))["chat_template"]
    if reader.fields["tokenizer.chat_template"].contents() != template:
        raise ValueError("Pinned chat template mismatch.")
    receipt = audit(args.partial, reader, records)
    with args.audit.open("x", encoding="utf-8") as stream:
        json.dump(receipt, stream, indent=2)
    print(json.dumps({k: v for k, v in receipt.items() if k != "recoveredSourceTensorSha256"}), flush=True)
    if args.output:
        if not args.cache:
            raise ValueError("Explicit preserved range cache required.")
        write_base(args.output, args.partial, reader, records, receipt, source, args.cache)
    if args.shard_directory:
        if not args.overflow_directory or not args.cache or args.output:
            raise ValueError("Sharded recovery requires two fresh directories/cache and no single output.")
        write_sharded(args.shard_directory, args.overflow_directory, args.partial, reader, records, receipt, source, args.cache)


if __name__ == "__main__":
    main()
