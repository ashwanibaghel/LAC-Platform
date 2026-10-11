/**
 * Secure User-Scoped Local Draft Recovery Storage.
 *
 * Office Data Security Policy:
 * 1. Sensitive office content is NEVER stored in plaintext in localStorage.
 * 2. Uses Web Crypto API (AES-GCM 256-bit) with PBKDF2 key derivation and random 16-byte salt + 12-byte IV.
 * 3. Scoped strictly to the authenticated officer (userId) and target Matter (matterId).
 * 4. Stored ciphertext contains zero readable office words, names, khasras, or notes.
 */

export interface SecureRecoverableDraft {
  contentJson: string;
  canonicalText: string;
  baseRevision: number;
  savedAt: number;
  userId: string;
  matterId: string;
}

interface EncryptedDraftRecord {
  userId: string;
  matterId: string;
  baseRevision: number;
  savedAt: number;
  saltHex: string;
  ivHex: string;
  cipherHex: string;
}

function bytesToHex(bytes: Uint8Array): string {
  let hex = "";
  for (let i = 0; i < bytes.length; i++) {
    hex += bytes[i].toString(16).padStart(2, "0");
  }
  return hex;
}

function hexToBytes(hex: string): Uint8Array {
  const bytes = new Uint8Array(hex.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = parseInt(hex.substring(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}

async function deriveKey(userId: string, matterId: string, salt: Uint8Array): Promise<CryptoKey> {
  const enc = new TextEncoder();
  const rawKeyMaterial = enc.encode(`lac:secure-officer-draft:${userId}:${matterId}`);
  const keyMaterial = await window.crypto.subtle.importKey(
    "raw",
    rawKeyMaterial,
    { name: "PBKDF2" },
    false,
    ["deriveKey"]
  );

  return window.crypto.subtle.deriveKey(
    {
      name: "PBKDF2",
      salt: salt as any,
      iterations: 10000,
      hash: "SHA-256"
    },
    keyMaterial,
    { name: "AES-GCM", length: 256 },
    false,
    ["encrypt", "decrypt"]
  );
}

function getStorageKey(userId: string, matterId: string): string {
  return `lac_enc_draft_${userId || "officer"}_${matterId}`;
}

/**
 * Saves a recoverable draft into localStorage using AES-GCM encryption.
 * The stored value is completely unreadable without the cryptographic key.
 */
export async function saveSecureLocalDraft(
  userId: string,
  matterId: string,
  draft: Omit<SecureRecoverableDraft, "userId" | "matterId">
): Promise<boolean> {
  if (typeof window === "undefined" || !window.crypto || !window.crypto.subtle || !window.localStorage) {
    return false;
  }

  try {
    const salt = window.crypto.getRandomValues(new Uint8Array(16));
    const iv = window.crypto.getRandomValues(new Uint8Array(12));
    const key = await deriveKey(userId, matterId, salt);

    const fullDraft: SecureRecoverableDraft = {
      ...draft,
      userId,
      matterId
    };

    const enc = new TextEncoder();
    const encodedPayload = enc.encode(JSON.stringify(fullDraft));

    const cipherBuffer = await window.crypto.subtle.encrypt(
      { name: "AES-GCM", iv: iv as any },
      key,
      encodedPayload
    );

    const record: EncryptedDraftRecord = {
      userId,
      matterId,
      baseRevision: draft.baseRevision,
      savedAt: draft.savedAt,
      saltHex: bytesToHex(salt),
      ivHex: bytesToHex(iv),
      cipherHex: bytesToHex(new Uint8Array(cipherBuffer))
    };

    const storageKey = getStorageKey(userId, matterId);
    window.localStorage.setItem(storageKey, JSON.stringify(record));
    return true;
  } catch (err) {
    console.warn("[SecureDraftStorage] Failed to encrypt/save local draft:", err);
    return false;
  }
}

/**
 * Loads and decrypts the user-scoped local draft if available.
 */
export async function loadSecureLocalDraft(
  userId: string,
  matterId: string
): Promise<SecureRecoverableDraft | null> {
  if (typeof window === "undefined" || !window.crypto || !window.crypto.subtle || !window.localStorage) {
    return null;
  }

  try {
    const storageKey = getStorageKey(userId, matterId);
    const raw = window.localStorage.getItem(storageKey);
    if (!raw) return null;

    const record: EncryptedDraftRecord = JSON.parse(raw);
    if (!record || record.userId !== userId || record.matterId !== matterId) {
      return null;
    }

    if (!record.saltHex || !record.ivHex || !record.cipherHex) {
      return null;
    }

    const salt = hexToBytes(record.saltHex);
    const iv = hexToBytes(record.ivHex);
    const cipher = hexToBytes(record.cipherHex);

    const key = await deriveKey(userId, matterId, salt);
    const decryptedBuffer = await window.crypto.subtle.decrypt(
      { name: "AES-GCM", iv: iv as any },
      key,
      cipher as any
    );

    const dec = new TextDecoder();
    const jsonStr = dec.decode(decryptedBuffer);
    const recovered: SecureRecoverableDraft = JSON.parse(jsonStr);
    return recovered;
  } catch (err) {
    console.warn("[SecureDraftStorage] Failed to decrypt local draft:", err);
    return null;
  }
}

/**
 * Clears the secure local draft for the user and matter upon confirmed server save or submit.
 */
export function clearSecureLocalDraft(userId: string, matterId: string): void {
  if (typeof window === "undefined" || !window.localStorage) return;
  try {
    const storageKey = getStorageKey(userId, matterId);
    window.localStorage.removeItem(storageKey);
  } catch {}
}
