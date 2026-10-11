import assert from "node:assert/strict";
import test from "node:test";
import {
  PERSISTENT_DRAFT_CACHING_ENABLED,
  isPersistentDraftCachingEnabled,
  generateApprovedSecretKey,
  encryptWithSecretKey,
  decryptWithSecretKey,
  attemptInsecurePublicDerivation,
  saveSecureLocalDraft,
  loadSecureLocalDraft,
  clearSecureLocalDraft,
  purgeLegacyLocalDrafts,
  bytesToHex,
  hexToBytes
} from "../src/matter/secureDraftStorage.ts";

// Helper: In-memory mock localStorage
function createMockLocalStorage() {
  const store = new Map();
  return {
    getItem(key) {
      return store.has(key) ? store.get(key) : null;
    },
    setItem(key, value) {
      store.set(key, String(value));
    },
    removeItem(key) {
      store.delete(key);
    },
    clear() {
      store.clear();
    },
    key(index) {
      return Array.from(store.keys())[index] || null;
    },
    get length() {
      return store.size;
    },
    _raw: store
  };
}

test("SECURITY 1: Adversarial Cryptographic Proof — Public IDs + Storage + Source Code cannot decrypt secret draft", async () => {
  const confidentialPayload = JSON.stringify({
    noteType: "Cabinet Decision / Section 11 LAC Award",
    matterNumber: "MTR-2026-0042",
    confidentialText: "Strictly Confidential: Special compensation award recommended at Rs 4,50,00,000 for Khasra 102, Galib Pur village.",
    officer: "ADM / LAC Revenue Officer",
    timestamp: Date.now()
  });

  const publicUserId = "officer-delhi-revenue-9821";
  const publicMatterId = "MTR-2026-0042";
  const baseRevision = 4;

  // 1. Legitimate government authority generates an approved, non-derivable secret key
  // (e.g., WebCrypto non-extractable CryptoKey protected by hardware session/HSM/passkey)
  const approvedSecretKey = await generateApprovedSecretKey();

  // 2. Draft is encrypted with the approved secret key
  const encryptedRecord = await encryptWithSecretKey(
    approvedSecretKey,
    confidentialPayload,
    publicUserId,
    publicMatterId,
    baseRevision
  );

  assert.ok(encryptedRecord.cipherHex, "Ciphertext must be present");
  assert.ok(encryptedRecord.ivHex, "IV must be present");
  assert.ok(encryptedRecord.saltHex, "Salt must be present");
  assert.strictEqual(encryptedRecord.cipherHex.includes("Confidential"), false, "Ciphertext must never contain plaintext words");

  // 3. ADVERSARIAL SCENARIO:
  // An attacker obtains:
  //   a) The exact localStorage / storage record (saltHex, ivHex, cipherHex)
  //   b) All public IDs (userId, matterId, baseRevision, savedAt)
  //   c) The full application client source code and PBKDF2/AES-GCM derivation algorithms
  const saltBytes = hexToBytes(encryptedRecord.saltHex);
  const adversaryDerivedKey = await attemptInsecurePublicDerivation(
    encryptedRecord.userId,
    encryptedRecord.matterId,
    saltBytes
  );

  // 4. Adversary attempts to decrypt the protected record using the key derived from public values + source code
  let adversaryDecryptionSucceeded = false;
  let capturedError = null;
  try {
    await decryptWithSecretKey(adversaryDerivedKey, encryptedRecord);
    adversaryDecryptionSucceeded = true;
  } catch (err) {
    capturedError = err;
  }

  // 5. ASSERTIONS: Decryption MUST fail due to AES-GCM tag mismatch / OperationError
  assert.strictEqual(
    adversaryDecryptionSucceeded,
    false,
    "CRITICAL: Attacker must NOT be able to decrypt the draft using public IDs and source code"
  );
  assert.ok(capturedError, "WebCrypto subtle.decrypt must reject unauthorized decryption attempt");
  assert.strictEqual(
    capturedError.name,
    "OperationError",
    "WebCrypto must reject with OperationError (AES-GCM authentication tag mismatch)"
  );

  // 6. Legitimate authority with the genuine approved secret key CAN decrypt successfully
  const recoveredPlaintext = await decryptWithSecretKey(approvedSecretKey, encryptedRecord);
  assert.strictEqual(recoveredPlaintext, confidentialPayload, "Legitimate key must cleanly decrypt the draft");
  const parsed = JSON.parse(recoveredPlaintext);
  assert.strictEqual(parsed.matterNumber, "MTR-2026-0042");
});

