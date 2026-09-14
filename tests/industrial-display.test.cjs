const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
function display(){const context={document:{getElementById:()=>null},window:{},Map,Date};vm.runInNewContext(fs.readFileSync(require.resolve('../industrial-dispatch.js'),'utf8'),context);return context.BdvmIndustrialDisplay}
test('cargo presentation distinguishes unknown, empty, partial and full without an economy request',()=>{
 const view=display();assert.match(view.describe({}).short,/Unknown/);
 assert.match(view.describe({loadedAmount:0,cargoCapacity:1}).short,/Empty 0%/);
 assert.match(view.describe({loadedAmount:.4,cargoCapacity:1}).short,/Partial 40%/);
 assert.match(view.describe({loadedAmount:1,cargoCapacity:1}).short,/Full 100%/);
 assert.match(view.describe({loadedAmount:.4}).short,/Loaded/);
});
test('exact GUID joins tags and dossiers, exposing mismatches and destination in text',()=>{
 const view=display();view.accept({fleet:[{assetId:'a',carGuid:'GUID-A',state:'Available'}],rollingStockTags:[{assetId:'a',sourceFacilityId:'SM',cargoId:'Steel',lifetime:'UntilEmpty'}],locationChoices:[{id:'GF',name:'Goods Factory'}],industrial:{contracts:[{dossierId:'d',displayName:'My delivery',state:'Active',destinationFacilityId:'GF',assignedWagons:[{assetId:'a'}]}]}});
 const loaded=view.describe({guid:'guid-a',cargoId:'Steel',loadedAmount:1,cargoCapacity:1});
 assert.equal(loaded.dossierId,'d');assert.equal(loaded.destination,'GF');assert.match(loaded.detail,/My delivery/);assert.match(loaded.detail,/Goods Factory/);
 const wrong=view.describe({guid:'guid-a',cargoId:'Coal',loadedAmount:1,cargoCapacity:1});assert.equal(wrong.color,'#ec7777');assert.match(wrong.detail,/differs from tag/);
 assert.equal(view.describe({guid:'other',loadedAmount:0}).dossierId,undefined);
 view.accept({});assert.equal(view.describe({guid:'guid-a'}).dossierId,undefined);
});
test('text escaping and dispatch navigation replace the former mutation form',()=>{
 const view=display();assert.equal(view.escape('<x>"&'),'&lt;x&gt;&quot;&amp;');
 const html=fs.readFileSync(require.resolve('../index.html'),'utf8');assert.ok(html.includes('/management?tab=contracts'));assert.ok(!html.includes('industryDispatchCreate'));assert.ok(html.includes('value="industrial"'));
 const script=fs.readFileSync(require.resolve('../industrial-dispatch.js'),'utf8');assert.ok(!script.includes('setInterval('));assert.ok(!script.includes("operation:'start-manual'"));
});
