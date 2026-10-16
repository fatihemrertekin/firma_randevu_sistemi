const {chromium}=require('./node_modules/playwright')
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto')
const dir=path.resolve(__dirname,'../../.local/p04-02')
const origin='http://127.0.0.1:8097',out=path.join(dir,'browser'),password='Synthetic!Owner123'
fs.mkdirSync(out,{recursive:true})
const report={synthetic:true,mainOwnerLogin:false,api:[],images:[],errors:[],checks:[]}
function totp(secret){let value=0,bits=0;const bytes=[];for(const ch of secret){value=(value<<5)|'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'.indexOf(ch);bits+=5;if(bits>=8){bits-=8;bytes.push((value>>bits)&255)}}const counter=Buffer.alloc(8);counter.writeBigUInt64BE(BigInt(Math.floor(Date.now()/30000)));const hash=crypto.createHmac('sha1',Buffer.from(bytes)).update(counter).digest(),offset=hash[19]&15;return String((hash.readUInt32BE(offset)&0x7fffffff)%1000000).padStart(6,'0')}
async function post(context,endpoint,data){const token=await context.request.get(origin+'/api/auth/csrf');const response=await context.request.post(origin+endpoint,{data,headers:{'X-CSRF-TOKEN':(await token.json()).token}});report.api.push({endpoint,status:response.status()});return response}
async function get(context,endpoint){const response=await context.request.get(origin+endpoint);assert.equal(response.status(),200,endpoint);return response.json()}
async function inspect(page,name){await page.evaluate(()=>document.fonts.ready);for(const width of [320,390,768,1280]){await page.setViewportSize({width,height:1000});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,name+' '+width);const image=name+'-'+width+'.png';await page.screenshot({path:path.join(out,image),fullPage:true});report.images.push(image)}}
async function main(){
 const browser=await chromium.launch({headless:true,channel:'chrome'})
 try{
  const owner=await browser.newContext(),guest=await browser.newContext(),page=await owner.newPage()
  page.on('pageerror',e=>report.errors.push(e.message))
  await page.goto(origin+'/giris');await page.locator('#email').fill('owner@example.test');await page.locator('#password').fill(password);await page.getByRole('button',{name:'Giriş yap',exact:true}).click()
  await page.getByRole('heading',{name:'İki aşamalı girişi kurun',exact:true}).waitFor();await page.locator('#setup-password').fill(password)
  const setup=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/auth/mfa/setup'&&r.request().method()==='POST')
  await page.getByRole('button',{name:'Kurulumu başlat',exact:true}).click();const key=(await(await setup).json()).key
  await page.locator('#enable-password').fill(password);await page.locator('#enable-code').fill(totp(key));await page.getByRole('button',{name:'İki aşamalı girişi aç',exact:true}).click();await page.getByRole('button',{name:'Kodları kaydettim',exact:true}).click()
  await page.locator('#password').fill(password);await page.getByRole('button',{name:'Giriş yap',exact:true}).click();await page.locator('#mfa-code').fill(totp(key));await page.getByRole('button',{name:'Doğrula',exact:true}).click();await page.getByRole('heading',{name:'İşletme bilgileri',exact:true}).waitFor()
  const serviceResponse=await post(owner,'/api/services/',{id:crypto.randomUUID(),name:'Saç kesimi',durationMinutes:25,price:'450.00'});assert.equal(serviceResponse.status(),201);const service=await serviceResponse.json()
  const members=[];const days=Array.from({length:7},(_,day)=>({day,isClosed:false,opensAt:'09:00',closesAt:'19:00'}))
  const business=await get(owner,'/api/business-hours/');assert.equal((await post(owner,'/api/business-hours/',{version:business.version,days})).status(),200)
  for(const name of ['Personel A','Personel B']){const response=await post(owner,'/api/staff-members/',{id:crypto.randomUUID(),name});assert.equal(response.status(),201);let member=await response.json();assert.equal((await post(owner,`/api/staff-members/${member.id}/services`,{version:member.version,services:[{id:service.id,version:service.version}]})).status(),200);member=await get(owner,'/api/staff-members/'+member.id);assert.equal((await post(owner,`/api/staff-members/${member.id}/hours`,{version:member.version,days})).status(),200);members.push(member)}
  const local=new Date(Date.now()+3*3600000),date=new Date(Date.UTC(local.getUTCFullYear(),local.getUTCMonth(),local.getUTCDate()+1)).toISOString().slice(0,10),at=time=>date+'T'+time+':00+03:00'
  const create=(name,member,time)=>({requestKey:crypto.randomUUID(),serviceId:service.id,serviceVersion:service.version,staffMemberId:member.id,startsAt:at(time),customerName:name,customerPhone:'05000000001'})
  const a=await post(owner,'/api/appointments/pending',create('Müşteri 01',members[0],'09:00'));assert.equal(a.status(),201);const pending=await a.json()
  const b=await post(owner,'/api/appointments/',create('Müşteri 02',members[0],'10:30'));assert.equal(b.status(),201);const second=await b.json()
  const c=await post(owner,'/api/appointments/',create('Müşteri 03',members[1],'09:15'));assert.equal(c.status(),201)
  fs.writeFileSync(path.join(dir,'synthetic-session.json'),JSON.stringify({date,key,service,members,second,pending},null,2))
  await owner.storageState({path:path.join(dir,'owner-state.json')})
  const listPath='/yonetim/randevular?tarih='+date
  await page.goto(origin+listPath);await page.getByRole('heading',{name:'Personel A',exact:true}).waitFor();await page.getByRole('heading',{name:'Personel B',exact:true}).waitFor()
  assert(!await page.locator('main').innerText().then(t=>t.includes('+905000000001')))
  await inspect(page,'daily');report.checks.push('grouped-day-and-no-phone')
  await page.locator('#appointment-filter').selectOption(members[0].id);await page.waitForURL('**calisan='+members[0].id);await page.getByRole('heading',{name:'Personel A',exact:true}).waitFor();assert.equal(await page.getByRole('heading',{name:'Personel B',exact:true}).count(),0)
  await page.reload();await page.getByRole('heading',{name:'Personel A',exact:true}).waitFor();assert.equal(await page.locator('#appointment-filter').inputValue(),members[0].id);report.checks.push('filter-url-refresh')
  await page.goto(origin+listPath);await page.getByRole('heading',{name:'Personel A',exact:true}).waitFor();await page.getByRole('link',{name:'Müşteri 01, 09.00 randevusunun ayrıntıları'}).click()
  await page.getByRole('heading',{name:'Müşteri 01',exact:true}).waitFor();await inspect(page,'detail-pending')
  await page.getByRole('button',{name:'Onayla',exact:true}).click();await page.getByRole('button',{name:'İşlemi onayla',exact:true}).click();await page.getByRole('status').filter({hasText:'Güncel durum: Onaylı'}).waitFor();report.checks.push('pending-confirm')
  await page.goto(origin+listPath);await page.getByRole('link',{name:'Yeni randevu',exact:true}).click();await page.getByLabel('Hizmet',{exact:true}).selectOption(service.id);await page.getByLabel('Çalışan',{exact:true}).selectOption(members[0].id)
  await page.getByRole('radio',{name:'09.30',exact:true}).check();await page.getByLabel('Ad soyad',{exact:true}).fill('Sentetik elle kayıt');await page.getByLabel('Telefon',{exact:true}).fill('05000000001');await inspect(page,'create-ready')
  await page.setViewportSize({width:1280,height:1000})
  const unsaved=[];page.once('dialog',async dialog=>{unsaved.push(dialog.type());await dialog.dismiss()});await page.getByRole('link',{name:'Hizmetler',exact:true}).click();assert.equal(new URL(page.url()).pathname,'/yonetim/randevular/yeni');assert.equal(unsaved.length,1);report.checks.push('dirty-navigation-protected')
  let committed,requests=0;let release;const responseGate=new Promise(resolve=>{release=resolve})
  await page.route('**/api/appointments/',async route=>{if(route.request().method()!=='POST'){return route.continue()}requests++;const response=await route.fetch();assert.equal(response.status(),201);committed=await response.json();await responseGate;await route.abort('failed')})
  await page.getByRole('button',{name:'Randevuyu kaydet',exact:true}).evaluate(button=>{button.click();button.click()})
  await page.waitForFunction(()=>document.querySelector('button[type="submit"]')?.disabled)
  await page.waitForFunction(()=>[...document.querySelectorAll('#management-navigation a')].every(a=>a.getAttribute('aria-disabled')==='true'))
  for(let index=0;index<100&&!committed;index++)await page.waitForTimeout(50)
  assert(committed);assert.equal(requests,1);release()
  await page.getByText('İşlemin sonucu belirsiz.',{exact:false}).waitFor();await inspect(page,'create-ambiguous')
  await page.unroute('**/api/appointments/');const beforeRetry=await get(owner,'/api/appointments/?date='+date)
  await page.getByRole('button',{name:'Aynı işlemi yeniden dene',exact:true}).click();await page.getByRole('status').filter({hasText:'Randevu kaydedildi.'}).waitFor()
  const afterRetry=await get(owner,'/api/appointments/?date='+date);assert.equal(afterRetry.totalCount,beforeRetry.totalCount);assert.equal(afterRetry.items.filter(item=>item.id===committed.id).length,1);report.checks.push('single-submit-through-body-and-ambiguous-replay-no-duplicate')
  await page.goto(origin+'/yonetim/randevular/'+second.id+'/tasi?tarih='+date);await page.getByRole('heading',{name:'Randevuyu taşı',exact:true}).waitFor();await page.getByLabel('Çalışan',{exact:true}).selectOption(members[1].id);await page.getByRole('radio',{name:'11.00',exact:true}).check();await inspect(page,'move-ready')
  await page.getByRole('button',{name:'Yeni zamanı kaydet',exact:true}).click();await page.getByRole('status').filter({hasText:'Randevu kaydedildi.'}).waitFor();const moved=await get(owner,'/api/appointments/'+second.id);assert.equal(moved.staffMemberId,members[1].id);assert.equal(Date.parse(moved.startsAt),Date.parse(at('11:00')));report.checks.push('move-preserves-snapshot')
  await page.goto(origin+'/yonetim/randevular/'+second.id+'?tarih='+date);await page.getByRole('button',{name:'İptal et',exact:true}).click();await page.getByRole('button',{name:'İşlemi onayla',exact:true}).click();await page.getByRole('status').filter({hasText:'Güncel durum: İptal edildi'}).waitFor();await inspect(page,'cancelled-read-only');assert.equal(await page.getByRole('button',{name:'İptal et',exact:true}).count(),0);report.checks.push('cancel-confirmation-and-history-read-only')
  await page.route('**/api/appointments/?*',route=>route.fulfill({status:503,contentType:'application/problem+json',body:'{"title":"Synthetic failure"}'}));await page.goto(origin+listPath);await page.getByRole('alert').waitFor();assert.equal(await page.getByText('Seçili günde randevu yok.',{exact:true}).count(),0);await inspect(page,'list-error');await page.unroute('**/api/appointments/?*')
  await page.getByRole('button',{name:'Listeyi yenile',exact:true}).click();await page.getByRole('heading',{name:'Personel A',exact:true}).waitFor();report.checks.push('real-api-error-recovery')
  await page.route('**/api/appointments/?*',route=>route.fulfill({status:403,contentType:'application/problem+json',body:'{}'}));await page.goto(origin+listPath);await page.getByRole('alert').filter({hasText:'yetkiniz yok'}).waitFor();assert.equal(await page.locator('main h3').count(),0);await inspect(page,'list-forbidden');await page.unroute('**/api/appointments/?*');report.checks.push('forbidden-not-empty')
  const guestPage=await guest.newPage();await guestPage.goto(origin+listPath);await guestPage.getByRole('heading',{name:'İşletme girişi',exact:true}).waitFor();const protectedRead=await guest.request.get(origin+'/api/appointments/?date='+date);assert.equal(protectedRead.status(),401);report.checks.push('anonymous-api-and-login-return')
  for(const route of ['/api/appointments/','/api/appointments/'+committed.id])assert.equal((await guest.request.get(origin+route)).status(),401)
  assert.deepEqual(report.errors,[]);report.pass=true;fs.writeFileSync(path.join(dir,'browser-report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({pass:true,checks:report.checks,images:report.images.length,errors:report.errors}))
 }finally{await browser.close()}
}
main().catch(error=>{fs.writeFileSync(path.join(dir,'browser-failure.json'),JSON.stringify({error:String(error.stack),report},null,2));console.error(String(error.stack));process.exitCode=1})
