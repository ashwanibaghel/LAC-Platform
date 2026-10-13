import test from 'node:test';
import assert from 'node:assert/strict';
import { awaitCommittedOfficeSave, submittedNoteNumber, validateOfficeSaveResolution } from '../src/matter/officeSaveBarrier.ts';

const receipt = (state = 'Pending') => ({
  id: 'request-A', sessionId: 'session-A', clientGeneration: 7, sessionRevision: 2,
  state, deadline: new Date(Date.now()+45000).toISOString(),
  snapshotId: state === 'Committed' ? 'snapshot-A' : null,
  sha256: state === 'Committed' ? 'a'.repeat(64) : null,
  version: state === 'Committed' ? 1 : null, failureCode: null,
});

test('review awaits the matching actual commit and uses its current session revision', async () => {
  let release;
  const revisions = [];
  let finished = false;
  const committed = {...receipt('Committed'), sessionRevision: 3};
  const operation = awaitCommittedOfficeSave(receipt(),
    () => new Promise(resolve => { release = () => resolve(committed); }),
    r => revisions.push(r.sessionRevision), () => Promise.resolve());
  operation.then(() => { finished = true; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(finished, false, 'command acknowledgment must not release serialization or review');
  release();
  assert.equal(await operation, committed, 'caller receives current commit, not a stale React state snapshot');
  assert.deepEqual(revisions, [2,3]);
});

test('a failed command cannot authorize canonical review', async () => {
  await assert.rejects(awaitCommittedOfficeSave({...receipt('Failed'), failureCode:'DocumentServerCommandRejected'},
    () => {throw Error('Must not poll failed result');}, () => {}), /DocumentServerCommandRejected/);
});

test('deadline expires without claiming saved content', async () => {
  let clock=0;
  await assert.rejects(awaitCommittedOfficeSave({...receipt(),deadline:new Date(1000).toISOString()},
    async () => receipt(), () => {}, async () => {clock=2000;}, () => clock), /timed out/);
});

for (const changed of [{id:'request-B'},{sessionId:'session-B'},{clientGeneration:8}]) {
  test(`reject unrelated receipt ${Object.keys(changed)[0]}`, async () => {
    await assert.rejects(awaitCommittedOfficeSave(receipt(), async () => ({...receipt('Committed'),...changed}),
      () => {}, () => Promise.resolve()), /different request, session or edit generation/);
  });
}

for (const missing of [{snapshotId:null},{sha256:null},{sha256:'not-a-hash'},{version:null},{version:0}]) {
  test(`reject incomplete committed evidence ${JSON.stringify(missing)}`, async () => {
    await assert.rejects(awaitCommittedOfficeSave({...receipt('Committed'),...missing}, async () => receipt(), () => {}), /missing snapshot, hash or version/);
  });
}

test('submit and send use the actual backend number fields', () => {
  assert.equal(submittedNoteNumber({id:'note-A',number:1}), 1);
  assert.equal(submittedNoteNumber({note:{id:'note-B',number:2},dak:{revision:6}}), 2);
  for (const malformed of [null,{}, {noteNumber:2}, {number:undefined}, {number:0}]) {
    assert.throws(() => submittedNoteNumber(malformed), /did not identify an official note/);
  }
});

const verified = () => ({ noChanges:true, snapshotId:'snapshot-A',sha256:'a'.repeat(64),snapshotVersion:1,sessionRevision:2,committedReceipt:receipt('Committed') });
test('no-change verification preserves the original receipt and its older generation',()=>{
  const value=verified();const result=validateOfficeSaveResolution(value,'session-A',2);
  assert.equal(result,value);assert.equal(result.committedReceipt,value.committedReceipt);
  assert.equal(result.committedReceipt.clientGeneration,7,'a later local capture cannot invent a newer committed generation');
});
test('reopened server seed has no fabricated committed save receipt',()=>{
  const value={...verified(),snapshotVersion:0,committedReceipt:null};assert.equal(validateOfficeSaveResolution(value,'session-A',2),value);
  assert.throws(()=>validateOfficeSaveResolution({...value,snapshotVersion:1},'session-A',2),/no valid committed receipt or server seed/);
  assert.throws(()=>validateOfficeSaveResolution({...value,committedReceipt:{...receipt('Committed'),version:0}},'session-A',2),/different or incomplete/);
});
for(const patch of [{sessionId:'another-session'},{snapshotId:'another-snapshot'},{sha256:'b'.repeat(64)},{sessionRevision:3},{state:'Failed'}]){
  test(`reject no-change receipt mismatch ${Object.keys(patch)[0]}`,()=>{
    const value=verified();assert.throws(()=>validateOfficeSaveResolution({...value,committedReceipt:{...value.committedReceipt,...patch}},'session-A',2),/different or incomplete/);
  });
}
test('changed content or stale revision cannot reuse a committed no-change receipt',()=>{
  assert.throws(()=>validateOfficeSaveResolution({...verified(),noChanges:false},'session-A',2),/different or incomplete/);
  assert.throws(()=>validateOfficeSaveResolution(verified(),'session-A',3),/valid current snapshot evidence/);
});
