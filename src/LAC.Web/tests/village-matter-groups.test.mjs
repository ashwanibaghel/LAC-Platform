import assert from 'node:assert/strict';
import test from 'node:test';
import { groupMattersByType } from '../src/matter/matterTypeGroups.ts';

test('General and a future/custom Matter type both remain visible', () => {
  const items = [{id:'1', matterType:'General'}, {id:'2', matterType:'Future custom type'}];
  assert.deepEqual(groupMattersByType(items).flatMap(([, group]) => group).sort((a,b) => a.id.localeCompare(b.id)), items);
});

test('preferred display order preserves every unknown type and every record exactly once', () => {
  const items = ['Zulu', 'Other', 'General', 'Court Case', 'Compensation', 'Alpha', 'General']
    .map((matterType, i) => ({id:String(i), matterType}));
  const groups = groupMattersByType(items);
  assert.deepEqual(groups.map(([type]) => type), ['Court Case', 'Compensation', 'Other', 'Alpha', 'General', 'Zulu']);
  const rendered = groups.flatMap(([, group]) => group);
  assert.equal(new Set(rendered.map(m => m.id)).size, items.length);
  assert.equal(rendered.length, items.length);
  for (const item of items) assert.ok(rendered.includes(item));
});

test('prototype-like type names are ordinary groups', () => {
  const items = ['__proto__', 'constructor', 'toString'].map(matterType => ({matterType}));
  assert.equal(groupMattersByType(items).length, 3);
  assert.equal(groupMattersByType(items).flatMap(([, group]) => group).length, 3);
});

test('blank/null/missing types use Other without modifying records', () => {
  const items = [{matterType:null}, {}, {matterType:'   '}, {matterType:' General '}];
  const groups = groupMattersByType(items);
  assert.deepEqual(groups.map(([type]) => type), ['Other', 'General']);
  assert.equal(groups[0][1].length, 3);
  assert.equal(groups[1][1][0].matterType, ' General ');
  assert.deepEqual(groupMattersByType([]), []);
});
