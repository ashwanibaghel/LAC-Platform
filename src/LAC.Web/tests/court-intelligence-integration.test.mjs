import assert from 'node:assert/strict';
import test from 'node:test';
import {readFileSync} from 'node:fs';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import ts from 'typescript';
const source=readFileSync(new URL('../src/court/CourtIntelligence.tsx',import.meta.url),'utf8');
const js=ts.transpileModule(source.replace('import "./court-intelligence.css";',''),{compilerOptions:{module:ts.ModuleKind.ESNext,jsx:ts.JsxEmit.React,target:ts.ScriptTarget.ES2022}}).outputText;
const evaluated=js.replace(/import React,.*?from "react";/,'const {useEffect,useState}=React;').replace('export const CourtIntelligence','const CourtIntelligence');
const A='71000000-0000-4000-8000-000000000001',B='71000000-0000-4000-8000-000000000002';
const payload=id=>({version:1,caseId:id,status:'Unprocessed',processingComplete:false,currentPosition:[],beforeNextHearing:[],latestOrder:null,orders:[],knownOrderCount:0});
const response=value=>({ok:true,status:200,json:async()=>value});
const tick=()=>new Promise(resolve=>setImmediate(resolve));
function deferred(){let resolve;return {promise:new Promise(r=>resolve=r),resolve:value=>resolve(value)};}
function harness(){
  let stateIndex=0,refIndex=0,effectIndex=0;
  const states=[],refs=[],effects=[],pending=[];
  const hooks={...React,useState:initial=>{const i=stateIndex++;if(!(i in states))states[i]=initial;return [states[i],value=>states[i]=typeof value==='function'?value(states[i]):value];},useRef:initial=>{const i=refIndex++;return refs[i]??=( {current:initial});},useEffect:(fn,deps)=>{const i=effectIndex++;if(!effects[i]||deps.some((v,j)=>v!==effects[i].deps[j]))pending.push(()=>{effects[i]?.cleanup?.();effects[i]={deps,cleanup:fn()};});}};
  const component=new Function('React',evaluated+';return CourtIntelligence;')(hooks);
  return {render:id=>{stateIndex=refIndex=effectIndex=0;const tree=component({caseId:id});while(pending.length)pending.shift()();return tree;},close:()=>effects.forEach(e=>e?.cleanup?.())};
}
function find(tree,predicate){if(!tree||typeof tree!=='object')return null;if(predicate(tree))return tree;for(const child of React.Children.toArray(tree.props?.children)){const match=find(child,predicate);if(match)return match;}return null;}

