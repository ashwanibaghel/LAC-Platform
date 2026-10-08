import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import ts from 'typescript';

const source=fs.readFileSync(new URL('../src/court/CourtIntelligence.tsx',import.meta.url),'utf8');
const compiled=ts.transpileModule(source.replace('import "./court-intelligence.css";',''),{compilerOptions:{module:ts.ModuleKind.ESNext,jsx:ts.JsxEmit.React,target:ts.ScriptTarget.ES2022}}).outputText
  .replace(/import React,.*?from "react";/,'const {useEffect,useState}=React;').replace('export const CourtIntelligence','const CourtIntelligence');
const {OrderScope,scopeNotice,CaseLandLacContext}=new Function('React',compiled+';return {OrderScope,scopeNotice,CaseLandLacContext};')(React);
const fixture=JSON.parse(fs.readFileSync(new URL('./fixtures/court-structured-scope.json',import.meta.url),'utf8'));
const markup=order=>renderToStaticMarkup(React.createElement(OrderScope,{order}));
const contextMarkup=(order,orders=[order],section='awards',fact=order.lacOrderScope.awards[0])=>renderToStaticMarkup(React.createElement(CaseLandLacContext,{
  orders,context:{[section]:[{officialUrl:order.officialUrl,orderDate:order.orderDate,fact,evidence:[]}]}
}));

test('case context renders explicit facts with original evidence and attribution',()=>{
  const html=contextMarkup(fixture.structured);
  assert.match(html,/63\/86-87/);assert.match(html,/Court finding/);assert.match(html,/Source · p\. 1/);
  assert.ok(html.includes(`${fixture.structured.officialUrl}#page=1`));
});
test('case context renders NotStated only with complete source coverage',()=>{
  assert.match(contextMarkup(fixture.structured),/Not stated in this order\./);
});
test('case context renders review wording for incomplete extraction',()=>{
  const order=structuredClone(fixture.structured);order.lacOrderScope.extraction.fullRelevantTextChecked=false;
  const html=contextMarkup(order);assert.match(html,/Needs review — extraction incomplete\./);assert.doesNotMatch(html,/Not stated in this order\./);
});
test('case context handles missing source scopes and coverage metadata conservatively',()=>{
  const row=fixture.structured.lacOrderScope.awards[0];
  const cases=[[],[{...fixture.structured,lacOrderScope:undefined}]];
  for(const change of [scope=>delete scope.extraction,scope=>delete scope.extraction.fullRelevantTextChecked,scope=>delete scope.missingState]){
    const order=structuredClone(fixture.structured);change(order.lacOrderScope);cases.push([order]);
  }
  for(const orders of cases){
    const html=contextMarkup(fixture.structured,orders,'awards',row);
    assert.match(html,/Needs review — source coverage unavailable\./);assert.doesNotMatch(html,/Not stated in this order\./);
  }
});
test('case context matches both official URL and order date, including same-URL orders',()=>{
  const wrongDate=structuredClone(fixture.structured);wrongDate.orderDate='2026-09-01';wrongDate.lacOrderScope.extraction.fullRelevantTextChecked=false;
  const wrongUrl=structuredClone(fixture.structured);wrongUrl.officialUrl='https://delhihighcourt.nic.in/another-order.pdf';wrongUrl.lacOrderScope.extraction.fullRelevantTextChecked=false;
  assert.match(contextMarkup(fixture.structured,[wrongDate,wrongUrl,fixture.structured]),/Not stated in this order\./);
  const html=contextMarkup(fixture.structured,[wrongDate,wrongUrl]);
  assert.match(html,/Needs review — source coverage unavailable\./);assert.doesNotMatch(html,/Not stated in this order\./);
});
test('case context resolves entity references through the complete original scope',()=>{
  const html=contextMarkup(fixture.structured,undefined,'parcels',fixture.structured.lacOrderScope.parcels[0]);
  assert.match(html,/Village Refs: Pochanpur/);assert.match(html,/Award Refs: 63\/86-87/);
  assert.match(html,/29\/\/8\/4/);assert.match(html,/Source · p\. 1/);
});
test('supporting context leaves selected order scope and source data unchanged',()=>{
  const orders=structuredClone([fixture.structured]);const original=structuredClone(orders);
  const before=markup(orders[0]);contextMarkup(orders[0],orders);
  assert.equal(markup(orders[0]),before);assert.deepEqual(orders,original);
});
test('unknown LAC retains exact office review wording',()=>{
  const scope=structuredClone(fixture.structured.lacOrderScope);scope.lacAuthorityScope='UnknownLAC';scope.lacActionable=false;
  assert.equal(scopeNotice(scope),'LAC authority needs review. No action is established for this office.');
});
test('legacy scope does not assert unstated facts',()=>{const html=markup({});assert.match(html,/Structured order scope not yet extracted\./);assert.doesNotMatch(html,/Not stated/);});
test('complete no-LAC order uses exact office wording',()=>assert.equal(scopeNotice(fixture.nonlac),'No LAC-specific issue or direction identified in this order.'));
test('caption LAC is relevant without substantive directions',()=>assert.equal(scopeNotice(fixture.caption),'LAC is identified in this order, but no LAC-specific direction is established.'));
test('selected order preserves raw identifiers, collective area, roles and evidence',()=>{
  const html=markup(fixture.structured);for(const value of ['29//8/4','29//18/4','Pochanpur','63/86-87','Collective areas','3','bighas','Source · p. 1','Court finding','What Our LAC Needs To Do'])assert.ok(html.includes(value),value);
  assert.equal(fixture.structured.lacOrderScope.parcels[0].area.rawText,null);
});
test('canonical award date stays in office record, without filling court date',()=>{
  const html=markup(fixture.structured);assert.match(html,/Not stated in this order\./);assert.match(html,/Office Record Says:.*1986-09-19/);
  assert.equal(fixture.structured.lacOrderScope.awards[0].date.value,null);
});
test('unresolved source coverage never renders NotStated',()=>{
  const order=structuredClone(fixture.structured);order.lacOrderScope.extraction.fullRelevantTextChecked=false;order.lacOrderScope.missingState='NeedsReview';
  const html=markup(order);assert.doesNotMatch(html,/Not stated in this order/);assert.match(html,/Extraction incomplete/);
});
test('other LAC has grounded direction but no action for this office',()=>{
  const order=structuredClone(fixture.structured);const scope=order.lacOrderScope;scope.lacAuthorityScope='OtherLAC';scope.lacActionable=false;scope.directions.forEach(d=>d.lacActionable=false);
  assert.match(scopeNotice(scope),/another LAC/);assert.match(markup(order),/No operative direction to this office/);
});
test('review submission requires decision, reason, expected version and explicit action',()=>{
  const html=renderToStaticMarkup(React.createElement(OrderScope,{order:fixture.structured,canReview:true,onReview:async()=>{}}));
  assert.match(html,/<input(?=[^>]*name="reason")(?=[^>]*required)/);assert.match(html,/name="decision"/);assert.match(source,/expectedVersion: link.version/);assert.match(source,/method: link \? "PUT" : "POST"/);
});
