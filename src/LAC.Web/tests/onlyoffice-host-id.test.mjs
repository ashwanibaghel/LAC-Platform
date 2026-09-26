import assert from "node:assert/strict";
import test from "node:test";

import { createOfficeHostId } from "../src/editor/officeHostId.js";

test("ONLYOFFICE host ids are unique without browser crypto APIs", () => {
  const originalCrypto = globalThis.crypto;

  try {
    // Mirrors a plain HTTP LAN browser where randomUUID is unavailable.
    Object.defineProperty(globalThis, "crypto", {
      configurable: true,
      value: undefined,
    });

    const letterHost = createOfficeHostId("letter-draft");
    const notingHost = createOfficeHostId("noting-draft");
    const retryHost = createOfficeHostId("letter-draft");
    const reopenedHost = createOfficeHostId("letter-draft");

    assert.match(letterHost, /^office-letter-draft-\d+$/);
    assert.match(notingHost, /^office-noting-draft-\d+$/);
    assert.notEqual(letterHost, notingHost, "Letter and Noting hosts must not collide");
    assert.notEqual(letterHost, retryHost, "Retry must create a fresh editor host");
    assert.notEqual(retryHost, reopenedHost, "Remount/reopen must create a fresh editor host");
  } finally {
    Object.defineProperty(globalThis, "crypto", {
      configurable: true,
      value: originalCrypto,
    });
  }
});