test('real workspace routing uses actual case ID; no demo production dependency',()=>{
  const workspace=readFileSync(new URL('../src/court/CourtCaseWorkspace.tsx',import.meta.url),'utf8');
  assert.match(workspace,/<CourtIntelligence key=\{courtCase.id\} caseId=\{courtCase.id\}/);
  for(const file of ['../src/App.tsx','../src/court/CourtCaseWorkspace.tsx','../src/court/CourtIntelligence.tsx']){
    assert.doesNotMatch(readFileSync(new URL(file,import.meta.url),'utf8'),/a1000000|CourtIntelligenceDemo|court-intelligence-demo\.html|demo-jobs\.json/);
  }
});
test('slow A response never overwrites B and render triggers only case-scoped GET',async()=>{
  const previous=globalThis.fetch,requests=[],a=deferred(),b=deferred(),h=harness();
  try{
    globalThis.fetch=(url,options)=>{requests.push({url,options});return url.includes(A)?a.promise:b.promise;};
    h.render(A);h.render(B);b.resolve(response(payload(B)));await tick();
    a.resolve(response({...payload(A),currentPosition:[{text:'A-only confidential fixture'}]}));await tick();
    const html=renderToStaticMarkup(h.render(B));
    assert.doesNotMatch(html,/A-only/);assert.match(html,/DHC history has not been checked yet/);
    assert.equal(requests.length,2);assert.ok(requests.every(r=>!r.options.method));
    assert.equal(requests[0].url,`/api/court-cases/${A}/intelligence`);
    assert.equal(requests[1].url,`/api/court-cases/${B}/intelligence`);
  }finally{h.close();globalThis.fetch=previous;}
});
test('wrong-case or malformed GET payload fails closed without crashing workspace',async()=>{
  const previous=globalThis.fetch;
  try{for(const bad of [payload(A),{caseId:B,orders:'bad'},null]){
    const h=harness();globalThis.fetch=async()=>response(bad);h.render(B);await tick();
    assert.match(renderToStaticMarkup(h.render(B)),/Court intelligence is temporarily unavailable/);h.close();
  }}finally{globalThis.fetch=previous;}
});
test('Ask uses B actual ID and rejects a response carrying A identity',async()=>{
  const previous=globalThis.fetch,requests=[],h=harness();
  try{
    globalThis.fetch=async(url,options)=>{requests.push({url,options});return response(options.method?{caseId:A,claims:[],insufficientEvidence:true}:payload(B));};
    h.render(B);await tick();let tree=h.render(B);
    find(tree,n=>n.type==='input').props.onChange({target:{value:'What happened in this case?'}});
    tree=h.render(B);await find(tree,n=>n.type==='form').props.onSubmit({preventDefault(){}});
    assert.equal(requests[1].url,`/api/court-cases/${B}/intelligence/ask`);assert.equal(requests[1].options.method,'POST');
    assert.match(renderToStaticMarkup(h.render(B)),/Question answering is temporarily unavailable/);
  }finally{h.close();globalThis.fetch=previous;}
});
test('late A answer/finally cannot alter B, including A -> B -> A navigation',async()=>{
  const previous=globalThis.fetch,ask=deferred(),h=harness();
  try{
    globalThis.fetch=(url,options)=>options.method?ask.promise:Promise.resolve(response(payload(url.includes(A)?A:B)));
    h.render(A);await tick();let tree=h.render(A);
    find(tree,n=>n.type==='input').props.onChange({target:{value:'Latest direction?'}});
    tree=h.render(A);const pending=find(tree,n=>n.type==='form').props.onSubmit({preventDefault(){}});
    h.render(B);await tick();h.render(A);await tick();
    ask.resolve(response({caseId:A,claims:[{text:'OLD A ANSWER',source:{}}],insufficientEvidence:false}));await pending;
    assert.doesNotMatch(renderToStaticMarkup(h.render(A)),/OLD A ANSWER|Checking…/);
  }finally{h.close();globalThis.fetch=previous;}
});
test('unprocessed real sources have explicit processing action; no automatic POST',async()=>{
  const previous=globalThis.fetch,requests=[],h=harness();
  try{
    globalThis.fetch=async(url,options)=>{requests.push({url,options});return response({...payload(B),knownOrderCount:1,unprocessedOrderCount:1,orders:[{orderDate:'2026-01-01',officialUrl:'https://delhihighcourt.nic.in/app/showlogo/fixture.pdf/2026',status:'Unprocessed',facts:[]}]});};
    h.render(B);await tick();const html=renderToStaticMarkup(h.render(B));
    assert.match(html,/Intelligence not processed yet/);assert.match(html,/Process known orders/);
    assert.match(html,/Office action check is incomplete/);assert.equal(requests.length,1);assert.ok(!requests[0].options.method);
  }finally{h.close();globalThis.fetch=previous;}
});
test('official order links reject arbitrary paths, credentials, query, ports and other origins',()=>{
  const link=new Function('React',evaluated+';return officialLink;')(React);
  const good='https://delhihighcourt.nic.in/app/showlogo/order.pdf/2026';assert.equal(link(good),good);
  for(const bad of [good+'?token=x',good+'#fragment',good.replace('https://','https://user:pass@'),good.replace('.in/','.in:8097/'),good.replace('showlogo','anything'),good.replace('delhihighcourt.nic.in','example.com')])assert.equal(link(bad),null);
});

test('new discovery keeps polling across an older completed AI job after modal closure',async()=>{
  const previous=globalThis.fetch,oldInterval=globalThis.setInterval,oldClear=globalThis.clearInterval,h=harness();
  let poll,reads=0;
  try{
    globalThis.setInterval=fn=>{poll=fn;return 1;};globalThis.clearInterval=()=>{};
    globalThis.fetch=async()=>{reads++;return response({...payload(B),knownOrderCount:1,unprocessedOrderCount:1,
      orders:[{orderDate:'2026-01-01',officialUrl:'https://delhihighcourt.nic.in/app/showlogo/new.pdf',status:'Unprocessed',facts:[]}],
      historySync:{runId:'run',status:'Completed',phase:'OrderLookup',completedAt:'2026-10-04T00:10:00Z'},
      refreshState:{caseId:B,status:'Completed',startedAt:'2026-10-03T00:00:00Z'}});};
    h.render(B);await tick();h.render(B);assert.equal(typeof poll,'function');
    await poll();assert.equal(reads,2);
  }finally{h.close();globalThis.fetch=previous;globalThis.setInterval=oldInterval;globalThis.clearInterval=oldClear;}
});

