import type { OfficeSaveReceiptDto, OfficeSaveResolutionDto } from './notingTypes';

export function validateOfficeSaveResolution(result: OfficeSaveResolutionDto, sessionId: string, expectedRevision: number): OfficeSaveResolutionDto {
  if (typeof result.noChanges !== 'boolean' || !result.snapshotId || !/^[a-f0-9]{64}$/i.test(result.sha256) ||
      !Number.isInteger(result.snapshotVersion) || result.snapshotVersion < 0 || result.sessionRevision !== expectedRevision) {
    throw new Error('Server content verification is missing valid current snapshot evidence.');
  }
  const receipt = result.committedReceipt;
  if (receipt && (!result.noChanges || receipt.state !== 'Committed' || receipt.sessionId !== sessionId ||
      receipt.snapshotId !== result.snapshotId || receipt.sha256 !== result.sha256 || receipt.version !== result.snapshotVersion ||
      receipt.sessionRevision !== result.sessionRevision || !receipt.id || !Number.isInteger(receipt.version) || receipt.version! < 1 ||
      !Number.isInteger(receipt.clientGeneration) || receipt.clientGeneration < 0)) {
    throw new Error('No-change verification refers to a different or incomplete committed receipt.');
  }
  if (result.noChanges && !receipt && result.snapshotVersion !== 0) throw new Error('No-change content has no valid committed receipt or server seed.');
  return result; // Preserve the original receipt's identity/generation; never fabricate a newer save.
}

/** A command acknowledgment is not a save. Keep the caller's operation pending
 * until the exact request has committed, failed, or exceeded its server deadline. */
export async function awaitCommittedOfficeSave(
  initial: OfficeSaveReceiptDto,
  read: () => Promise<OfficeSaveReceiptDto>,
  observe: (receipt: OfficeSaveReceiptDto) => void,
  wait: () => Promise<void> = () => new Promise(resolve => setTimeout(resolve, 500)),
  now: () => number = Date.now,
): Promise<OfficeSaveReceiptDto> {
  let receipt = initial;
  const deadline = Math.min(Date.parse(initial.deadline), now() + 120_000);
  if (!Number.isFinite(deadline)) throw new Error('Save receipt has no valid deadline.');
  for (;;) {
    if (receipt.id !== initial.id || receipt.sessionId !== initial.sessionId ||
        receipt.clientGeneration !== initial.clientGeneration) {
      throw new Error('Save status belongs to a different request, session or edit generation.');
    }
    observe(receipt);
    if (receipt.state === 'Failed') throw new Error(`Save failed: ${receipt.failureCode || 'rejected by server'}. Last committed bytes retained.`);
    if (receipt.state === 'Committed') {
      if (!receipt.snapshotId || !receipt.sha256 || !/^[a-f0-9]{64}$/i.test(receipt.sha256) ||
          !Number.isInteger(receipt.version) || receipt.version! < 1) {
        throw new Error('Committed save receipt is missing snapshot, hash or version evidence.');
      }
      return receipt;
    }
    if (receipt.state !== 'Pending') throw new Error('Unknown save state; review remains blocked.');
    if (now() >= deadline) throw new Error('Save timed out before server commit was confirmed.');
    await wait();
    receipt = await read();
  }
}

export function submittedNoteNumber(value: unknown): number {
  const response = value as { number?: unknown; note?: { number?: unknown } } | null;
  const number = response?.number ?? response?.note?.number;
  if (!Number.isInteger(number) || (number as number) < 1) {
    throw new Error('Submission response did not identify an official note. Refresh the official notes before retrying.');
  }
  return number as number;
}
