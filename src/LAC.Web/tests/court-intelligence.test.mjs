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
const presentation=new Function('React',evaluated+'; return {officerText, actionPeriod, confirmedNextHearing};')(React);
const evidence={orderDate:'2025-01-30',page:6,evidence:'2. The LAC shall forward the reference preferably within four weeks.',officialUrl:'https://delhihighcourt.nic.in/app/test.pdf'};
const direction={category:'COURT_DIRECTION',field:'direction',scope:'Current',value:evidence.evidence,page:6,evidence:evidence.evidence};
const data={status:'NeedsReview',processingComplete:true,currentPosition:[{text:'2. Reference was declined.',source:{...evidence,evidence:'2. Reference was declined.'}}],beforeNextHearing:[{id:'a',text:direction.value,actor:'LAC',deadlineText:'preferably within four weeks',dueDate:'2025-02-27',source:evidence}],latestOrder:{orderDate:'2025-01-30',officialUrl:evidence.officialUrl,status:'NeedsReview',facts:[direction],summaryFacts:[direction]},orders:[]};
function officerMarkup(artifact){
  let stateIndex=0;
  const passiveReact={...React,useEffect:()=>{},useRef:value=>({current:value}),useState:initial=>[stateIndex++===0?artifact:initial,()=>{}]};
  const populated=new Function('React',evaluated+'; return CourtIntelligence;')(passiveReact);
  return renderToStaticMarkup(React.createElement(populated,{caseId:'case'}));
}
test('no confirmed hearing uses outstanding action, not next-hearing language',()=>{
  const html=officerMarkup(data);
  assert.match(html,/<h4>Outstanding LAC action<\/h4>/);
  assert.doesNotMatch(html,/<h4>Before next hearing<\/h4>/);
  assert.match(html,/What is still pending from LAC/);
  assert.equal(false,presentation.confirmedNextHearing({...data.latestOrder,nextHearingDate:'2026-10-05'}));
});
test('source-confirmed next hearing enables the before-next-hearing heading',()=>{
  const latest={...data.latestOrder,nextHearingDate:'2026-10-05',summaryFacts:[direction,{...direction,field:'nextHearing',value:'Renotify on 05.10.2026.'}]};
  assert.match(officerMarkup({...data,latestOrder:latest}),/<h4>Before next hearing<\/h4>/);
  assert.equal(false,presentation.confirmedNextHearing({...latest,summaryFacts:[]}));
});
test('preferred periods are qualified, never displayed as unconditional Due dates',()=>{
  const action=data.beforeNextHearing[0];
  assert.equal(presentation.actionPeriod(action,'2026-10-02'),'Preferred period ended 27 Feb 2025 · completion not confirmed');
  assert.equal(presentation.actionPeriod(action,'2025-02-01'),'Preferred period ends 27 Feb 2025 · completion not confirmed');
  assert.doesNotMatch(presentation.actionPeriod(action,'2026-10-02'),/Due/);
  assert.match(presentation.actionPeriod({...action,dueDate:null}),/preferably within four weeks/);
});
test('officer summaries strip paragraph prefixes but evidence stays verbatim',()=>{
  const html=officerMarkup(data);
  assert.match(html,/<p>Reference was declined\.<\/p>/);
  assert.doesNotMatch(html,/<p>2\. Reference was declined\.<\/p>/);
  assert.match(html,/<blockquote>2\. Reference was declined\.<\/blockquote>/);
  assert.match(html,/<p>The LAC shall forward/);
  assert.match(html,/<blockquote>2\. The LAC shall forward the reference preferably within four weeks\.<\/blockquote>/);
  assert.equal(presentation.officerText('25.09.2026'),'25.09.2026');
  assert.equal(presentation.officerText('A. Gupta appeared.'),'A. Gupta appeared.');
});
test('review warning uses officer wording without altering verified evidence',()=>{
  assert.match(officerMarkup(data),/Some orders still need verification\. Actions shown below are based only on verified evidence\./);
  assert.doesNotMatch(officerMarkup(data),/Some source orders need checking|Do not rely on an older summary/);
});
