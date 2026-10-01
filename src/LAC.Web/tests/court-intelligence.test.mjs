import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync } from 'node:fs';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import ts from 'typescript';
const source=readFileSync(new URL('../src/court/CourtIntelligence.tsx',import.meta.url),'utf8');
const js=ts.transpileModule(source.replace('import "./court-intelligence.css";',''),{compilerOptions:{module:ts.ModuleKind.ESNext,jsx:ts.JsxEmit.React,target:ts.ScriptTarget.ES2022}}).outputText;
// Resolve React without relying on package resolution from a data URL.
const evaluated=js.replace(/import React,.*?from "react";/,'const { useEffect, useState } = React;').replace('export const CourtIntelligence','const CourtIntelligence');
const component=new Function('React',evaluated+'; return CourtIntelligence;')(React);

test('Court intelligence renders calmly with AI off and makes no render-time action',()=>{
  const html=renderToStaticMarkup(React.createElement(component,{caseId:'case'}));
  assert.match(html,/COURT INTELLIGENCE/);
  assert.match(html,/AI-assisted summary/);
  assert.match(html,/Court records and official order links remain unchanged/);
  assert.doesNotMatch(html,/tokens|RAM|latency|confidence|raw JSON|Qwen|llama/i);
});
test('officer headings and evidence are present without developer metrics',()=>{
  for(const text of ['Current position','Before next hearing','Latest order','Order history','Timeline','Ask this case','Review source evidence'])assert.ok(source.includes(text));
  assert.doesNotMatch(source,/modelVersion|inferenceTime|confidencePercent|JSON\.stringify\(data/);
  assert.match(source,/source\.orderDate/);assert.match(source,/source\.page/);assert.match(source,/source\.evidence/);
});
test('questions require explicit submit and AI failure leaves intelligence available',()=>{
  assert.match(source,/onSubmit=\{ask\}/);
  assert.match(source,/Question answering is temporarily unavailable\. Case intelligence remains available/);
  assert.match(source,/activeCase\.current === requestedCase/);
  assert.doesNotMatch(source,/\/resume|captcha|dhc-assisted\/runs|sync-now|review-decision/);
});
test('review-marked orders display only independently usable summary facts',()=>{
  assert.match(source,/order\.summaryFacts \?\? \(order\.status === "Validated" \? order\.facts : \[\]\)/);
  assert.match(source,/const facts = safeFacts\.filter/);
  assert.match(source,/safeFacts\.find/);
});