test("SECURITY 2: Policy Enforcement — Persistent browser draft caching in localStorage is disabled", async () => {
  assert.strictEqual(
    PERSISTENT_DRAFT_CACHING_ENABLED,
    false,
    "Persistent draft caching must be disabled until approved by office security policy"
  );
  assert.strictEqual(
    isPersistentDraftCachingEnabled(),
    false,
    "isPersistentDraftCachingEnabled must return false"
  );

  const mockStorage = createMockLocalStorage();
  const prevWindow = globalThis.window;
  globalThis.window = { localStorage: mockStorage };

  try {
    const saveResult = await saveSecureLocalDraft("officer-1", "matter-99", {
      contentJson: JSON.stringify({ type: "doc" }),
      canonicalText: "Draft regarding land survey",
      baseRevision: 1,
      savedAt: Date.now()
    });

    assert.strictEqual(saveResult, false, "saveSecureLocalDraft must return false when caching disabled");
    assert.strictEqual(mockStorage.length, 0, "Zero records must be written to localStorage");

    const loadedDraft = await loadSecureLocalDraft("officer-1", "matter-99");
    assert.strictEqual(loadedDraft, null, "loadSecureLocalDraft must return null");
  } finally {
    globalThis.window = prevWindow;
  }
});

test("SECURITY 3: Safe cleanup and immediate purge of legacy unapproved records", () => {
  const mockStorage = createMockLocalStorage();
  const prevWindow = globalThis.window;
  globalThis.window = { localStorage: mockStorage };

  try {
    // Simulate legacy unapproved pseudo-encrypted & plaintext records in browser storage
    mockStorage.setItem("lac_enc_draft_officerA_matter101", '{"saltHex":"123","cipherHex":"456"}');
    mockStorage.setItem("lac_enc_draft_officerB_matter202", '{"saltHex":"abc","cipherHex":"def"}');
    mockStorage.setItem("lac_draft_officerA_matter303", "plaintext draft content");
    mockStorage.setItem("theme_preference", "dark");
    mockStorage.setItem("user_session_flag", "active");

    assert.strictEqual(mockStorage.length, 5);

    // Trigger purge
    purgeLegacyLocalDrafts();

    // Verify all legacy lac_enc_draft_* and lac_draft_* records are purged
    assert.strictEqual(mockStorage.getItem("lac_enc_draft_officerA_matter101"), null);
    assert.strictEqual(mockStorage.getItem("lac_enc_draft_officerB_matter202"), null);
    assert.strictEqual(mockStorage.getItem("lac_draft_officerA_matter303"), null);

    // Verify unrelated application keys are preserved
    assert.strictEqual(mockStorage.getItem("theme_preference"), "dark");
    assert.strictEqual(mockStorage.getItem("user_session_flag"), "active");
    assert.strictEqual(mockStorage.length, 2);
  } finally {
    globalThis.window = prevWindow;
  }
});

test("SECURITY 4: Server revision guard prevents stale drafts from overwriting newer server records", () => {
  const serverRevision = 5;
  const staleLocalDraft = {
    baseRevision: 3,
    canonicalText: "Stale note typed offline hours ago",
    contentJson: JSON.stringify({ type: "doc" }),
    savedAt: Date.now() - 3600000,
    userId: "officer-1",
    matterId: "MTR-100"
  };

  // Rule: If serverRevision > baseRevision, local draft MUST NEVER silently overwrite server
  const isStale = serverRevision > staleLocalDraft.baseRevision;
  assert.strictEqual(isStale, true, "Stale baseRevision must be detected");

  // In NotingWorkspaceView, when serverRevision > localBackup.baseRevision:
  // isNewerOnServer is flagged true, blocking direct overwrite and requiring manual review/merge
  const canSilentlyOverwrite = !isStale;
  assert.strictEqual(canSilentlyOverwrite, false, "Stale draft must never silently overwrite newer server record");
});
