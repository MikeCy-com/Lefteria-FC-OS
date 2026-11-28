import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
test('POS install has distinct manifest identity and exact start scope',()=>{
 const pos=JSON.parse(read('static/pos.webmanifest'));
 const os=JSON.parse(read('static/manifest.webmanifest'));
 assert.notEqual(pos.id,os.id);assert.equal(os.start_url,'/app');
 assert.equal(pos.start_url,'/admin/pos/terminal');assert.equal(pos.scope,pos.start_url);
 assert.equal(pos.display,'standalone');assert.equal(pos.icons.length,2);
 const layout=read('src/routes/+layout.svelte');
 assert.match(layout,/posTerminal\?'\/pos\.webmanifest':'\/manifest\.webmanifest'/);
 assert.doesNotMatch(read('src/app.html'),/rel="manifest"/);
});
test('cashier terminal reuses live admin session and exact register component without offline or stored credentials',()=>{
 const terminal=read('src/routes/admin/pos/terminal/+page.svelte');
 assert.doesNotThrow(()=>compile(terminal,{filename:'terminal/+page.svelte',generate:'server'}));
 for(const marker of ["user.role==='cashier'","auth.login(username,password,null)","AdminPosCashierShiftsPanel","appInstall.install()","auth.logout()","pos-install-controls"])assert.ok(terminal.includes(marker),marker);
 assert.doesNotMatch(terminal,/localStorage|sessionStorage|\/api\/pos\/login|FoundingAdminSetup/);
 const link=read('src/lib/admin/AdminPosTerminalLink.svelte');
 assert.doesNotThrow(()=>compile(link,{filename:'AdminPosTerminalLink.svelte',generate:'server'}));
 assert.match(link,/href="\/admin\/pos\/terminal" data-sveltekit-reload/);
 assert.ok(read('src/routes/admin/pos/+page.svelte').includes('<AdminPosTerminalLink/>'));
});
