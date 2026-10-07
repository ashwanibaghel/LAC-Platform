import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import ts from 'typescript';

const source=fs.readFileSync(new URL('../src/court/CourtIntelligence.tsx',import.meta.url),'utf8');
const compiled=ts.transpileModule(source.replace('import "./court-intelligence.css";',''),{compilerOptions:{module:ts.ModuleKind.ESNext,jsx:ts.JsxEmit.React,target:ts.ScriptTarget.ES2022}}).outputText
  .replace(/import React,.*?from "react";/,'const {useEffect,useState}=React;').replace('export const CourtIntelligence','const CourtIntelligence');
const {OrderScope,scopeNotice}=new Function('React',compiled+';return {OrderScope,scopeNotice};')(React);
const fixture=JSON.parse(fs.readFileSync(new URL('./fixtures/court-structured-scope.json',import.meta.url),'utf8'));
const markup=order=>renderToStaticMarkup(React.createElement(OrderScope,{order}));
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
