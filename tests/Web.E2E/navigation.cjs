const { chromium } = require('playwright')
const assert = require('node:assert/strict')
const crypto = require('node:crypto')
const fs = require('node:fs')
const path = require('node:path')

const origin = process.env.NAVIGATION_TEST_ORIGIN || 'http://127.0.0.1:8092'
if (origin !== 'http://127.0.0.1:8092' && !(process.env.CI === 'true' && origin === 'http://127.0.0.1:8080')) {
  throw new Error('Yalnız ayrı yerel sentetik ortam veya geçici CI ortamı kabul edilir.')
}
const out = path.resolve(__dirname, process.env.MINIMAL_TEST === 'true' ? '../../.local/minimal-navigation/browser' : process.env.MENU_TEST === 'true' ? '../../.local/menu-login/browser' : process.env.T04_TEST === 'true' ? '../../.local/t04/browser' : '../../.local/url-navigation/browser')
fs.mkdirSync(out, { recursive: true })
const widths = [320, 390, 768, 1280], states = [], errors = [], assets = {}, failedAssets = []
function totp(secret) {
  let bits = 0, count = 0; const bytes = []
  for (const ch of secret) {
    bits = (bits << 5) | 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'.indexOf(ch); count += 5
    if (count >= 8) { count -= 8; bytes.push((bits >> count) & 255) }
  }
  const counter = Buffer.alloc(8); counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)))
  const hash = crypto.createHmac('sha1', Buffer.from(bytes)).update(counter).digest(), offset = hash[19] & 15
  return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1000000).padStart(6, '0')
}
async function post(context, endpoint, body) {
  const csrf = await context.request.get(origin + '/api/auth/csrf'); assert.equal(csrf.status(), 200)
  return context.request.post(origin + endpoint, { data: body, headers: { 'X-CSRF-TOKEN': (await csrf.json()).token } })
}
async function check(page, state) {
  if (!['conflict-preserved', 'verification-invalid'].includes(state)) assert.equal(await page.locator('p[role="alert"]:visible').count(), 0, `${state} unexpected error`)
  for (const width of widths) {
    await page.setViewportSize({ width, height: 1000 }); await page.evaluate(() => document.fonts.ready)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${state} overflow ${width}`)
    const controls = await page.evaluate(() => [...document.querySelectorAll('button,a[data-navigation]')].filter(item => item.getClientRects().length).map(item => {
      const rect = item.getBoundingClientRect(), css = getComputedStyle(item)
      const canvas = document.createElement('canvas'), ctx = canvas.getContext('2d')
      function luminance(color) {
        ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = color; ctx.fillRect(0, 0, 1, 1)
        const values = [...ctx.getImageData(0, 0, 1, 1).data].slice(0, 3).map(n => { n /= 255; return n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4 })
        return .2126 * values[0] + .7152 * values[1] + .0722 * values[2]
      }
      let background = css.backgroundColor, ancestor = item.parentElement
      while ((background === 'transparent' || /rgba\([^)]*,\s*0\)$/.test(background)) && ancestor) {
        background = getComputedStyle(ancestor).backgroundColor; ancestor = ancestor.parentElement
      }
      const ls = [luminance(css.color), luminance(background)].sort((a, b) => b - a)
      const navigationWeight = item.closest('#management-navigation') && ['500', '600'].includes(css.fontWeight)
      const paginationWeight = item.closest('nav[aria-label$="sayfaları"]') && ['500', '700'].includes(css.fontWeight)
      return { target: rect.width >= 44 && rect.height >= 44, contrast: (ls[0] + .05) / (ls[1] + .05), disabled: item.matches(':disabled,[aria-disabled="true"]'), compact: !!navigationWeight || !!paginationWeight || !!item.closest('[data-login-buttons]') || (css.fontSize === '14px' && css.fontWeight === '600') }
    }))
    assert.deepEqual(controls.filter(item => !item.target || (!item.disabled && item.contrast < 4.5)), [], `${state} targets/contrast ${width}`)
    if (process.env.T04_TEST === 'true') assert.deepEqual(controls.filter(item => !item.compact), [], `${state} compact buttons ${width}`)
    await page.screenshot({ path: path.join(out, `${state}-${width}.png`), fullPage: true })
  }
  await page.setViewportSize({ width: 1280, height: 1000 }); states.push(state)
}
async function heading(page, text) { await page.getByRole('heading', { name: text, exact: true, level: 1 }).waitFor() }
async function ready(page) {
  await page.waitForFunction(() => {
    const control = document.querySelector('header button')
    return control && !control.disabled && ![...document.querySelectorAll('main [aria-busy="true"],main [role="status"]')].some(node =>
      node.getClientRects().length && (node.getAttribute('aria-busy') === 'true' || /yükleniyor|alınıyor/i.test(node.textContent)))
  })
}
async function main() {
  const browser = await chromium.launch(process.env.CI === 'true' ? { headless: true } : { headless: true, channel: 'chrome' })
  let activePage
  try {
    const seed = await browser.newContext(), ctx = await browser.newContext(), page = await ctx.newPage()
    activePage = page
    page.on('pageerror', error => errors.push(error.message))
    page.on('response', response => {
      if (response.url().includes('/assets/')) {
        if (![200, 304].includes(response.status())) failedAssets.push({ url: response.url(), status: response.status() })
        assets[new URL(response.url()).pathname] = true
      }
    })
    assert.equal((await post(seed, '/api/auth/login', { email: 'owner@example.test', password: 'Synthetic!Owner123' })).status(), 204)
    const setup = await post(seed, '/api/auth/mfa/setup', { password: 'Synthetic!Owner123' }); assert.equal(setup.status(), 200)
    const key = (await setup.json()).key
    assert.equal((await post(seed, '/api/auth/mfa/enable', { password: 'Synthetic!Owner123', code: totp(key) })).status(), 200)
    assert.equal((await post(seed, '/api/auth/logout', {})).status(), 204)
    assert.equal((await post(seed, '/api/auth/login', { email: 'owner@example.test', password: 'Synthetic!Owner123' })).status(), 202)
    assert.equal((await post(seed, '/api/auth/mfa/login', { code: totp(key) })).status(), 204)
    const serviceResponse = await post(seed, '/api/services/', { id: crypto.randomUUID(), name: 'Sentetik hizmet', durationMinutes: 30, price: '350.00' })
    assert.equal(serviceResponse.status(), 201); const service = await serviceResponse.json()
    let member
    for (let i = 0; i < 21; i++) {
      const response = await post(seed, '/api/staff-members/', { id: crypto.randomUUID(), name: `Sentetik kişi ${String(i).padStart(2, '0')}` })
      assert.equal(response.status(), 201); member = await response.json()
    }
    console.log('Kayıt hazırlığı: gerçek istek sınırı penceresi bekleniyor.'); await page.waitForTimeout(30500); await page.waitForTimeout(30500)
    console.log('Sentetik kayıtlar hazır; tarayıcı akışları başladı.')
    await page.goto(origin + '/yonetim/hizmetler'); await heading(page, 'İşletme girişi'); await check(page, 'login-deep-link')
    assert.equal(new URL(page.url()).searchParams.get('donus'), '/yonetim/hizmetler')
    await page.getByLabel('E-posta', { exact: true }).fill('owner@example.test'); await page.getByLabel('Parola', { exact: true }).fill('Synthetic!Owner123')
    await page.getByRole('button', { name: 'Giriş yap', exact: true }).click(); await heading(page, 'İkinci adımı tamamlayın'); await check(page, 'mfa-return')
    await page.getByLabel('Doğrulayıcı uygulama kodu', { exact: true }).fill(totp(key)); await page.getByRole('button', { name: 'Doğrula', exact: true }).click()
    await heading(page, 'Hizmetler'); await ready(page); assert.equal(new URL(page.url()).pathname, '/yonetim/hizmetler'); await check(page, 'services')
    await page.setViewportSize({ width: 320, height: 1000 }); await page.getByRole('button', { name: 'Menü', exact: true }).click(); await check(page, 'mobile-menu')
    await page.setViewportSize({ width: 320, height: 1000 }); await page.getByRole('link', { name: 'Ekip', exact: true }).click(); await heading(page, 'Personel'); await ready(page)
    assert.equal(await page.getByRole('button', { name: 'Menü', exact: true }).getAttribute('aria-expanded'), 'false'); await page.setViewportSize({ width: 1280, height: 1000 })
    await page.goBack(); await heading(page, 'Hizmetler'); await ready(page); await page.goForward(); await heading(page, 'Personel'); await ready(page)
    await page.goto(origin + '/yonetim/personel?sayfa=2'); await page.getByRole('button', { name: 'Sayfa 2', exact: true }).waitFor()
    await page.getByRole('link', { name: /için ayrıntılar$/ }).first().click(); await ready(page); assert.equal(new URL(page.url()).search, '?sayfa=2')
    await page.getByRole('link', { name: 'Personel listesine dön', exact: true }).click(); await page.getByRole('button', { name: 'Sayfa 2', exact: true }).waitFor()
    await ready(page); await check(page, 'personnel-page-two')
    for (const [url, title, state] of [
      ['/yonetim/isletme', 'İşletme bilgileri', 'business'], ['/yonetim/isletme/saatler', 'İşletme saatleri', 'business-hours'],
      ['/yonetim/personel/yeni', 'Personel', 'personnel-create'], [`/yonetim/personel/${member.id}`, 'Personel', 'personnel-detail'],
      [`/yonetim/personel/${member.id}/hizmetler`, 'Personel', 'personnel-services'], [`/yonetim/personel/${member.id}/saatler`, 'Personel', 'personnel-hours'],
      ['/yonetim/hizmetler/yeni', 'Yeni hizmet', 'service-create'], [`/yonetim/hizmetler/${service.id}/duzenle`, 'Sentetik hizmet', 'service-edit'],
      ['/yonetim/calisan-erisimleri', 'Çalışan erişimleri', 'access'], ['/yonetim/hesap', 'Hesap ve güvenlik', 'account'],
      ['/yonetim/degisiklik-kayitlari', 'Değişiklik kayıtları', 'audit'], ['/yonetim/olmayan', 'Sayfa bulunamadı', 'missing'],
    ]) {
      await page.goto(origin + url); await heading(page, title)
      await ready(page)
      if (state === 'service-edit') await page.getByLabel('Hizmet adı (zorunlu)').waitFor()
      await page.reload(); await heading(page, title); await ready(page)
      if (process.env.MENU_TEST === 'true') {
        if (state === 'personnel-detail') assert.equal(await page.locator('#member-name').evaluate(node => node === document.activeElement), false)
        if (['business', 'service-edit', 'business-hours', 'personnel-services', 'personnel-hours'].includes(state)) assert.equal(await page.getByRole('button', { name: /^Güncel .* yükle$/ }).count(), 0)
        if (state === 'access') assert.equal(await page.getByRole('button', { name: 'Davetleri yenile', exact: true }).count(), 0)
        if (state === 'account') {
          assert.equal(await page.getByRole('button', { name: 'Durumu yenile', exact: true }).count(), 0)
          const emailRead = page.waitForResponse(response => response.url().endsWith('/api/auth/recovery-email/') && response.request().method() === 'GET')
          await page.evaluate(() => window.dispatchEvent(new Event('focus')))
          assert.equal((await emailRead).status(), 200)
        }
      }
      await check(page, state)
    }
    await page.goto(origin + '/yonetim/isletme'); await heading(page, 'İşletme bilgileri'); await ready(page)
    if (process.env.MENU_TEST === 'true') {
      await page.locator('#business-name').fill('Sentetik Berber')
      const profileSaved = page.waitForResponse(response => response.url().endsWith('/api/business-profile/') && response.request().method() === 'POST')
      await page.getByRole('button', { name: 'Profili kaydet', exact: true }).click(); assert.equal((await profileSaved).status(), 200)
      await ready(page)
      assert.match(await page.locator('header').innerText(), /Sentetik Berber.*●.*İşletme Sahibi/s)
      await check(page, 'business-saved-header')
      for (const label of ['İşletme', 'Ekip', 'Hesap']) {
        await page.getByRole('link', { name: 'Değişiklik kayıtları', exact: true }).click(); await heading(page, 'Değişiklik kayıtları'); await ready(page)
        assert.equal(await page.locator('[id^="management-context-"]:visible').count().then(count => count > 0), false)
        await page.getByRole('link', { name: label, exact: true }).click(); await ready(page)
        assert.equal(await page.locator('[id^="management-context-"]:visible').count().then(count => count > 0), true, label + ' first click after audit')
      }
      await page.goto(origin + '/yonetim/isletme'); await heading(page, 'İşletme bilgileri'); await ready(page)
      const group = page.getByRole('link', { name: 'İşletme', exact: true })
      await group.click(); assert.equal(await group.getAttribute('aria-expanded'), 'false')
      assert.equal(await page.locator('[id^="management-context-"]:visible').count().then(count => count > 0), false)
      await group.focus(); await group.press('Enter'); assert.equal(await group.getAttribute('aria-expanded'), 'true')
      await check(page, 'menu-open')
      await page.getByRole('button', { name: 'Yan menüyü kapat', exact: true }).click()
      assert.equal(await page.locator('[id^="management-context-"]:visible').count().then(count => count > 0), false)
      assert.equal(await group.evaluate(node => node === document.activeElement), true)
      await check(page, 'menu-closed')
      await group.click()
      await page.setViewportSize({ width: 390, height: 1000 })
      const mobile = page.getByRole('button', { name: 'Menü', exact: true })
      await mobile.click()
      await group.click(); assert.equal(await group.getAttribute('aria-expanded'), 'false')
      await group.click(); assert.equal(await group.getAttribute('aria-expanded'), 'true')
      await page.getByRole('button', { name: 'Yan menüyü kapat', exact: true }).click()
      assert.equal(await mobile.getAttribute('aria-expanded'), 'false')
      assert.equal(await mobile.evaluate(node => node === document.activeElement), true)
      await page.setViewportSize({ width: 1280, height: 1000 }); await group.click()
    }
    await page.locator('#business-name').fill('Korunan işletme taslağı'); await page.getByRole('link', { name: 'Ekip', exact: true }).click(); await heading(page, 'Personel'); await ready(page)
    await page.getByRole('link', { name: 'İşletme', exact: true }).click(); await heading(page, 'İşletme bilgileri'); await ready(page)
    assert.equal(await page.locator('#business-name').inputValue(), 'Korunan işletme taslağı'); await check(page, 'profile-draft-preserved'); await page.locator('#business-name').fill('')
    await page.goto(origin + '/yonetim/hizmetler'); await page.getByRole('link', { name: 'Yeni hizmet', exact: true }).click()
    await page.getByLabel('Hizmet adı (zorunlu)').fill('Taslak hizmet')
    let dialogs = 0; const reject = async dialog => { dialogs++; await dialog.dismiss() }; page.on('dialog', reject)
    await page.evaluate(() => history.back()); await page.waitForTimeout(250)
    assert.equal(new URL(page.url()).pathname, '/yonetim/hizmetler/yeni'); assert.equal(await page.getByLabel('Hizmet adı (zorunlu)').inputValue(), 'Taslak hizmet'); assert.equal(dialogs, 1)
    await check(page, 'dirty-history-rejected'); page.off('dialog', reject)
    page.once('dialog', dialog => dialog.accept()); await page.evaluate(() => history.back()); await page.waitForURL(origin + '/yonetim/hizmetler'); await heading(page, 'Hizmetler')
    await page.getByRole('link', { name: 'Yeni hizmet', exact: true }).waitFor(); assert.equal(new URL(page.url()).pathname, '/yonetim/hizmetler')
    await page.getByRole('link', { name: 'Yeni hizmet', exact: true }).click()
    await page.getByLabel('Hizmet adı (zorunlu)').fill('Sentetik kayıt'); await page.getByLabel('Süre (dakika, zorunlu)').fill('30'); await page.getByLabel('Fiyat (TL, zorunlu)').fill('350')
    let release; const pending = new Promise(resolve => { release = resolve })
    await page.route('**/api/services/', async route => { if (route.request().method() === 'POST') await pending; await route.continue() })
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click(); await page.getByRole('button', { name: 'İşlem sürüyor…', exact: true }).waitFor()
    await page.evaluate(() => history.back()); await page.waitForTimeout(250); assert.equal(new URL(page.url()).pathname, '/yonetim/hizmetler/yeni')
    release(); await page.getByText('Hizmet kaydedildi.', { exact: true }).waitFor(); assert.equal(new URL(page.url()).pathname, '/yonetim/hizmetler'); await page.unroute('**/api/services/')
    await check(page, 'saved')
    await page.goto(origin + `/yonetim/hizmetler/${service.id}/duzenle`); await page.getByLabel('Hizmet adı (zorunlu)').waitFor()
    assert.equal((await post(seed, `/api/services/${service.id}`, { name: 'Güncel sentetik hizmet', durationMinutes: 30, price: '350.00', version: service.version })).status(), 200)
    await page.getByLabel('Hizmet adı (zorunlu)').fill('Korunan taslak')
    const conflict = page.waitForResponse(response => response.url().endsWith(`/api/services/${service.id}`) && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Kaydet', exact: true }).click(); assert.equal((await conflict).status(), 409)
    await page.getByRole('button', { name: 'Güncel kaydı yükle', exact: true }).waitFor(); assert.equal(await page.getByLabel('Hizmet adı (zorunlu)').inputValue(), 'Korunan taslak')
    await check(page, 'conflict-preserved'); page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Güncel kaydı yükle', exact: true }).click()
    await page.waitForFunction(() => document.querySelector('#service-name')?.value === 'Güncel sentetik hizmet')
    if (process.env.T04_TEST === 'true') {
      assert.equal(await page.locator('main ul').count(), 0)
      await page.getByLabel('Fiyat (TL, zorunlu)').fill('400')
      assert.equal(await page.getByRole('button', { name: 'Sil', exact: true }).isDisabled(), true)
      assert.equal(await page.getByRole('button', { name: 'Pasifleştir', exact: true }).isDisabled(), true)
      await check(page, 'service-dirty')
      await page.getByRole('button', { name: 'Kaydet', exact: true }).click(); await heading(page, 'Hizmetler'); await ready(page)
      await page.goto(origin + `/yonetim/hizmetler/${service.id}/duzenle`); await heading(page, 'Güncel sentetik hizmet'); await ready(page)
      await page.getByRole('button', { name: 'Pasifleştir', exact: true }).click(); await check(page, 'service-status-confirm')
      await page.getByRole('group', { name: 'Hizmet durum değişikliği onayı' }).getByRole('button', { name: 'Vazgeç', exact: true }).click()
      assert.equal(await page.getByRole('button', { name: 'Pasifleştir', exact: true }).evaluate(node => node === document.activeElement), true)
      await page.getByRole('button', { name: 'Pasifleştir', exact: true }).click(); await page.getByRole('button', { name: 'Durumu değiştir', exact: true }).click()
      await page.getByText('Güncel sentetik hizmet pasifleştirildi.', { exact: true }).waitFor(); await check(page, 'service-status-saved')
      const canonical = await (await seed.request.get(origin + `/api/services/${service.id}`)).json()
      let sentVersion
      page.on('request', request => { if (request.method() === 'POST' && request.url() === origin + `/api/services/${service.id}`) sentVersion = request.postDataJSON().version })
      await page.getByLabel('Fiyat (TL, zorunlu)').fill('401'); await page.getByRole('button', { name: 'Kaydet', exact: true }).click(); await heading(page, 'Hizmetler'); await ready(page)
      assert.equal(sentVersion, canonical.version)
      await page.goto(origin + `/yonetim/hizmetler/${service.id}/duzenle`); await heading(page, 'Güncel sentetik hizmet'); await ready(page)
      await page.getByRole('button', { name: 'Sil', exact: true }).click(); await check(page, 'service-delete-confirm')
      const deletion = page.waitForResponse(response => response.url().endsWith(`/api/services/${service.id}/delete`))
      await page.getByRole('button', { name: 'Hizmeti sil', exact: true }).click(); assert.equal((await deletion).status(), 204)
      await heading(page, 'Hizmetler'); await ready(page); await check(page, 'service-deleted')
      assert.equal((await seed.request.get(origin + `/api/services/${service.id}`)).status(), 404)
    }
    assert.equal((await post(seed, '/api/services/', { id: crypto.randomUUID(), name: '', durationMinutes: 0, price: '-1.00' })).status(), 400)
    const tab = await ctx.newPage(); await tab.goto(origin + `/yonetim/personel/${member.id}/hizmetler`); await heading(tab, 'Personel'); await tab.getByRole('link', { name: 'Hizmetler', exact: true }).last().waitFor(); await tab.close()
    await page.getByRole('button', { name: 'Çıkış yap', exact: true }).first().click(); await heading(page, 'İşletme girişi'); await page.goBack(); await heading(page, 'İşletme girişi')
    for (const [url, title, state] of [
      ['/parola-yenile', 'Parolanızı yenileyin', 'reset-request'], ['/parola-yenile/kod', 'İşletme sahibi parola sıfırlama', 'reset-code'],
      ['/calisan/parola-yenile', 'Çalışan parola sıfırlama', 'staff-reset'], ['/calisan/davet-kabul', 'Çalışan davetini kabul et', 'invitation'],
    ]) { await page.goto(origin + url); await heading(page, title); await page.waitForLoadState('networkidle'); await check(page, state) }
    await page.goto(origin + '/#verify-owner-email=synthetic-invalid-proof'); await heading(page, 'Hesap e-postasını doğrula')
    assert.equal(new URL(page.url()).hash, ''); assert.equal(new URL(page.url()).pathname, '/eposta-dogrula')
    const verification = page.waitForResponse(response => response.url().endsWith('/api/auth/recovery-email/confirm'))
    await page.getByRole('button', { name: 'E-postamı doğrula', exact: true }).click(); assert.equal((await verification).status(), 400)
    await check(page, 'verification-invalid'); await page.getByRole('button', { name: 'Hesaba dön', exact: true }).click(); await heading(page, 'İşletme girişi')
    await page.goto(origin + '/eposta-dogrula'); await heading(page, 'Doğrulama bağlantısı gerekli')
    const invitation = await post(seed, '/api/staff-invitations/', { email: 'staff@example.test', verifiedRecipient: true }); assert.equal(invitation.status(), 200)
    assert.equal((await post(ctx, '/api/staff-invitations/accept', { email: 'staff@example.test', token: (await invitation.json()).token, password: 'Synthetic!Staff123', confirmPassword: 'Synthetic!Staff123' })).status(), 204)
    if (process.env.MINIMAL_TEST === 'true') {
      const ownerPage = await seed.newPage(); ownerPage.on('pageerror', error => errors.push(error.message))
      activePage = ownerPage
      await require('./minimal-navigation.cjs')({ page: ownerPage, seed, member, origin, post, ready, heading, check })
      await ownerPage.close(); activePage = page
    }
    assert.equal((await post(ctx, '/api/auth/login', { email: 'staff@example.test', password: 'Synthetic!Staff123' })).status(), 204)
    await page.goto(origin + '/yonetim/hizmetler'); await heading(page, 'Erişim izni yok'); assert.equal((await ctx.request.get(origin + '/api/services/')).status(), 403); await check(page, 'staff-forbidden')
    await page.goto(origin + '/yonetim/hesap'); await heading(page, 'Hesap ve güvenlik'); await check(page, 'staff-account')
    assert.equal((await post(ctx, '/api/auth/logout', {})).status(), 204); assert.equal((await ctx.request.get(origin + '/api/services/')).status(), 401)
    assert.equal((await ctx.request.get(origin + '/api/olmayan')).status(), 404)
    assert.equal((await ctx.request.get(origin + '/health/ready')).status(), 200)
    const html = await (await ctx.request.get(origin + '/yonetim/hizmetler/yeni')).text()
    assert.match(html, /src="\/assets\//); assert.match(html, /href="\/assets\//)
    if (process.env.T04_TEST === 'true') {
      for (const asset of Object.keys(assets)) assert.equal((await ctx.request.get(origin + asset)).status(), 200, asset)
    }
    let limited = false
    for (let attempt = 0; attempt < 11; attempt++) {
      const response = await post(ctx, '/api/auth/login', { email: `missing-${attempt}@example.test`, password: 'Synthetic!Invalid123' })
      if (response.status() === 429) { limited = true; break }
      assert.equal(response.status(), 401)
    }
    assert.equal(limited, true); assert.deepEqual(failedAssets, [])
    assert.deepEqual(errors, [])
    fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ pass: true, realAPI: true, states, widths, assets: Object.keys(assets), pageErrors: errors, history: true, refresh: true, dirtyGuard: true, busyGuard: true, roleGuard: true, errors: [400, 401, 403, 404, 409, 429] }, null, 2))
    console.log('Gerçek API gezinme kabulü geçti: ' + states.length + ' durum × dört genişlik.')
  } catch (error) {
    if (activePage) {
      console.error('Başarısız adımın ekranı: ' + new URL(activePage.url()).pathname)
      await activePage.screenshot({ path: path.join(out, 'failure.png'), fullPage: true })
    }
    throw error
  } finally { await browser.close() }
}
main().catch(error => { console.error(error.stack); process.exitCode = 1 })
