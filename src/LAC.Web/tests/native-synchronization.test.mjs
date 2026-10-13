import test from 'node:test';
import assert from 'node:assert/strict';
import { NativeSynchronization } from '../src/matter/nativeSynchronization.ts';
const origin = 'http://localhost:8082', url = origin+'/cache/synthetic.docx';
const tick = () => new Promise(resolve=>setImmediate(resolve));

test('native export before changes-sent cannot release the save command', async()=>{
  const order=[]; const bridge=new NativeSynchronization(origin,()=>order.push('quiesced'),()=>order.push('export-requested'));
  bridge.documentState(true); let finished=false;
  const pending=bridge.quiesceAndExport().then(v=>{finished=true;return v});
  bridge.exported(url); await tick(); assert.equal(finished,false);
  assert.deepEqual(order,['quiesced','export-requested']);
  bridge.documentState(false); assert.equal(await pending,url);
});
test('changes-sent before native export still waits for export evidence', async()=>{
  const bridge=new NativeSynchronization(origin,()=>{},()=>{}); let finished=false;
  const p=bridge.quiesceAndExport().then(v=>{finished=true;return v});
  bridge.documentState(false); await tick(); assert.equal(finished,false);
  bridge.exported(url); assert.equal(await p,url);
});
test('another dirty transition while capturing cannot release synchronization', async()=>{
  const bridge=new NativeSynchronization(origin,()=>{},()=>{}); bridge.documentState(false);
  const p=bridge.quiesceAndExport(); bridge.documentState(true); bridge.exported(url);
  let finished=false; p.then(()=>finished=true); await tick(); assert.equal(finished,false);
  bridge.documentState(false); assert.equal(await p,url);
});
test('concurrent capture requests do not replace the original pending promise', async()=>{
  const bridge=new NativeSynchronization(origin,()=>{},()=>{}); const p=bridge.quiesceAndExport();
  await assert.rejects(bridge.quiesceAndExport(),/already pending/); bridge.exported(url); assert.equal(await p,url);
});
test('native errors and changed leases reject capture; old events cannot complete it', async()=>{
  const bridge=new NativeSynchronization(origin,()=>{},()=>{}); const p=bridge.quiesceAndExport();
  bridge.close(); bridge.exported(url); bridge.documentState(false);
  await assert.rejects(p,/session changed/); await assert.rejects(bridge.quiesceAndExport(),/session has ended/);
});
test('deadline never yields success when native synchronization is missing', async()=>{
  const bridge=new NativeSynchronization(origin,()=>{},()=>{},10); bridge.documentState(true);
  const p=bridge.quiesceAndExport(); bridge.exported(url); await assert.rejects(p,/timed out/);
});
test('an export request failure preserves failure rather than returning a save receipt', async()=>{
  const bridge=new NativeSynchronization(origin,()=>{},()=>{throw Error('native request failed')});
  await assert.rejects(bridge.quiesceAndExport(),/request failed/);
});
for(const unsafe of ['http://evil.invalid/file.docx','http://localhost:8083/file.docx','http://user@localhost:8082/file.docx',url+'#fragment']) {
  test('untrusted native export is rejected: '+unsafe,async()=>{
    const bridge=new NativeSynchronization(origin,()=>{},()=>{});const p=bridge.quiesceAndExport();bridge.exported(unsafe);
    await assert.rejects(p,/origin/);
  });
}
