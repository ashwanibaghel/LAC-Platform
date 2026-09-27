import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';

const out = path.resolve('test-results/award-workbench');
const argument = name => {const index=process.argv.indexOf(name);return index<0?undefined:process.argv[index+1];};
const baseUrl = argument('--base') || process.env.AWARD_TEST_BASE_URL || 'http://127.0.0.1:5173';
const realOutputPath=argument('--real-output') || process.env.AWARD_REAL_WORKER_OUTPUT;
const realPdfPath=argument('--real-pdf') || process.env.AWARD_REAL_PDF;
const realCropPath=argument('--real-crop') || process.env.AWARD_REAL_ROW_CROP;
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
async function open(size,width,height,state='attention',customRows,pdfUnavailable=false,sourcePdf=pdfBytes,sourceCrop=cropSvg) {
  const started=performance.now();
  const context=await browser.newContext({viewport:{width,height}});
  const page=await context.newPage();
  let pdfRequests=0;
  const rows=customRows||Array.from({length:size},(_,i)=>candidate(i,i===1?'conflict':i===2?'unreadable':i%9===0?'exact':'attention'));
  const posts=[];
  if(state==='verified')rows[1]=candidate(1,'verified');
  await page.route('**/api/**',route=>{
    const url=new URL(route.request().url());const endpoint=url.pathname;
    if(endpoint==='/api/auth/me')return route.fulfill({json:user});
    if(endpoint.endsWith('/workspace'))return route.fulfill({json:{villages:[{id:guid(2),name:'Bijwasan'}]}});
    if(endpoint.endsWith('/overview')){
      const groups=new Map();for(const row of rows){const key=JSON.stringify([row.candidateType,row.status,row.safeToConfirm,!!row.verifiedAt]);groups.set(key,(groups.get(key)||0)+1);}
      const sections=[...groups].map(([key,count])=>{const [candidateType,status,safeToConfirm,verified]=JSON.parse(key);return {candidateType,status,safeToConfirm,verified,count};});
      const pageNumbers=[...new Set(rows.map(row=>row.sourcePage))].sort((a,b)=>a-b);
      return route.fulfill({json:{targetAwardId:guid(1),selectedVillageId:guid(2),sourceDocumentId:guid(3),documentName:state==='real-claim'?'Pochanpur Award.pdf':'Synthetic Award.pdf',awardNumber:state==='real-claim'?'30/2002-03':'02/2012',villageName:state==='real-claim'?'Pochanpur':'Bijwasan',sections,pages:pageNumbers.map(number=>({page:number,count:rows.filter(r=>r.sourcePage===number).length,exact:rows.filter(r=>r.sourcePage===number&&r.safeToConfirm).length}))}});
    }
    if(endpoint.endsWith('/candidates')){const bucket=url.searchParams.get('bucket');const sourcePage=url.searchParams.get('sourcePage');const type=url.searchParams.get('type');const typeGroup=url.searchParams.get('typeGroup');const search=url.searchParams.get('search')?.toLowerCase();const index=Number(url.searchParams.get('page')||0);const filtered=rows.filter(r=>(bucket==='attention'?!r.safeToConfirm&&!r.verifiedAt:bucket==='exact'?r.safeToConfirm:bucket==='conflict'?r.status==='Conflict':bucket==='unreadable'?r.status==='Invalid':bucket==='verified'?!!r.verifiedAt:true)&&(!sourcePage||String(r.sourcePage)===sourcePage)&&(!type||r.candidateType===type)&&(typeGroup!=='other'||!['Claim','AwardKhasra'].includes(r.candidateType))&&(!search||r.payloadJson.toLowerCase().includes(search)));return route.fulfill({json:{items:filtered.slice(index*100,index*100+100),page:index,pageSize:100,totalCount:filtered.length}});}
    if(endpoint.includes('/source-crop'))return route.fulfill({body:sourceCrop,contentType:sourceCrop===cropSvg?'image/svg+xml':'image/png'});
    if(endpoint.includes('/documents/')&&endpoint.endsWith('/content')){pdfRequests++;return pdfUnavailable?route.fulfill({status:404,json:{title:'Source file missing'}}):route.fulfill({body:sourcePdf,contentType:'application/pdf'});}
    if(route.request().method()==='POST'){
      posts.push({endpoint,body:route.request().postDataJSON()});
      if(endpoint.endsWith('/confirm-exact')){for(const row of rows.filter(r=>r.safeToConfirm&&!r.verifiedAt)){row.verifiedAt='2026-09-26T10:00:00Z';row.verifiedBy='reviewer';}return route.fulfill({json:{confirmed:1}});}
      if(endpoint.endsWith('/verify')){const row=rows.find(r=>endpoint.includes(r.id));if(row){row.verifiedAt='2026-09-26T10:00:00Z';row.verifiedBy='reviewer';row.status='Ready';}return route.fulfill({status:204});}
      return route.fulfill({status:204});
    }
    return route.fulfill({status:404,json:{title:'No synthetic response'}});
  });
  await page.goto(baseUrl+'/awards/'+guid(1)+'/ingestion/'+guid(4));
  await page.getByRole('main',{name:'Award review workbench'}).waitFor();
  await page.getByText('Review queue').waitFor();
  await page.screenshot({path:path.join(out,`${width}x${height}-${state}-queue.png`),fullPage:false});
  return {page,context,rows,posts,loadMs:Math.round(performance.now()-started),pdfLoads:()=>pdfRequests};
}

