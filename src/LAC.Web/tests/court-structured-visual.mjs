import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';

const fixtures=JSON.parse(fs.readFileSync(new URL('./fixtures/court-structured-scope.json',import.meta.url),'utf8'));
const out=path.resolve('../../docs/receipts/court-structured-lac');fs.mkdirSync(out,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe'});
const context=await browser.newContext({viewport:{width:1366,height:768}});const page=await context.newPage();
const requests=[],errors=[];page.on('pageerror',error=>errors.push(error.message));
const structured=fixtures.structured;
const oldOrder={...structured,orderDate:'2026-09-01',officialUrl:structured.officialUrl.replace('synthetic-order','synthetic-caption'),lacOrderScope:fixtures.caption,recordLinks:[]};
oldOrder.lacOrderScope.source={...oldOrder.lacOrderScope.source,orderDate:oldOrder.orderDate,officialUrl:oldOrder.officialUrl};
let mode='structured';
await page.route('**/api/**',async route=>{
  const request=route.request();requests.push({method:request.method(),url:request.url()});
  const id=new URL(request.url()).pathname.split('/')[3];
  let order=structured;
  if(mode==='nonlac')order={...structured,lacOrderScope:fixtures.nonlac,recordLinks:[]};
  if(mode==='legacy'){order={...structured};delete order.lacOrderScope;delete order.recordLinks;}
  const orders=mode==='structured' ? [oldOrder,order] : [order];
  const direction=order.lacOrderScope?.directions.find(item=>item.lacActionable===true);
  const beforeNextHearing=direction ? [{id:direction.id,text:direction.action.value,actor:direction.directedTo.value,deadlineText:direction.deadline.value,dueDate:'2026-11-04',source:{orderDate:order.orderDate,officialUrl:order.officialUrl,page:1,evidence:direction.action.rawText}}] : [];
  await route.fulfill({json:{version:1,caseId:id,caseNumber:'SYNTHETIC COURT SCOPE REVIEW FIXTURE',status:'Validated',processingComplete:true,currentPosition:[],beforeNextHearing,latestOrder:order,orders,knownOrderCount:orders.length,canReviewOrderLinks:true,actionStatus:'VerifiedEvidenceAvailable'}});
});
try{
  await page.goto('http://127.0.0.1:5190/court-intelligence-demo.html');
  await page.locator('.court-selected-order').waitFor();
  await page.locator('body').evaluate(body=>{const note=document.createElement('div');note.textContent='SYNTHETIC TEST FIXTURE · 1366 × 768 · No live office data';note.style='position:fixed;right:12px;top:8px;font:11px system-ui;color:#526e88;background:#f3f6fa;padding:6px;z-index:9999';body.appendChild(note);});
  assert.equal(await page.locator('#court-order-scope-select').inputValue(),structured.officialUrl);
  assert.match(await page.locator('.court-card-attention').innerText(),/Outstanding LAC action/i);
  await page.screenshot({path:path.join(out,'01-selected-order-1366x768.png')});
  await page.locator('.court-selected-land-facts .court-scope-links').evaluate(element=>element.open=true);
  await page.locator('.court-selected-land-facts .court-scope-links').scrollIntoViewIfNeeded();
  await page.screenshot({path:path.join(out,'02-office-record-review-1366x768.png')});
  await page.locator('#court-order-scope-select').selectOption(oldOrder.officialUrl);
  await page.locator('.court-selected-order').scrollIntoViewIfNeeded();
  assert.ok(await page.locator('.court-selected-order').innerText().then(text=>text.includes('LAC is identified in this order, but no LAC-specific direction is established.')));
  await page.screenshot({path:path.join(out,'03-caption-only-1366x768.png')});
  mode='nonlac';await page.reload();await page.locator('.court-selected-order').waitFor();
  assert.ok(await page.locator('.court-selected-order').innerText().then(text=>text.includes('No LAC-specific issue or direction identified in this order.')));
  await page.screenshot({path:path.join(out,'04-no-lac-1366x768.png')});
  mode='legacy';await page.reload();await page.locator('.court-selected-order').waitFor();
  assert.ok(await page.locator('.court-selected-order').innerText().then(text=>text.includes('Structured order scope not yet extracted.')));
  await page.screenshot({path:path.join(out,'05-legacy-1366x768.png')});
  assert.deepEqual(errors,[]);assert.ok(requests.every(request=>request.method==='GET'));
  fs.writeFileSync(path.join(out,'visual-checks.json'),JSON.stringify({viewport:{width:1366,height:768},fixture:'synthetic',checksPassed:5,pageErrors:errors,renderTimeMutations:requests.filter(r=>r.method!=='GET').length},null,2));
  console.log('5 browser visual checks passed; no page errors or render-time mutations.');
} finally {await context.close();await browser.close();}