test('date-review source keeps its official link visible without promoting an AI fact',async()=>{
  const previous=globalThis.fetch,h=harness();
  try{
    globalThis.fetch=async()=>response({...payload(B),knownOrderCount:1,unusableKnownOrderCount:1,
      sourceReviewOrders:[{orderDate:null,officialUrl:'https://delhihighcourt.nic.in/app/showlogo/date-review.pdf',reason:'Official order date needs verification before AI processing.'}]});
    h.render(B);await tick();const html=renderToStaticMarkup(h.render(B));
    assert.match(html,/Order date needs review/);assert.match(html,/Open official PDF/);
    assert.match(html,/Current position is not yet confirmed/);assert.doesNotMatch(html,/No direct LAC action was identified/);
  }finally{h.close();globalThis.fetch=previous;}
});

test('legacy brief displays the exact principal disposition while preserving the source artifact',async()=>{
  const previous=globalThis.fetch,h=harness(),url='https://delhihighcourt.nic.in/app/showlogo/disposition.pdf';
  const artifact={...payload(B),currentPosition:[{text:'Pending applications, if any, are also disposed of.',role:'DISPOSITION',scope:'Current',source:{orderDate:'2026-01-01',officialUrl:url,page:1,evidence:'Pending applications, if any, are also disposed of.'}}],
    latestOrder:{orderDate:'2026-01-01',officialUrl:url,status:'Validated',facts:[{category:'DISPOSITION',scope:'Current',value:'The present petition is disposed of in these terms.',page:1,evidence:'The present petition is disposed of in these terms.'}]}};
  try{
    const original=JSON.stringify(artifact);globalThis.fetch=async()=>response(artifact);
    h.render(B);await tick();const tree=h.render(B);
    const position=find(tree,n=>n.type==='article'&&find(n,x=>x.type==='h4'&&x.props.children==='Current position'));
    const html=renderToStaticMarkup(position);
    assert.match(html,/The present petition is disposed/);assert.doesNotMatch(html,/Pending applications/);
    assert.match(html,/View evidence/);assert.equal(JSON.stringify(artifact),original);
  }finally{h.close();globalThis.fetch=previous;}
});

test('full-history action opens only the current case wizard without starting unrelated work',async()=>{
  const previous=globalThis.fetch,requests=[],h=harness();
  try{
    globalThis.fetch=async(url,options)=>{requests.push({url,options});return response(options.method ? {caseId:B,runId:'81000000-0000-4000-8000-000000000001'} : payload(B));};
    h.render(B);await tick();let tree=h.render(B);
    await find(tree,n=>n.type==='button'&&n.props.children==='Sync Full DHC History').props.onClick();
    tree=h.render(B);const wizard=find(tree,n=>typeof n.type==='function'&&n.type.name==='DhcHistoryWizard');
    assert.equal(wizard.props.caseId,B);assert.equal(requests.length,2);
    assert.equal(requests[1].url,`/api/court-cases/${B}/dhc-history`);
    assert.equal(requests[1].options.method,'POST');
  }finally{h.close();globalThis.fetch=previous;}
});

test('official and office status stay distinct and every history row exposes its PDF',async()=>{
  const previous=globalThis.fetch,h=harness();
  try{
    globalThis.fetch=async()=>response({...payload(B),officeStatus:'Pending',officialStatus:{rawStatus:'Disposed',observedAt:'2026-10-04T00:00:00Z',listingDate:null},knownOrderCount:2,
      orders:[{orderDate:'2026-01-01',officialUrl:'https://delhihighcourt.nic.in/app/showlogo/one.pdf',status:'Unprocessed',facts:[]},
        {orderDate:'2026-02-01',officialUrl:'https://delhihighcourt.nic.in/app/showlogo/two.pdf',status:'NeedsReview',facts:[]}]});
    h.render(B);await tick();const html=renderToStaticMarkup(h.render(B));
    assert.match(html,/Official DHC status/);assert.match(html,/Disposed/);assert.match(html,/Office register/);assert.match(html,/Pending/);
    assert.equal((html.match(/Open official PDF/g)||[]).length,2);
    assert.match(html,/Not Processed/);assert.match(html,/Needs Review/);
  }finally{h.close();globalThis.fetch=previous;}
});

