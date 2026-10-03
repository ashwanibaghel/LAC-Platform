import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
const source=readFileSync(new URL('../src/court/CourtRequestError.ts',import.meta.url),'utf8');
const js=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.ESNext}}).outputText;
const {responseError}=await import(`data:text/javascript;base64,${Buffer.from(js).toString('base64')}`);
test('Court requests expose validation reasons without weakening guards',async()=>{
  assert.equal(await responseError(new Response(JSON.stringify({error:'Review reason is required.'}),{status:400}),'fallback'),'Review reason is required.');
  assert.equal(await responseError(new Response(JSON.stringify({detail:'Evidence is stale.'}),{status:409}),'fallback'),'Evidence is stale.');
});
test('Expired sessions explain sign-in and server errors do not leak internals',async()=>{
  assert.match(await responseError(new Response('',{status:401}),'fallback'),/sign in again/);
  assert.equal(await responseError(new Response('secret stack trace',{status:500}),'fallback'),'fallback');
  assert.equal(await responseError(new Response('<html>',{status:400}),'fallback'),'fallback');
});
