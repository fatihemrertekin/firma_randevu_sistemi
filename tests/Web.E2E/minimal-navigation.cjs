const assert = require('node:assert/strict')
const crypto = require('node:crypto')
const { execFileSync } = require('node:child_process')

// Yalnız bağımsız, sentetik yerel kabul ortamında çağrılır.
module.exports = async ({ page, seed, member, origin, post, ready, heading, check }) => {
  assert.equal(origin, 'http://127.0.0.1:8092')
  const waitWindow = async () => { await page.waitForTimeout(30500); await page.waitForTimeout(30500) }
  // Önceki kabul akışının gerçek 30 istek/dakika penceresi sona ersin.
  await waitWindow()
  const compose = '.local/minimal-navigation/compose.yaml'
  const accountSeed = `INSERT INTO "AspNetUsers" SELECT (jsonb_populate_record(NULL::"AspNetUsers", to_jsonb(u) ||
    jsonb_build_object('Id', gen_random_uuid(), 'Email', 'pager-' || n || '@example.test', 'NormalizedEmail', 'PAGER-' || n || '@EXAMPLE.TEST',
    'UserName', 'pager-' || n || '@example.test', 'NormalizedUserName', 'PAGER-' || n || '@EXAMPLE.TEST',
    'SecurityStamp', gen_random_uuid()::text, 'ConcurrencyStamp', gen_random_uuid()::text))).*
    FROM "AspNetUsers" u CROSS JOIN generate_series(1, 12) n WHERE u."Email" = 'staff@example.test';
    INSERT INTO "AspNetUserRoles" SELECT u."Id", r."Id" FROM "AspNetUsers" u CROSS JOIN "AspNetRoles" r
    WHERE u."Email" LIKE 'pager-%@example.test' AND r."Name" = 'Staff';`
  execFileSync('docker', ['compose', '-f', compose, 'exec', '-T', 'db', 'psql', '-U', 'postgres', '-d', 'auth_design', '-v', 'ON_ERROR_STOP=1'],
    { input: accountSeed, stdio: ['pipe', 'pipe', 'pipe'] })
  for (let i = 0; i < 21; i++) {
    const response = await post(seed, '/api/services/', { id: crypto.randomUUID(), name: `Seçim hizmeti ${String(i).padStart(2, '0')}`, durationMinutes: 30, price: '350.00' })
    assert.equal(response.status(), 201)
  }
  // Hazırlama POST'ları ile kullanıcı gezinmesini aynı pencereye yığma.
  await waitWindow()
  for (const [url, title, label] of [
    ['/yonetim/personel', 'Personel', 'Personel sayfaları'],
    ['/yonetim/hizmetler', 'Hizmetler', 'Hizmet sayfaları'],
    ['/yonetim/calisan-erisimleri', 'Çalışan erişimleri', 'Çalışan hesabı sayfaları'],
  ]) {
    await page.goto(origin + url + '?boyut=10'); await heading(page, title); await ready(page)
    const pager = page.getByRole('navigation', { name: label, exact: true })
    await pager.getByRole('button', { name: 'Son sayfa', exact: true }).click(); await ready(page)
    assert.ok(Number(new URL(page.url()).searchParams.get('sayfa')) > 1)
    const lastURL = page.url(); await page.reload(); await ready(page); assert.equal(page.url(), lastURL)
    await pager.getByRole('button', { name: 'İlk sayfa', exact: true }).click(); await ready(page)
    await page.goBack(); await ready(page); assert.equal(page.url(), lastURL)
    await page.goForward(); await ready(page); assert.equal(new URL(page.url()).searchParams.get('sayfa'), null)
    await pager.getByLabel('Sayfadaki kayıt sayısı').selectOption('20'); await ready(page)
    assert.equal(new URL(page.url()).searchParams.get('boyut'), null)
    if (await pager.getByLabel('Sayfadaki kayıt sayısı').count()) {
      await pager.getByLabel('Sayfadaki kayıt sayısı').selectOption('50'); await ready(page)
      assert.equal(new URL(page.url()).searchParams.get('boyut'), '50')
      assert.equal(await pager.getByRole('button', { name: 'Sonraki sayfa', exact: true }).count(), 0)
    }
    await page.goto(origin + url + '?boyut=10&sayfa=999'); await ready(page)
    assert.notEqual(new URL(page.url()).searchParams.get('sayfa'), '999')
    await check(page, title === 'Personel' ? 'minimal-personnel-pagination' : title === 'Hizmetler' ? 'minimal-services-pagination' : 'minimal-accounts-pagination')
  }
  // Personel ve hesap okuması aynı yönetim penceresini paylaşır.
  await waitWindow()
  await page.goto(origin + `/yonetim/personel/${member.id}/hizmetler?boyut=10&sayfa=2`); await ready(page)
  const back = page.getByRole('link', { name: 'Personel listesine dön', exact: true })
  await back.hover()
  const hover = await back.evaluate(node => { const css = getComputedStyle(node); return { foreground: css.color, background: css.backgroundColor } })
  assert.notEqual(hover.foreground, hover.background)
  await check(page, 'minimal-back-hover')
  const choices = page.getByRole('group', { name: 'Hizmet seçimleri', exact: true })
  const columns = await choices.evaluate(node => new Set([...node.querySelectorAll('label')].map(label => Math.round(label.getBoundingClientRect().left))).size)
  assert.ok(columns >= 2, 'desktop service selection uses available width')
  await choices.getByRole('checkbox').first().check()
  const firstName = await choices.getByRole('checkbox').first().getAttribute('aria-label')
  await page.getByRole('navigation', { name: 'Personel hizmet sayfaları', exact: true }).getByRole('button', { name: 'Sonraki sayfa', exact: true }).click(); await ready(page)
  assert.match(await page.locator('#assignment-info').innerText(), /Seçili: 1 hizmet/)
  await page.getByLabel('Sayfadaki kayıt sayısı').selectOption('20'); await ready(page)
  assert.equal(await page.getByRole('checkbox', { name: firstName, exact: true }).isChecked(), true)
  await check(page, 'minimal-service-selection-grid')
  let rejected = 0
  const reject = async dialog => { rejected++; await dialog.dismiss() }; page.on('dialog', reject)
  await page.getByRole('link', { name: 'Bilgiler', exact: true }).click()
  assert.equal(rejected, 1); assert.ok(new URL(page.url()).pathname.endsWith('/hizmetler')); page.off('dialog', reject)
  await page.getByRole('button', { name: 'Seçimleri kaydet', exact: true }).click(); await ready(page)
  const saved = await (await seed.request.get(origin + `/api/staff-members/${member.id}/services`)).json()
  assert.equal(saved.selected.length, 1)
  await back.click(); await ready(page); assert.equal(new URL(page.url()).search, '?sayfa=2&boyut=10')
  await page.getByRole('button', { name: 'Menüyü daralt', exact: true }).click()
  await check(page, 'minimal-narrow-menu')
  await page.getByRole('link', { name: 'Ekip', exact: true }).focus(); await page.keyboard.press('Escape')
  assert.equal(await page.getByRole('link', { name: 'Ekip', exact: true }).getAttribute('aria-expanded'), 'false')
  assert.equal(await page.getByRole('link', { name: 'Ekip', exact: true }).evaluate(node => node === document.activeElement), true)
  await page.getByRole('link', { name: 'Ekip', exact: true }).hover(); await check(page, 'minimal-narrow-tooltip')
  await page.getByRole('button', { name: 'Menüyü genişlet', exact: true }).click()
  await page.goto(origin + '/yonetim/degisiklik-kayitlari'); await ready(page)
  const audit = page.getByRole('navigation', { name: 'Kayıt sayfaları', exact: true })
  assert.equal(await audit.getByRole('button', { name: 'Son sayfa', exact: true }).count(), 0)
  await audit.getByRole('button', { name: 'Sonraki sayfa', exact: true }).click(); await ready(page)
  await audit.getByRole('button', { name: 'İlk sayfa', exact: true }).click(); await ready(page)
  await audit.getByLabel('Sayfadaki kayıt sayısı').selectOption('10'); await ready(page)
  await check(page, 'minimal-audit-pagination')
}