test('empty states distinguish never synced from no official PDF without claiming failed orders',async()=>{
  const previous=globalThis.fetch;
  try{for(const checked of [false,true]){
    const h=harness();globalThis.fetch=async()=>response({...payload(B),...(checked?{historySync:{runId:'run',status:'Completed',phase:'OrderLookup'}}:{})});
    h.render(B);await tick();const html=renderToStaticMarkup(h.render(B));
    assert.match(html,checked?/No official order PDF was returned/:/DHC history has not been checked yet/);
    assert.doesNotMatch(html,/one or more Court orders could not be fully processed|0 of 0|No direct LAC action was identified/);h.close();
  }}finally{globalThis.fetch=previous;}
});

test('normal local replies render without Court citations and A chat is cleared on B',async()=>{
  const previous=globalThis.fetch,h=harness();
  try{
    globalThis.fetch=async(url,options)=>response(options.method?{caseId:A,mode:'GeneralLocal',answer:'Hello Ashwani!',claims:[],insufficientEvidence:false}:payload(url.includes(A)?A:B));
    h.render(A);await tick();let tree=h.render(A);
    find(tree,n=>n.type==='input').props.onChange({target:{value:'hello'}});tree=h.render(A);
    await find(tree,n=>n.type==='form').props.onSubmit({preventDefault(){}});
    assert.match(renderToStaticMarkup(h.render(A)),/Hello Ashwani!/);
    h.render(B);await tick();assert.doesNotMatch(renderToStaticMarkup(h.render(B)),/Hello Ashwani!/);
  }finally{h.close();globalThis.fetch=previous;}
});

test('completed reviewed extraction counts as AI processed while failed and pending sources do not',async()=>{
  const previous=globalThis.fetch,h=harness();
  try{
    const base={orderDate:'2026-01-01',officialUrl:'https://delhihighcourt.nic.in/app/showlogo/one.pdf',facts:[]};
    globalThis.fetch=async()=>response({...payload(B),knownOrderCount:3,
      orders:[{...base,status:'NeedsReview',coverage:{allSelectedChunksProcessed:true}},
        {...base,orderDate:'2026-02-01',officialUrl:base.officialUrl.replace('one','two'),status:'NeedsReview',coverage:{allSelectedChunksProcessed:false},failureMessage:'Failed chunk'},
        {...base,orderDate:'2026-03-01',officialUrl:base.officialUrl.replace('one','three'),status:'Unprocessed'}],
      sourceCoverage:{basis:'Official index',knownSources:3,checkedSources:0,gaps:[]}});
    h.render(B);await tick();const html=renderToStaticMarkup(h.render(B));
    assert.match(html,/<dt>AI processed<\/dt><dd>1<\/dd>/);
    assert.match(html,/<dt>Needs Review<\/dt><dd>2<\/dd>/);
    assert.match(html,/1 of 3 official orders AI processed/);
    assert.match(html,/1 awaiting AI processing/);
    assert.match(html,/Processed orders can still need review/);
  }finally{h.close();globalThis.fetch=previous;}
});
test('explicit processing posts only the current real case and unavailable refresh leaves its brief readable',async()=>{
  const previous=globalThis.fetch,requests=[],h=harness();
  try{
    globalThis.fetch=async(url,options)=>{requests.push({url,options});return options.method?{ok:false,status:503}:response({...payload(B),knownOrderCount:1,unprocessedOrderCount:1,orders:[{orderDate:'2026-01-01',officialUrl:'https://delhihighcourt.nic.in/app/showlogo/fixture.pdf/2026',status:'Unprocessed',facts:[]}]});};
    h.render(B);await tick();let tree=h.render(B);
    const button=find(tree,n=>n.type==='button'&&n.props.children==='Process known orders');assert.ok(button);
    await button.props.onClick();tree=h.render(B);
    assert.equal(requests.length,2);assert.equal(requests[1].url,`/api/court-cases/${B}/intelligence/refresh`);assert.equal(requests[1].options.method,'POST');
    const html=renderToStaticMarkup(tree);assert.match(html,/temporarily unavailable or busy/);assert.match(html,/Court Intelligence/);assert.match(html,/Complete order history/);
  }finally{h.close();globalThis.fetch=previous;}
});
