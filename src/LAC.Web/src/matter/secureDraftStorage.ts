/**
 * Matter Noting Draft Storage & Security Architecture.
 *
 * SECURITY AUDIT POLICY (2026-10-11):
 * 1. Deriving encryption keys from non-secret public values (userId, matterId, public salts)
 *    is pseudo-encryption: an adversary obtaining localStorage can easily reproduce the key
 *    and decrypt sensitive government records. This is strictly prohibited.
 * 2. Any persistent client-side recovery requires non-derivable secret key material
 *    governed by an approved office security policy (e.g. WebCrypto non-extractable CryptoKey
 *    managed via hardware token, session HSM/KMS wrap, or authenticated user passkey).
 * 3. POLICY ENFORCEMENT:
 *    - Persistent browser draft caching in localStorage is DISABLED until a formal policy
 *      and non-derivable secret key mechanism are approved.
 *    - The authoritative, durable draft store is the server-side draft API
 *      (PUT /api/matters/{id}/noting/draft) with ETag revision tracking and 409 conflict detection.
 *    - All legacy records (lac_enc_draft_*, lac_draft_*) are purged immediately upon detection.
 *    - In-session editing relies on active memory state and honest offline indicators.
 */

export interface SecureRecoverableDraft {
  contentJson: string;
  canonicalText: string;
  baseRevision: number;
  savedAt: number;
  userId: string;
  matterId: string;
}

export interface EncryptedDraftRecord {
  userId: string;
  matterId: string;
  baseRevision: number;
  savedAt: number;
  saltHex: string;
  ivHex: string;
  cipherHex: string;
}

export const PERSISTENT_DRAFT_CACHING_ENABLED = false;

export function isPersistentDraftCachingEnabled(): boolean {
  return PERSISTENT_DRAFT_CACHING_ENABLED;
}

/**
 * Safely purges all legacy local draft records from browser localStorage.
 * Ensures cross-user session isolation and prevents residual draft leakage.
 */
export function purgeLegacyLocalDrafts(): void {
  if (typeof window === "undefined" || !window.localStorage) return;
  try {
    const keysToRemove: string[] = [];
    for (let i = 0; i < window.localStorage.length; i++) {
      const key = window.localStorage.key(i);
      if (key && (key.startsWith("lac_enc_draft_") || key.startsWith("lac_draft_"))) {
        keysToRemove.push(key);
      }
    }
    for (const key of keysToRemove) {
      try {
        window.localStorage.removeItem(key);
      } catch {}
    }
  } catch (err) {
    console.warn("[DraftStorage] Failed to purge legacy drafts:", err);
  }
}

// Automatically purge legacy unapproved records on script evaluation
if (typeof window !== "undefined") {
  purgeLegacyLocalDrafts();
}

/**
 * Safe local draft save entry point.
 * With persistent caching disabled by security policy, this strictly refrains
 * from persisting unapproved sensitive office notes into localStorage.
 */
export async function saveSecureLocalDraft(
  _userId: string,
  _matterId: string,
  _draft: Omit<SecureRecoverableDraft, "userId" | "matterId">
): Promise<boolean> {
  // Always clean up any legacy entries
  purgeLegacyLocalDrafts();

  if (!PERSISTENT_DRAFT_CACHING_ENABLED) {
    // Disabled until approved non-derivable key mechanism is established
    return false;
  }

  return false;
}

/**
 * Safe local draft load entry point.
 * With persistent caching disabled, returns null and purges any legacy records.
 */
export async function loadSecureLocalDraft(
  _userId: string,
  _matterId: string
): Promise<SecureRecoverableDraft | null> {
  purgeLegacyLocalDrafts();
  return null;
}

/**
 * Clears local draft for user and matter.
 */
export function clearSecureLocalDraft(_userId: string, _matterId: string): void {
  purgeLegacyLocalDrafts();
}

/**
 * Cryptographic engine using genuine non-derivable secret key material.
 * Used for approved cryptographic validation and adversarial testing.
 */
export function bytesToHex(bytes: Uint8Array): string {
  let hex = "";
  for (let i = 0; i < bytes.length; i++) {
    hex += bytes[i].toString(16).padStart(2, "0");
  }
  return hex;
}

export function hexToBytes(hex: string): Uint8Array {
  const bytes = new Uint8Array(hex.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = parseInt(hex.substring(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}

/**
 * Generates an approved cryptographically secure, non-derivable AES-GCM 256-bit key.
 */
export async function generateApprovedSecretKey(): Promise<CryptoKey> {
  if (typeof crypto === "undefined" || !crypto.subtle) {
    throw new Error("WebCrypto SubtleCrypto is required");
  }
  return await crypto.subtle.generateKey(
    { name: "AES-GCM", length: 256 },
    false, // non-extractable
    ["encrypt", "decrypt"]
  );
}

/**
 * Encrypts a draft payload using a genuine non-derivable secret key.
 */
export async function encryptWithSecretKey(
  secretKey: CryptoKey,
  payload: string,
  userId: string,
  matterId: string,
  baseRevision: number
): Promise<EncryptedDraftRecord> {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const enc = new TextEncoder();
  const encodedPayload = enc.encode(payload);

  const cipherBuffer = await crypto.subtle.encrypt(
    { name: "AES-GCM", iv: iv as any },
    secretKey,
    encodedPayload as any
  );

  return {
    userId,
    matterId,
    baseRevision,
    savedAt: Date.now(),
    saltHex: bytesToHex(salt),
    ivHex: bytesToHex(iv),
    cipherHex: bytesToHex(new Uint8Array(cipherBuffer))
  };
}

/**
 * Decrypts an EncryptedDraftRecord using the approved secret key.
 */
export async function decryptWithSecretKey(
  secretKey: CryptoKey,
  record: EncryptedDraftRecord
): Promise<string> {
  const iv = hexToBytes(record.ivHex);
  const cipher = hexToBytes(record.cipherHex);

  const decryptedBuffer = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: iv as any },
    secretKey,
    cipher as any
  );

  const dec = new TextDecoder();
  return dec.decode(decryptedBuffer);
}

/**
 * INSECURE / ADVERSARIAL derivation helper:
 * Attempts to derive a key solely from public IDs + application code.
 * Used exclusively by tests to prove that possession of localStorage,
 * public IDs, and source code is mathematically INSUFFICIENT to decrypt
 * a draft protected with an approved secret key.
 */
export async function attemptInsecurePublicDerivation(
  userId: string,
  matterId: string,
  salt: Uint8Array
): Promise<CryptoKey> {
  const enc = new TextEncoder();
  const rawKeyMaterial = enc.encode(`lac:insecure-public-ids:${userId}:${matterId}`);
  const keyMaterial = await crypto.subtle.importKey(
    "raw",
    rawKeyMaterial,
    { name: "PBKDF2" },
    false,
    ["deriveKey"]
  );

  return await crypto.subtle.deriveKey(
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
