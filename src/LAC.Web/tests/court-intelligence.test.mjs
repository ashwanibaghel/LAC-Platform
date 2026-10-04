import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync } from 'node:fs';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import ts from 'typescript';
const source=readFileSync(new URL('../src/court/CourtIntelligence.tsx',import.meta.url),'utf8');
test('conditional directions have a separate non-urgent evidence block',()=>{
  assert.match(source,/Conditional Court directions/);
  assert.match(source,/not unconditional tasks or payment deadlines/);
  assert.match(source,/entry\.conditionText/);
  assert.match(source,/Evidence source=\{entry\.source\}/);
  assert.doesNotMatch(source,/beforeNextHearing\.push|conditionalDirections.*isActionOverdue/);
});
const js=ts.transpileModule(source.replace('import "./court-intelligence.css";',''),{compilerOptions:{module:ts.ModuleKind.ESNext,jsx:ts.JsxEmit.React,target:ts.ScriptTarget.ES2022}}).outputText;
// Resolve React without relying on package resolution from a data URL.
const evaluated=js.replace(/import React,.*?from "react";/,'const { useEffect, useState } = React;').replace('export const CourtIntelligence','const CourtIntelligence');
const component=new Function('React',evaluated+'; return CourtIntelligence;')(React);

test('loading a multi-order case opens its history without any extra action request',async()=>{
  const previousFetch=globalThis.fetch;
  try {
    for(const count of [6,1]){
      let index=0; const effects=[];
      const changes=[],requests=[];
      const hooks={...React,useRef:value=>({current:value}),useEffect:fn=>{effects.push(fn);},useState:value=>{const slot=index++;return [value,next=>changes.push([slot,next])];}};
      const loaded=new Function('React',evaluated+'; return CourtIntelligence;')(hooks);
      globalThis.fetch=async(url,options)=>{requests.push({url,options});return {status:200,ok:true,json:async()=>({caseId:'case-'+count,currentPosition:[],beforeNextHearing:[],orders:Array.from({length:count},()=>({}))})};};
      loaded({caseId:'case-'+count});effects[0]();
      await new Promise(resolve=>setImmediate(resolve));
      assert.equal(changes.filter(([slot])=>slot===6).at(-1)[1],count>1);
      assert.equal(requests.length,1);
      assert.equal(requests[0].url,`/api/court-cases/case-${count}/intelligence`);
      assert.equal(requests[0].options.method,undefined);
    }
  } finally {globalThis.fetch=previousFetch;}
});