function fact(i,type,payload){return {...candidate(i),id:guid(i+700),candidateType:type,status:'NeedsReview',safeToConfirm:false,payloadJson:JSON.stringify(payload),sourcePage:43,fieldReviewJson:'[]'};}

try {
  for(const [width,height] of [[1280,720],[1366,768],[1440,900]]) {
    const {page,context,pdfLoads}=await open(250,width,height);
    const layout=await page.evaluate(()=>{
      const box=selector=>{const r=document.querySelector(selector).getBoundingClientRect();return {x:Math.round(r.x),y:Math.round(r.y),width:Math.round(r.width),height:Math.round(r.height)};};
      return {viewport:{width:innerWidth,height:innerHeight},root:box('.award-wb'),queue:box('.award-wb-queue'),queueList:box('.award-wb-queue-list'),editor:box('.award-wb-editor'),actions:box('.award-wb-editor-actions'),source:box('.award-wb-evidence'),pdf:box('.award-wb-evidence iframe'),pageOverflow:document.documentElement.scrollWidth>innerWidth};
    });
    if(layout.pageOverflow||layout.queueList.height<260||layout.actions.height<40||layout.pdf.height<300)throw Error(`Workbench geometry failed: ${JSON.stringify(layout)}`);
    console.log(`${width}x${height} layout: ${JSON.stringify(layout)}`);
    await page.getByRole('button',{name:'Hide Queue'}).click();
    if(await page.locator('.award-wb-grid').getAttribute('data-queue-hidden')!=='true')throw Error('Hide Queue state failed');
    await page.getByRole('button',{name:'Focus Source'}).click();
    const focused=await page.locator('.award-wb-evidence').boundingBox();
    if(focused.width<=layout.source.width)throw Error('Focus Source did not enlarge evidence');
    await page.getByRole('button',{name:'Balanced View'}).click();
    const grid=page.locator('.award-wb-grid');
    if(await grid.getAttribute('data-queue-hidden')!=='false'||await grid.getAttribute('data-source-focused')!=='false'||!await page.locator('.award-wb-queue').isVisible())throw Error('Balanced View did not restore the three-pane queue');
    await page.getByRole('button',{name:/2\/\/21/}).first().click();
    await page.screenshot({path:path.join(out,`${width}x${height}-conflict.png`)});
    await page.getByRole('button',{name:/3\/\/4/}).first().click().catch(()=>{});
    await page.getByRole('button',{name:'Total area recorded Pending'}).click();
    const frameBefore=await page.locator('.award-wb-evidence iframe').getAttribute('src');
    await page.getByRole('button',{name:'Area awarded Pending'}).click();
    const frameAfter=await page.locator('.award-wb-evidence iframe').getAttribute('src');
    if(frameBefore!==frameAfter)throw Error('Same-page field focus reloaded PDF');
    await page.screenshot({path:path.join(out,`${width}x${height}-field.png`)});
    const loadsBefore=pdfLoads();
    await page.getByRole('button',{name:/3\/\/5/}).first().click();
    if(pdfLoads()!==loadsBefore)throw Error('Same-page candidate switch reloaded PDF');
    await page.getByRole('button',{name:'Area awarded Pending'}).click();
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
    await page.getByRole('combobox',{name:'Review status'}).selectOption('unreadable');
    await page.screenshot({path:path.join(out,`${width}x${height}-unreadable.png`)});
    await page.getByRole('combobox',{name:'Review source page'}).selectOption('43');
    await page.getByRole('button',{name:/Confirm \d+ exact on page 43/}).click();
    await page.screenshot({path:path.join(out,`${width}x${height}-page.png`)});
    await page.getByRole('alertdialog').getByRole('button',{name:'Cancel'}).click();
    await context.close();
    const verified=await open(50,width,height,'verified');
    await verified.page.getByRole('combobox',{name:'Review status'}).selectOption('verified');
    await verified.page.getByRole('combobox',{name:'Review status'}).waitFor();
    await verified.page.waitForTimeout(400);
    await verified.page.screenshot({path:path.join(out,`${width}x${height}-completed.png`)});
    await verified.context.close();
  }
  for(const size of [50,250,1000]){const {page,context,loadMs}=await open(size,1366,768);const rendered=await page.locator('.award-wb-queue-list>button').count();if(rendered>100)throw Error(`Rendered ${rendered} queue rows for ${size}`);await context.close();console.log(`${size} candidates: ${rendered} lightweight queue rows rendered; ${loadMs} ms to ready in synthetic browser run`);}
  const unavailable=await open(5,1280,720,'attention',undefined,true);
  await unavailable.page.getByText('PDF could not be displayed here.').waitFor();
  if(!await unavailable.page.getByRole('button',{name:'Retry'}).isVisible()||!await unavailable.page.getByRole('link',{name:/Open PDF/}).count())throw Error('PDF fallback lacks actions');
  await unavailable.context.close();
  const factRows=[candidate(1,'conflict'),fact(2,'Notification',{sectionType:'Section 4',notificationNumber:'FIC/12',notificationDate:null}),fact(3,'AwardValuationRule',{ruleType:'Rate',rateAmount:null,rateUnit:null,legalSection:null}),fact(4,'AwardCompensationRule',{ruleType:'Solatium',ratePercent:null,rateAmount:null,legalSection:null}),candidate(5,'exact')];
  const review=await open(factRows.length,1366,768,'attention',factRows);
  const selectFact=async type=>review.page.locator('.award-wb-queue-list>button').filter({hasText:type}).click();
  await selectFact('Notification');
  await review.page.getByRole('textbox',{name:'Notification number'}).fill('FIC/13');
  await review.page.getByRole('textbox',{name:'Notification date'}).fill('2026-09-26');
  await review.page.screenshot({path:path.join(out,'1366x768-notification-correction.png')});
  await review.page.getByRole('button',{name:/Confirm & next/}).click();
  await selectFact('AwardValuationRule');
  await review.page.getByRole('spinbutton',{name:'Rate amount'}).pressSequentially('125.75');
  if(await review.page.getByRole('spinbutton',{name:'Rate amount'}).inputValue()!=='125.75')throw Error('Decimal typing was interrupted');
  await review.page.screenshot({path:path.join(out,'1366x768-valuation-correction.png')});
  await review.page.getByRole('button',{name:/Confirm & next/}).click();
  await selectFact('AwardCompensationRule');
  await review.page.getByRole('spinbutton',{name:'Rate percent'}).fill('30');
  await review.page.getByRole('spinbutton',{name:'Rate amount'}).fill('1500.5');
  await review.page.screenshot({path:path.join(out,'1366x768-compensation-correction.png')});
  await review.page.getByRole('button',{name:/Confirm & next/}).click();
  const edits=review.posts.filter(p=>p.endpoint.endsWith('/verify')).map(p=>JSON.parse(p.body.correctedPayloadJson));
  if(edits[0].notificationNumber!=='FIC/13'||edits[0].notificationDate!=='2026-09-26'||typeof edits[1].rateAmount!=='number'||edits[1].rateAmount!==125.75||typeof edits[2].ratePercent!=='number'||typeof edits[2].rateAmount!=='number')throw Error('Typed fact corrections lost their candidate contract types');
  await review.page.getByRole('combobox',{name:'Review status'}).selectOption('attention');
  if(!await review.page.locator('.award-wb-queue-list>button').filter({hasText:'Conflict'}).count())throw Error('OCR disagreement left Attention');
  await review.page.screenshot({path:path.join(out,'1366x768-ocr-disagreement-attention.png')});
  await review.page.getByRole('combobox',{name:'Review status'}).selectOption('exact');
  await review.page.screenshot({path:path.join(out,'1366x768-exact-positive.png')});
  await review.page.getByRole('button',{name:/Confirm \d+ exact in session/}).click();
  await review.page.getByRole('alertdialog').getByRole('button',{name:'Confirm',exact:true}).click();
  if(!factRows[4].verifiedAt||factRows[4].status!=='Ready'||review.posts.some(p=>p.endpoint.endsWith('/commit-verified')))throw Error('Exact confirmation committed canonical data');
  await review.page.screenshot({path:path.join(out,'1366x768-exact-verified-uncommitted.png')});
  await review.context.close();
  const claimRows=Array.from({length:163},(_,i)=>{
    const serial=i+1;
    const claimant=serial===3?'Ved Prakash s/o Sardar Singh V&PO Bindapur, ND':serial===4?'Harikishan s/o Mir Singh':serial===5?'Amit Kumar Jain':`Claimant ${serial}`;
    const row=fact(serial+1000,'Claim',{sourceSerialNumber:String(serial),claimantText:claimant,khasraReferences:serial===3?'12//20/2 etc':`${serial}//1`,claimedAreaText:'9-18',claimText:'Rs.3000/- per sq yard for land; Rs.5 lacs for boundary wall',claimedRateAmount:3000,claimedRateUnit:'sq yard',claimedAmount:null});
    row.sourcePage=8;row.sourceLocatorJson=JSON.stringify({page:8,sourceRegion:{x:100,y:200,width:900,height:100}});
    if(serial===3)row.khasraMatches=[{sourceReference:'12//20/2',khasraId:guid(900),displayNumber:'12//20/2'}];
    return row;
  });
  const claimReview=await open(claimRows.length,1366,768,'claims',claimRows);
  const claimPage=claimReview.page;
  await claimPage.getByRole('combobox',{name:'Candidate type'}).selectOption('claims');
  await claimPage.getByText('163 findings').waitFor();
  if(await claimPage.locator('.award-wb-queue-list>button').count()!==100)throw Error('Claims queue did not paginate 163 rows');
  await claimPage.getByRole('textbox',{name:'Search review queue'}).fill('Ved Prakash');
  await claimPage.getByRole('button',{name:/Ved Prakash/}).waitFor();
  if(await claimPage.locator('.award-wb-queue-list>button').count()!==1)throw Error('Claimant search did not narrow queue');
  await claimPage.getByRole('textbox',{name:'Search review queue'}).fill('12//20/2');
  await claimPage.getByRole('button',{name:/Ved Prakash/}).waitFor();
  if(await claimPage.locator('.award-wb-queue-list>button').count()!==1)throw Error('Khasra reference search failed');
  await claimPage.getByRole('textbox',{name:'Search review queue'}).fill('Claimant 163');
  await claimPage.getByRole('button',{name:/Claimant 163/}).waitFor();
  if(await claimPage.locator('.award-wb-queue-list>button').count()!==1)throw Error('Search did not reach beyond first 100 rows');
  await claimPage.getByRole('textbox',{name:'Search review queue'}).fill('Ved Prakash');
  await claimPage.getByRole('textbox',{name:'Claimant as recorded'}).waitFor();
  for(const name of ['Khasra references','Claimed area','Claim details','Claimed land rate','Claimed land rate unit','Source serial']){
    if(!await claimPage.getByRole(name==='Claimed land rate'?'spinbutton':'textbox',{name,exact:true}).isVisible())throw Error(`Missing Claim editor field ${name}`);
  }
  if(!await claimPage.getByText(/Exact village-master check: 12\/\/20\/2 matches/).isVisible())throw Error('Exact village-master preview missing');
  await claimPage.getByRole('button',{name:'Show crop'}).click();
  if(!await claimPage.getByText('Claimant row').isVisible()||!await claimPage.locator('.award-wb-crop.claim-row img').isVisible())throw Error('Whole claimant row crop unavailable');
  if(!await claimPage.getByText('Original PDF · Page 8').isVisible())throw Error('Claim source page is wrong');
  await claimPage.screenshot({path:path.join(out,'1366x768-claimant-row.png')});
  await claimPage.getByRole('button',{name:/Confirm & next/}).click();
  if(!claimReview.posts.some(post=>post.endpoint.endsWith('/verify')&&JSON.parse(post.body.correctedPayloadJson).claimantText.startsWith('Ved Prakash')))throw Error('Claim confirmation did not submit source fields');
  if(claimRows.some(row=>row.safeToConfirm))throw Error('Claim rows became bulk exact candidates');
  await claimReview.context.close();
  if(realOutputPath&&realPdfPath&&realCropPath){
    const extracted=JSON.parse(fs.readFileSync(realOutputPath,'utf8'));
    const realClaims=extracted.candidates.filter(item=>item.candidateType==='Claim');
    const realCourt=extracted.candidates.filter(item=>item.candidateType==='CourtCase'&&item.page===7);
    if(realCourt.length!==6||realCourt[0].structuredPayload.caseNumber?.normalizedSuggestion!=='4721/2002'||realCourt.some(item=>item.structuredPayload.caseType!=='CWP'))throw Error('Real Pochanpur Court table regression');
    const realRows=realClaims.map((item,index)=>{
      const row=fact(index+2000,'Claim',item.structuredPayload);
      row.sourcePage=item.page;row.rawSourceText=item.rawSourceText;
      row.sourceLocatorJson=JSON.stringify({page:item.page,sourceRegion:item.sourceRegion,structuredPayload:item.structuredPayload});
      return row;
    });
    realRows.push(...realCourt.map((item,index)=>{
      const payload=Object.fromEntries(Object.entries(item.structuredPayload).map(([key,value])=>[key,value&&typeof value==='object'&&!Array.isArray(value)?value.normalizedSuggestion??null:value]));
      const row=fact(index+5000,'CourtCase',payload);
      row.sourcePage=item.page;row.rawSourceText=item.rawSourceText;
      row.sourceLocatorJson=JSON.stringify({page:item.page,sourceRegion:item.sourceRegion});
      return row;
    }));
    if(realRows.length<150)throw Error(`Real claimant schedule too short: ${realRows.length}`);
    const actual=await open(realRows.length,1366,768,'real-claim',realRows,false,fs.readFileSync(realPdfPath),fs.readFileSync(realCropPath));
    await actual.page.getByRole('combobox',{name:'Candidate type'}).selectOption('claims');
    await actual.page.getByRole('textbox',{name:'Search review queue'}).fill('Ved Prakash');
    await actual.page.getByRole('textbox',{name:'Claimant as recorded'}).waitFor();
    await actual.page.waitForFunction(()=>document.querySelector('input[aria-label="Khasra references"]')?.value==='12//20/2 etc');
    if(await actual.page.getByRole('textbox',{name:'Khasra references'}).inputValue()!=='12//20/2 etc')throw Error('Real Pochanpur Khasra source lost');
    if(!await actual.page.getByRole('textbox',{name:'Claim details'}).inputValue().then(value=>value.includes('boundary wall')))throw Error('Real Pochanpur wrapped claim lost');
    if(await actual.page.getByRole('spinbutton',{name:'Claimed land rate'}).inputValue()!=='3000')throw Error('Real Pochanpur claimed rate not displayed');
    await actual.page.getByRole('button',{name:'Show crop'}).click();
    await actual.page.locator('.award-wb-crop.claim-row img').waitFor();
    await actual.page.getByText('Original PDF · Page 8').waitFor();
    if(actual.pdfLoads()<1)throw Error('Real Pochanpur PDF was not requested by browser');
    await actual.page.waitForTimeout(3000);
    await actual.page.screenshot({path:path.join(out,'1366x768-real-pochanpur-claimant.png')});
    await actual.page.getByRole('combobox',{name:'Candidate type'}).selectOption('other');
    await actual.page.getByRole('textbox',{name:'Search review queue'}).fill('4721/2002');
    await actual.page.getByRole('button',{name:/4721\/2002/}).waitFor();
    await actual.page.getByText('Original PDF · Page 7').waitFor();
    await actual.page.waitForTimeout(3000);
    await actual.page.screenshot({path:path.join(out,'1366x768-real-pochanpur-court.png')});
    await actual.context.close();
    console.log(`Real Pochanpur browser acceptance: ${realClaims.length} claimant rows, Ved Prakash source fields and page 8 PDF, plus ${realCourt.length} CWP Court rows and page 7 PDF verified`);
  }
  console.log(`Screenshots: ${out}`);
}finally{await browser.close();}
