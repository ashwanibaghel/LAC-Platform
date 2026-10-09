import test from 'node:test';
import assert from 'node:assert/strict';
import { parseAreaInput, parseRevenueShorthand, AREA_UNITS, areaToSqm, sqmToArea } from '../src/calculator/landConversions.js';
import { parseRevenueShorthandBulk } from '../src/calculator/bulkRevenueParser.js';
import { buildCompensationRequest, INITIAL_COMPENSATION_FORM_STATE, createCalculationSubmissionKey } from '../src/calculator/compensationContracts.ts';
for(const [text, canonical] of [['4-16','4.8'],['2-9-1','2.4525'],['0-18','0.9'],['5','5'],['4.8','4.8']]) {
  test(`exact normalization ${text} => ${canonical}`,()=>assert.equal(parseAreaInput(text,'bigha').canonical,canonical));
}
for(const text of ['-1','4-20','2-9-20','4--16','4-16-','4-16-1-0','4.1-2','4–16','1e2','NaN','0.1234567890123','1000000000000-1']) {
  test(`rejects ${text}`,()=>assert.equal(parseAreaInput(text,'bigha').valid,false));
}
test('non-Bigha units reject revenue notation',()=>{for(const unit of Object.keys(AREA_UNITS).filter(x=>x!=='bigha'))assert.equal(parseAreaInput('4-16',unit).valid,false);});
test('no silent rounding for large fractional decimals',()=>assert.equal(parseAreaInput('999999999999.123456789012').canonical,'999999999999.123456789012'));
test('revenue arithmetic and bulk share exact two/three component parsing',()=>{
  assert.deepEqual(parseRevenueShorthand('4-16').value,{bigha:4,biswa:16,biswansi:0});
  assert.equal(parseRevenueShorthand('4.123').valid,false);
  const rows=parseRevenueShorthandBulk('4-16,2-9-1').rows;assert.equal(rows.length,2);assert.equal(rows[1].value.biswansi,1);
});
test('all 81 conversion pairs round-trip with frozen constants',()=>{
  for(const from of Object.keys(AREA_UNITS))for(const to of Object.keys(AREA_UNITS)){
    const converted=sqmToArea(areaToSqm(4.8,from),to);const back=sqmToArea(areaToSqm(converted,to),from);assert.ok(Math.abs(back-4.8)<1e-12,from+' => '+to);
  }
  assert.equal(AREA_UNITS.bigha.sqm,843);assert.equal(AREA_UNITS.acre.sqm,4046.8564224);
});
test('canonical compensation requests are identical across shorthand and decimal for every rate unit',()=>{
  const state={...INITIAL_COMPENSATION_FORM_STATE,landArea:'4-16',marketRate:'5300000',multiplicationFactor:'2',solatiumPercentage:'100',annualRate:'12',durationValue:'30'};
  for(const marketRateUnit of Object.keys(AREA_UNITS))assert.deepEqual(buildCompensationRequest({...state,marketRateUnit}),buildCompensationRequest({...state,marketRateUnit,landArea:'4.8'}));
});
test('submission keys work without secure-context randomUUID, with UUID version/variant and no duplicates',()=>{
  const cryptoSource={getRandomValues: bytes => crypto.getRandomValues(bytes)};
  const keys=Array.from({length:100},()=>createCalculationSubmissionKey(cryptoSource));
  assert.equal(new Set(keys).size,100);for(const key of keys)assert.match(key,/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/);
});
