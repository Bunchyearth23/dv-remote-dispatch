const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const source=fs.readFileSync(require('node:path').join(__dirname,'../main.js'),'utf8');
const draw=vm.runInNewContext(source.slice(source.indexOf('function createJunctionDirection('),source.indexOf('function updateAllJunctions('))+';createJunctionDirection');
test('selected branch arrow follows geographical north/east, not branch number',()=>{
  const route=(lat,lng)=>({getLatLngs:()=>[{lat:0,lng:0},{lat,lng}]});
  const junction={routes:[route(0,1),route(1,0)]};
  assert.match(draw(junction,0),/rotate\(0\)/);
  assert.match(draw(junction,1),/rotate\(-90\)/);
  assert.match(draw(junction,0),/RIGHT/);
  assert.match(draw(junction,1),/LEFT/);
});
test('missing or coincident geometry does not invent a direction',()=>{
  const result=draw({routes:[null,{getLatLngs:()=>[{lat:0,lng:0},{lat:0,lng:0}]}]},1);
  assert.doesNotMatch(result,/NaN|<path/);
});