test('Court intelligence renders calmly with AI off and makes no render-time action',()=>{
  const html=renderToStaticMarkup(React.createElement(component,{caseId:'case'}));
  assert.match(html,/Court Intelligence/);
  assert.match(html,/Evidence-backed office brief/);
  assert.match(html,/Court records and official order links remain unchanged/);
  assert.doesNotMatch(html,/tokens|RAM|latency|confidence|raw JSON|Qwen|llama/i);
});
test('officer headings and evidence are present without developer metrics',()=>{
  for(const text of ['Current position','Before next hearing','Latest order','Complete order history','View timeline','Ask Court Intelligence','View evidence','What happened in this order'])assert.ok(source.includes(text));
  assert.doesNotMatch(source,/modelVersion|inferenceTime|confidencePercent|JSON\.stringify\(data/);
  assert.match(source,/source\.orderDate/);assert.match(source,/source\.page/);assert.match(source,/source\.evidence/);
});
test('questions require explicit submit and AI failure leaves intelligence available',()=>{
  assert.match(source,/onSubmit=\{ask\}/);
  assert.match(source,/Question answering is temporarily unavailable\. Case intelligence remains available/);
  assert.match(source,/activeCase\.current === requestedCase/);
  assert.doesNotMatch(source.slice(source.indexOf('  const ask = async'),source.indexOf('  const refresh = async')),/\/resume|captcha|dhc-assisted\/runs|sync-now|review-decision/);
});
test('review-marked orders display only independently usable summary facts',()=>{
  assert.match(source,/order\.summaryFacts \?\? \(order\.status === "Validated" \? order\.facts : \[\]\)/);
  assert.match(source,/order\.digest \?\? safeOrderFacts\(order\)/);
});
const presentation=new Function('React',evaluated+'; return {officerText, actionPeriod, confirmedNextHearing};')(React);
test('past source-listed hearing is not presented as an upcoming hearing',()=>{
  const dates=new Function('React',evaluated+'; return {upcomingHearing};')(React);
  const order={status:'Validated',facts:[{category:'COURT_DIRECTION',field:'nextHearing',scope:'Current'}],nextHearingDate:'2026-10-01'};
  assert.equal(false,dates.upcomingHearing(order,'2026-10-02'));
  assert.equal(true,dates.upcomingHearing(order,'2026-09-30'));
  assert.match(source,/Last source-listed date/);
  assert.match(source,/subsequent order or new hearing date is not established/);
});
const evidence={orderDate:'2025-01-30',page:6,evidence:'2. The LAC shall forward the reference preferably within four weeks.',officialUrl:'https://delhihighcourt.nic.in/app/showlogo/test.pdf/2025'};
const direction={category:'COURT_DIRECTION',field:'direction',scope:'Current',value:evidence.evidence,page:6,evidence:evidence.evidence};
const data={status:'NeedsReview',processingComplete:true,currentPosition:[{text:'2. Reference was declined.',source:{...evidence,evidence:'2. Reference was declined.'}}],beforeNextHearing:[{id:'a',text:direction.value,actor:'LAC',deadlineText:'preferably within four weeks',dueDate:'2025-02-27',source:evidence}],latestOrder:{orderDate:'2025-01-30',officialUrl:evidence.officialUrl,status:'NeedsReview',facts:[direction],summaryFacts:[direction]},orders:[]};
test('final outcome preserves history, caption evidence, factual date separation and source gaps',()=>{
  const html=officerMarkup({...data,finalOrder:data.latestOrder,orders:[data.latestOrder],caption:{title:'A vs GNCTD',respondentAdvocates:{text:'Mr. A, Advocate for LAC',source:evidence}},factualChronology:[{dates:['2012-08-01'],text:'Compensation was received.',role:'COMPENSATION_FACT',source:evidence}],sourceCoverage:{knownSources:2,checkedSources:1,gaps:[{orderDate:'2024-01-01',officialUrl:evidence.officialUrl,reason:'Unavailable source'}]}});
  assert.match(html,/Final judicial outcome/);
  assert.match(html,/Mr\. A, Advocate for LAC/);
  assert.match(html,/Factual dates mentioned in orders · not Court hearing dates/);
  assert.match(html,/Chronology gaps \(1\)/);
  assert.match(html,/not a claim that every Court order is available/);
});
test('multi-page evidence displays original fragments and both page numbers',()=>{
  const src={...evidence,evidenceParts:[{page:6,evidence:'The Respondents shall file details of'},{page:7,evidence:'the compensation deposited.'}]};
  const html=officerMarkup({...data,currentPosition:[{text:'The Respondents shall file details of the compensation deposited.',source:src}]});
  assert.match(html,/Page 6/); assert.match(html,/Page 7/);
  assert.match(html,/<blockquote>The Respondents shall file details of<\/blockquote>/);
});
function officerMarkup(artifact){
  let stateIndex=0;
  const passiveReact={...React,useEffect:()=>{},useRef:value=>({current:value}),useState:initial=>[stateIndex++===0?{...artifact,caseId:'case'}:initial,()=>{}]};
  const populated=new Function('React',evaluated+'; return CourtIntelligence;')(passiveReact);
  return renderToStaticMarkup(React.createElement(populated,{caseId:'case'}));
}
test('routine timeline remains compact without deleting substantive history or continuity warnings',()=>{
  const older={...data.latestOrder,orderDate:'2015-10-05',presentationKind:'Substantive'};
  const latest={...data.latestOrder,orderDate:'2026-05-21',presentationKind:'Routine'};
  const html=officerMarkup({...data,latestOrder:latest,latestMeaningfulOrder:older,orders:[older,latest],chronologyWarnings:['Intervening history is not established.']});
  assert.match(html,/supplied order history has a continuity gap/);
  assert.match(html,/2015/); assert.match(html,/2026/);
  assert.match(source,/order\.presentationKind === "Routine" \? 1 : 2/);
  assert.match(source,/answer\.reason === "OrderUnavailable"/);
  assert.match(source,/I could not find an official order for that listed date\./);
});
test('no confirmed hearing uses outstanding action, not next-hearing language',()=>{
  const html=officerMarkup(data);
  assert.match(html,/<span>Outstanding LAC action<\/span>/);
  assert.doesNotMatch(html,/<span>Before next hearing<\/span>/);
  assert.match(html,/What is pending from LAC\?/);
  assert.equal(false,presentation.confirmedNextHearing({...data.latestOrder,nextHearingDate:'2026-10-05'}));
});
test('source-confirmed next hearing enables the before-next-hearing heading',()=>{
  const latest={...data.latestOrder,nextHearingDate:'2026-10-05',summaryFacts:[direction,{...direction,field:'nextHearing',value:'Renotify on 05.10.2026.'}]};
  assert.match(officerMarkup({...data,latestOrder:latest}),/<span>Before next hearing<\/span>/);
  assert.equal(false,presentation.confirmedNextHearing({...latest,summaryFacts:[]}));
});
test('a party-requested date cannot enable a confirmed next hearing',()=>{
  const latest={...data.latestOrder,nextHearingDate:'2026-10-05',summaryFacts:[{...direction,category:'PETITIONER_SUBMISSION',field:'nextHearing',value:'The petitioner seeks listing on 05.10.2026.'}]};
  assert.equal(false,presentation.confirmedNextHearing(latest));
  assert.doesNotMatch(officerMarkup({...data,latestOrder:latest}),/<span>Before next hearing<\/span>/);
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
  assert.match(officerMarkup({...data,orders:[data.latestOrder]}),/Some source material still needs verification\./);
  assert.match(officerMarkup({...data,orders:[data.latestOrder]}),/Actions and summaries shown here use only verified evidence/);
  assert.doesNotMatch(officerMarkup(data),/Some source orders need checking|Do not rely on an older summary/);
});
test('zero-action order still renders rich attributed facts, not an empty action detector',()=>{
  const facts=[{...direction,category:'PETITIONER_SUBMISSION',field:'compensation',value:'Petitioner claims compensation remains unpaid.'},{...direction,category:'LAC_OR_RESPONDENT_SUBMISSION',field:'compensation',value:'LAC submits compensation was deposited.'},{...direction,category:'COURT_OBSERVATION',field:'observation',value:'The Court observed that the record was incomplete.'}];
  const latest={...data.latestOrder,status:'Validated',summaryFacts:facts,officeActionCount:0,coverage:{allSelectedChunksProcessed:true}};
  const html=officerMarkup({...data,beforeNextHearing:[],latestOrder:latest,currentPosition:[],orders:[latest]});
  assert.match(html,/What happened in this order/);
  assert.match(html,/Petitioner submission/);
  assert.match(html,/LAC\/respondent submission/);
  assert.match(html,/Court observation/);
  assert.match(html,/No direct LAC action was identified in this order/);
  assert.match(html,/<h4>Office action check<\/h4>/);
  assert.doesNotMatch(html,/<span>Outstanding LAC action<\/span>/);
  assert.doesNotMatch(html,/No useful intelligence|No confirmed operative summary/);
  assert.match(html,/court-intelligence-timeline-fact/);
  assert.doesNotMatch(html,/<details class="court-intelligence-history" open/);
});

test('conditional permission is visible separately and preserves adjacent source pages',()=>{
  const permission={text:'After examining the documents, the LAC may release compensation.',actor:'LAC',
    modality:'may release',conditionText:'After examining the documents',scope:'Current',completion:'Not established',
    source:{...evidence,evidenceParts:[{page:9,evidence:'If required, the LAC to consider the reference'},
      {page:10,evidence:'and examine the documents.'}]}};
  const html=officerMarkup({...data,beforeNextHearing:[],conditionalDirections:[permission]});
  assert.match(html,/Conditional Court directions/);
  assert.match(html,/may release/); assert.match(html,/After examining the documents/);
  assert.match(html,/Page 9/); assert.match(html,/Page 10/);
  assert.doesNotMatch(html,/Preferred period ended|Overdue/);
});

test('incomplete or review-required processing never claims there is no direct LAC action',()=>{
  for(const change of [
    {processingComplete:false},
    {orders:[{...data.latestOrder,status:'NeedsReview',facts:[],failureMessage:'Local processing failed'}]},
    {orders:[{...data.latestOrder,status:'NeedsSourceReview',facts:[]}]},
    {orders:[{...data.latestOrder,status:'Validated',facts:[],coverage:{allSelectedChunksProcessed:false}}]},
    {orders:[{...data.latestOrder,status:'Validated',facts:[],refreshFailure:'Latest check failed'}]},
  ]){
    const html=officerMarkup({...data,beforeNextHearing:[],orders:[{...data.latestOrder,status:'Validated'}],...change});
    assert.match(html,/Office action check is incomplete/);
    assert.doesNotMatch(html,/No direct LAC action was identified in the processed orders/);
  }
});

test('complete verified zero-action evidence retains the genuine no-action outcome',()=>{
  const latest={...data.latestOrder,status:'Validated',facts:[],summaryFacts:[],officeActionCount:0,coverage:{allSelectedChunksProcessed:true}};
  const html=officerMarkup({...data,status:'Validated',processingComplete:true,beforeNextHearing:[],latestOrder:latest,orders:[latest]});
  assert.match(html,/No direct LAC action was identified in the processed orders/);
  assert.doesNotMatch(html,/Office action check is incomplete/);
});
test('current position retains role distinctions and review warning appears only once',()=>{
  const html=officerMarkup({...data,orders:[data.latestOrder],currentPosition:[{role:'COURT_FINDING',text:'The reference was in time.',source:evidence},{role:'PETITIONER_SUBMISSION',text:'The petitioner disputes payment.',source:evidence}]});
  assert.match(html,/Court finding/);assert.match(html,/Petitioner submission/);
  assert.equal((html.match(/Some source material still needs verification/g)||[]).length,1);
  assert.match(html,/English, हिन्दी or Hinglish/);
});
