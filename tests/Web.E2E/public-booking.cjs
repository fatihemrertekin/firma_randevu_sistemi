// Run only against an isolated PostgreSQL host with the in-process fake SMS delivery.
const { chromium } = require('playwright')
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict')
const origin = process.env.PUBLIC_BOOKING_TEST_ORIGIN ?? 'http://127.0.0.1:8111'
const control = process.env.PUBLIC_BOOKING_SMS_TEST_ORIGIN ?? 'http://127.0.0.1:8112'
for (const url of [origin, control]) assert(['127.0.0.1', 'localhost'].includes(new URL(url).hostname), 'Synthetic loopback hosts only')
assert.notEqual(new URL(origin).port, '8080', 'Main runtime is never a write target')
const out = process.env.PUBLIC_BOOKING_EVIDENCE ?? path.resolve(__dirname, '../../.local/public-booking-ui/browser')
fs.mkdirSync(out, { recursive: true })
;(async () => {
  const browser = await chromium.launch({ headless: true, channel: 'chrome' })
  const errors = [], screenshots = [], bodies = { create: [], cancel: [] }
  try {
    const context = await browser.newContext(), page = await context.newPage()
    page.on('pageerror', error => errors.push(error.message))
    let loseCreate = true, loseCancel = true
    await page.route('**/api/public-booking/appointments', async route => {
      if (route.request().method() !== 'POST') return route.continue()
      bodies.create.push(route.request().postDataJSON())
      if (loseCreate) { loseCreate = false; await route.fetch(); return route.abort('failed') }
      return route.continue()
    })
    await page.route('**/api/public-booking/appointments/*/cancel', async route => {
      bodies.cancel.push(route.request().postDataJSON())
      if (loseCancel) { loseCancel = false; await route.fetch(); return route.abort('failed') }
      if (bodies.cancel.length === 2) { const headers = { ...route.request().headers() }; delete headers['x-csrf-token']; return route.continue({ headers }) }
      return route.continue()
    })
    async function capture(name) {
      for (const width of [320, 390, 768, 1280]) {
        await page.setViewportSize({ width, height: 1000 })
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${name} overflow ${width}`)
        const file = `${name}-${width}.png`
        await page.screenshot({ path: path.join(out, file), fullPage: true }); screenshots.push(file)
      }
    }
    await page.goto(origin + '/randevu'); await page.getByRole('radio').first().waitFor(); await capture('service')
    await page.getByRole('radio').first().check(); await page.getByRole('button', { name: 'Devam et', exact: true }).click()
    await page.getByRole('radio').first().check(); await page.getByRole('button', { name: 'Devam et', exact: true }).click()
    const tomorrow = new Date(Date.now() + 86400000)
    const day = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Istanbul', year: 'numeric', month: '2-digit', day: '2-digit' }).format(tomorrow)
    await page.getByLabel('Tarih seçin').fill(day); await page.getByRole('radio').first().waitFor(); await page.getByRole('radio').first().check(); await capture('time')
    await page.getByRole('button', { name: 'Devam et', exact: true }).click()
    await page.getByLabel('Adınız ve soyadınız').fill('Sentetik müşteri'); await page.getByLabel('Cep telefonu', { exact: true }).fill('05000000001')
    await page.getByRole('button', { name: 'Doğrulama kodu iste', exact: true }).click(); await page.getByLabel('Doğrulama kodu', { exact: true }).waitFor(); await capture('otp')
    const smsResponse = await context.request.get(control + '/sms'); assert(smsResponse.ok())
    const sms = await smsResponse.json(); assert.match(sms.code, /^\d{6}$/)
    await page.getByLabel('Doğrulama kodu', { exact: true }).fill(sms.code); await page.getByRole('button', { name: 'Kodu doğrula', exact: true }).click()
    await page.getByText('Telefon doğrulandı. Talebiniz henüz gönderilmedi.').waitFor(); await capture('verified')
    assert.equal((await (await context.request.get(control + '/state')).json()).appointments, 0)
    await page.getByRole('button', { name: 'Randevu talebi gönder', exact: true }).click()
    await page.getByRole('button', { name: 'Aynı talebi yeniden dene', exact: true }).waitFor(); await capture('create-unknown')
    for (let attempt = 0; attempt < 25; attempt++) {
      const response = await context.request.post(origin + '/api/public-booking/appointments', { data: {}, headers: { Origin: origin } })
      if (response.status() === 429) break
      assert.equal(response.status(), 400)
      assert(attempt < 24, 'Real shared request limit should reject before 25 attempts')
    }
    await page.getByRole('button', { name: 'Aynı talebi yeniden dene', exact: true }).click()
    await page.getByRole('alert').filter({ hasText: 'Çok sık denendi' }).waitFor(); assert(await page.getByRole('button', { name: 'Hizmeti değiştir', exact: true }).isDisabled()); await capture('create-retry-limited')
    await page.waitForFunction(() => [...document.querySelectorAll('button')].some(button => button.textContent === 'Aynı talebi yeniden dene' && !button.disabled), undefined, { timeout: 75000 })
    await page.getByRole('button', { name: 'Aynı talebi yeniden dene', exact: true }).click()
    await page.getByRole('heading', { name: 'İşletme onayı bekleniyor', exact: true }).waitFor(); await page.getByRole('button', { name: 'Sonuç bağlantısını kopyala', exact: true }).waitFor(); await capture('pending')
    assert.deepEqual(bodies.create[0], bodies.create[1]); assert.deepEqual(bodies.create[1], bodies.create[2]); assert.equal((await (await context.request.get(control + '/state')).json()).appointments, 1)
    await page.getByRole('button', { name: 'Sonuç bağlantısını kopyala', exact: true }).click()
    const fallback = page.getByLabel('Kopyalanacak sonuç bağlantısı')
    let link
    if (await fallback.isVisible()) link = await fallback.inputValue()
    else { await context.grantPermissions(['clipboard-read'], { origin }); link = await page.evaluate(() => navigator.clipboard.readText()) }
    assert(link.includes('#erisim=')); assert.equal(new URL(page.url()).hash, '')
    const storage = await page.evaluate(() => ({ local: Object.keys(localStorage), session: Object.keys(sessionStorage) })); assert.deepEqual(storage, { local: [], session: [] })
    await page.getByRole('button', { name: 'Randevuyu iptal et', exact: true }).click(); await capture('cancel-confirm')
    assert.equal((await (await context.request.get(control + '/state')).json()).cancellations, 0)
    await page.getByRole('button', { name: 'Evet, randevuyu iptal et', exact: true }).click(); await page.getByRole('button', { name: 'Aynı iptali yeniden dene', exact: true }).waitFor(); await page.getByRole('button', { name: 'Aynı iptali yeniden dene', exact: true }).click()
    await page.getByRole('alert').filter({ hasText: 'işlem uygulanmadı' }).waitFor(); await capture('cancel-retry-csrf'); await page.getByRole('button', { name: 'Aynı iptali yeniden dene', exact: true }).click()
    await page.getByRole('heading', { name: 'Randevu iptal edildi', exact: true }).waitFor(); await capture('cancelled')
    assert.deepEqual(bodies.cancel[0], bodies.cancel[1]); assert.deepEqual(bodies.cancel[1], bodies.cancel[2]); assert.equal((await (await context.request.get(control + '/state')).json()).cancellations, 1)
    await page.goto(link); await page.getByRole('heading', { name: 'Randevu iptal edildi', exact: true }).waitFor(); assert.equal(new URL(page.url()).hash, ''); await capture('reopened')
    const wrong = new URL(link); wrong.hash = 'erisim=' + 'x'.repeat(43)
    await page.goto(wrong.href); await page.getByRole('alert').filter({ hasText: 'Bağlantı geçersiz veya süresi dolmuş' }).waitFor(); assert.equal(await page.getByRole('heading', { name: 'İşletme onayı bekleniyor', exact: true }).count(), 0); await capture('forbidden')
    await context.request.post(control + '/confirmed')
    await page.goto(origin + '/randevu'); await page.getByRole('radio').first().check(); await page.getByRole('button', { name: 'Devam et', exact: true }).click()
    await page.getByRole('radio').first().check(); await page.getByRole('button', { name: 'Devam et', exact: true }).click(); await page.getByLabel('Tarih seçin').fill(day)
    await page.getByRole('radio').first().check(); await page.getByRole('button', { name: 'Devam et', exact: true }).click()
    await page.getByLabel('Adınız ve soyadınız').fill('Sentetik müşteri iki'); await page.getByLabel('Cep telefonu', { exact: true }).fill('05000000002')
    await page.getByRole('button', { name: 'Doğrulama kodu iste', exact: true }).click(); await page.getByLabel('Doğrulama kodu', { exact: true }).waitFor()
    const secondSms = await (await context.request.get(control + '/sms')).json()
    await page.getByLabel('Doğrulama kodu', { exact: true }).fill(secondSms.code); await page.getByRole('button', { name: 'Kodu doğrula', exact: true }).click()
    await page.getByRole('button', { name: 'Randevu talebi gönder', exact: true }).click(); await page.getByRole('heading', { name: 'Randevunuz onaylandı', exact: true }).waitFor(); await capture('confirmed')
    assert.equal((await (await context.request.get(control + '/state')).json()).appointments, 2)
    const head = await context.request.get(origin + '/randevu'); assert.equal(head.headers()['cache-control'], 'no-store'); assert.equal(head.headers()['referrer-policy'], 'no-referrer')
    assert.deepEqual(errors, [])
    fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ pass: true, widths: [320,390,768,1280], screenshots, api: 'real Kestrel/PostgreSQL, fake SMS', lostCreateAndCancel: true, realRateAndCsrfRetries: true, singleRecordAndCancellation: true, fragmentRemoved: true, storageEmpty: true, pageErrors: errors }, null, 2))
    console.log(JSON.stringify({ pass: true, screenshots: screenshots.length }))
  } finally { await browser.close() }
})().catch(error => { console.error(error); process.exitCode = 1 })
