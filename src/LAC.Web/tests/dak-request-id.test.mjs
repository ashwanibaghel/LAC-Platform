import test from "node:test";
import assert from "node:assert/strict";
import { createDakRequestId } from "../src/dak/requestId.js";

const uuidV4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

test("Dak request ID uses randomUUID when available", () => {
  const expected = "01234567-89ab-4def-8123-456789abcdef";
  assert.equal(createDakRequestId({ randomUUID: () => expected, getRandomValues: () => assert.fail("Unexpected fallback") }), expected);
});

test("LAN HTTP without randomUUID generates deterministic RFC4122 v4 bits from getRandomValues", () => {
  let calls = 0;
  const source = { getRandomValues(bytes) { calls++; bytes.set(Array.from({ length: 16 }, (_, i) => i)); return bytes; } };
  const id = createDakRequestId(source);
  assert.equal(id, "00010203-0405-4607-8809-0a0b0c0d0e0f");
  assert.match(id, uuidV4);
  assert.equal(calls, 1);
});

test("all-ones entropy still produces correct version and variant without Math.random", () => {
  const original = Math.random;
  try {
    Math.random = () => assert.fail("Insecure randomness must not be used");
    assert.equal(createDakRequestId({ getRandomValues(bytes) { bytes.fill(255); return bytes; } }), "ffffffff-ffff-4fff-bfff-ffffffffffff");
  } finally { Math.random = original; }
});

test("each new receipt obtains fresh secure entropy", () => {
  let seed = 0;
  const source = { getRandomValues(bytes) { bytes.fill(++seed); return bytes; } };
  const a = createDakRequestId(source), b = createDakRequestId(source);
  assert.match(a, uuidV4); assert.match(b, uuidV4); assert.notEqual(a, b);
});
