import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';

const out = path.resolve('test-results/award-workbench');
fs.mkdirSync(out, {recursive:true});
const browser = await chromium.launch({headless:true, executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe'});
const user = {id:'00000000-0000-0000-0000-000000000001',username:'reviewer',displayName:'Review Officer',roles:['Admin'],permissions:[{code:'Award.View',scope:'All'},{code:'Award.Edit',scope:'All'}],workstreams:[],desks:[]};
const guid = n => `00000000-0000-0000-0000-${String(n).padStart(12,'0')}`;
const box = x => ({x,y:10,width:65,height:22});
function candidate(i, state='attention') {
  const number = i===1?'2//21':`${Math.floor(i/30)+2}//${i%30+1}`;
  const cells = {
    khasra:{rawOcr:number,pageAssignedOcr:number,cellCropOcr:state==='conflict'?'2//27':number,normalizedSuggestion:number,recognitionAgreement:state==='conflict'?'OcrDisagreement':'StrongAgreement',sourceRegion:box(20)},
    recordedArea:{rawOcr:'4-16',pageAssignedOcr:'4-16',cellCropOcr:'4-16',normalizedSuggestion:'4-16',sourceRegion:box(120)},
    awardedArea:{rawOcr:state==='unreadable'?'':'4-10',pageAssignedOcr:state==='unreadable'?'':'4-10',cellCropOcr:state==='unreadable'?'':'4-10',normalizedSuggestion:state==='unreadable'?null:'4-10',sourceRegion:box(220)}
  };
  return {id:guid(i+100),candidateType:'AwardKhasra',sequence:i,status:state==='conflict'?'Conflict':state==='unreadable'?'Invalid':state==='exact'||state==='verified'?'Ready':'NeedsReview',payloadJson:JSON.stringify({khasraNumber:number,recordedAreaBigha:4,recordedAreaBiswa:16,awardedAreaBigha:4,awardedAreaBiswa:10,qualifier:null}),sourceLocatorJson:JSON.stringify({page:43+Math.floor(i/12),structuredPayload:{sourceCells:cells}}),rawSourceText:number,sourcePage:43+Math.floor(i/12),safeToConfirm:state==='exact',validationIssuesJson:JSON.stringify(state==='conflict'?['OCR readings disagree.']:state==='unreadable'?['Awarded area is unreadable.']:[]),conflictDetailsJson:state==='conflict'?JSON.stringify({code:'RepeatedDifferentAreas'}):null,fieldReviewJson:'[]',verifiedAt:state==='verified'?'2026-09-26T10:00:00Z':null,verifiedBy:state==='verified'?'reviewer':null};
}
function pdf() {
  const content='BT /F1 16 Tf 60 720 Td (Synthetic Award source page) Tj 0 -42 Td (Khasra 2//21  Recorded 4-16  Awarded 4-10) Tj ET';
  const objects=['<< /Type /Catalog /Pages 2 0 R >>',`<< /Type /Pages /Kids [${Array.from({length:60},(_,i)=>`${i+3} 0 R`).join(' ')}] /Count 60 >>`];
  for(let i=0;i<60;i++)objects.push('<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 64 0 R >> >> /Contents 63 0 R >>');
  objects.push(`<< /Length ${Buffer.byteLength(content)} >>\nstream\n${content}\nendstream`,'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>');
  let body='%PDF-1.4\n';const offsets=[0];for(let i=0;i<objects.length;i++){offsets.push(Buffer.byteLength(body));body+=`${i+1} 0 obj\n${objects[i]}\nendobj\n`;}
  const xref=Buffer.byteLength(body);body+=`xref\n0 ${objects.length+1}\n0000000000 65535 f \n`;for(const offset of offsets.slice(1))body+=`${String(offset).padStart(10,'0')} 00000 n \n`;body+=`trailer\n<< /Root 1 0 R /Size ${objects.length+1} >>\nstartxref\n${xref}\n%%EOF`;
  return Buffer.from(body);
}
const pdfBytes=pdf();
const cropSvg=Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="680" height="150"><rect width="100%" height="100%" fill="white"/><path d="M0 0H680M0 149H680" stroke="#666"/><text x="30" y="87" font-family="serif" font-size="50" fill="#222">2//21     4-16     4-10</text></svg>');
async function open(size,width,height,state='attention') {
  const started=performance.now();
  const context=await browser.newContext({viewport:{width,height}});
  const page=await context.newPage();
  let pdfRequests=0;
  const rows=Array.from({length:size},(_,i)=>candidate(i,i===1?'conflict':i===2?'unreadable':i%9===0?'exact':'attention'));
  if(state==='verified')rows[1]=candidate(1,'verified');
  await page.route('**/api/**',route=>{
    const url=new URL(route.request().url());const endpoint=url.pathname;
    if(endpoint==='/api/auth/me')return route.fulfill({json:user});
    if(endpoint.endsWith('/overview')){
      const groups=new Map();for(const row of rows){const key=JSON.stringify([row.status,row.safeToConfirm,!!row.verifiedAt]);groups.set(key,(groups.get(key)||0)+1);}
      const sections=[...groups].map(([key,count])=>{const [status,safeToConfirm,verified]=JSON.parse(key);return {candidateType:'AwardKhasra',status,safeToConfirm,verified,count};});
      return route.fulfill({json:{targetAwardId:guid(1),selectedVillageId:guid(2),sourceDocumentId:guid(3),documentName:'Synthetic Award.pdf',awardNumber:'02/2012',villageName:'Bijwasan',sections,pages:Array.from({length:Math.ceil(size/12)},(_,i)=>({page:43+i,count:rows.filter(r=>r.sourcePage===43+i).length,exact:rows.filter(r=>r.sourcePage===43+i&&r.safeToConfirm).length}))}});
    }
    if(endpoint.endsWith('/candidates')){const bucket=url.searchParams.get('bucket');const sourcePage=url.searchParams.get('sourcePage');const index=Number(url.searchParams.get('page')||0);const filtered=rows.filter(r=>(bucket==='attention'?!r.safeToConfirm&&!r.verifiedAt:bucket==='exact'?r.safeToConfirm:bucket==='conflict'?r.status==='Conflict':bucket==='unreadable'?r.status==='Invalid':bucket==='verified'?!!r.verifiedAt:true)&&(!sourcePage||String(r.sourcePage)===sourcePage));return route.fulfill({json:{items:filtered.slice(index*100,index*100+100),page:index,pageSize:100,totalCount:filtered.length}});}
    if(endpoint.includes('/source-crop'))return route.fulfill({body:cropSvg,contentType:'image/svg+xml'});
    if(endpoint.includes('/documents/')&&endpoint.endsWith('/content')){pdfRequests++;return route.fulfill({body:pdfBytes,contentType:'application/pdf'});}
    if(route.request().method()==='POST')return route.fulfill({status:204});
    return route.fulfill({status:404,json:{title:'No synthetic response'}});
  });
  await page.goto('http://127.0.0.1:5173/awards/'+guid(1)+'/ingestion/'+guid(4));
  await page.getByRole('main',{name:'Award review workbench'}).waitFor();
  await page.getByText('Review queue').waitFor();
  await page.screenshot({path:path.join(out,`${width}x${height}-${state}-queue.png`),fullPage:false});
  return {page,context,rows,loadMs:Math.round(performance.now()-started),pdfLoads:()=>pdfRequests};
}

try {
  for(const [width,height] of [[1366,768],[1440,900],[1920,1080]]) {
    const {page,context,pdfLoads}=await open(250,width,height);
    await page.getByRole('button',{name:/2\/\/21/}).first().click();
    await page.screenshot({path:path.join(out,`${width}x${height}-conflict.png`)});
    await page.getByRole('button',{name:/3\/\/4/}).first().click().catch(()=>{});
    await page.getByRole('button',{name:'2. Total area recorded Pending'}).click();
    const frameBefore=await page.locator('.award-wb-evidence iframe').getAttribute('src');
    await page.getByRole('button',{name:'3. Area awarded Pending'}).click();
    const frameAfter=await page.locator('.award-wb-evidence iframe').getAttribute('src');
    if(frameBefore!==frameAfter)throw Error('Same-page field focus reloaded PDF');
    await page.screenshot({path:path.join(out,`${width}x${height}-field.png`)});
    const loadsBefore=pdfLoads();
    await page.getByRole('button',{name:/3\/\/5/}).first().click();
    if(pdfLoads()!==loadsBefore)throw Error('Same-page candidate switch reloaded PDF');
    await page.getByRole('button',{name:'3. Area awarded Pending'}).click();
    const human=page.getByRole('textbox',{name:'Human Area awarded'});
    await human.fill('4-11');
    page.once('dialog',dialog=>dialog.dismiss());
    await page.locator('.award-wb-queue-list>button').first().click();
    if(await human.inputValue()!=='4-11')throw Error('Dirty edit was silently discarded');
    let posts=0;page.on('request',request=>{if(request.method()==='POST')posts++;});
    await human.press('s');
    if(posts)throw Error('Shortcut fired while typing in input');
    await page.reload();
    await page.getByText('Review queue').waitFor();
    await page.getByRole('button',{name:'Unreadable',exact:true}).click();
    await page.screenshot({path:path.join(out,`${width}x${height}-unreadable.png`)});
    await page.getByRole('combobox',{name:'Review source page'}).selectOption('43');
    await page.getByRole('button',{name:/Confirm \d+ exact on page 43/}).click();
    await page.screenshot({path:path.join(out,`${width}x${height}-page.png`)});
    await page.getByRole('alertdialog').getByRole('button',{name:'Cancel'}).click();
    await context.close();
    const verified=await open(50,width,height,'verified');
    await verified.page.getByRole('button',{name:'Verified',exact:true}).click();
    await verified.page.getByText('Verified as Review Officer').waitFor();
    await verified.page.waitForTimeout(400);
    await verified.page.screenshot({path:path.join(out,`${width}x${height}-completed.png`)});
    await verified.context.close();
  }
  for(const size of [50,250,1000]){const {page,context,loadMs}=await open(size,1366,768);const rendered=await page.locator('.award-wb-queue-list>button').count();if(rendered>100)throw Error(`Rendered ${rendered} queue rows for ${size}`);await context.close();console.log(`${size} candidates: ${rendered} lightweight queue rows rendered; ${loadMs} ms to ready in synthetic browser run`);}
  console.log(`Screenshots: ${out}`);
}finally{await browser.close();}
