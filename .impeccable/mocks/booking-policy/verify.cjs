const { chromium } = require('../../../tests/Web.E2E/node_modules/playwright')
const assert = require('node:assert/strict')
const fs = require('node:fs'), path = require('node:path')
const { pathToFileURL } = require('node:url')
;(async () => {
  const browser = await chromium.launch({ headless: true, channel: 'chrome' })
  const page = await browser.newPage(), errors = [], requests = [], captures = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('request', request => { if (!request.url().startsWith('file:')) requests.push(request.url()) })
  const file = pathToFileURL(path.join(__dirname, 'index.html')).href
  try {
    for (const width of [1280, 768, 390, 320]) {
      await page.setViewportSize({ width, height: 1000 })
      await page.goto(file)
      await page.evaluate(() => document.fonts.ready)
      for (const state of ['ready', 'dirty', 'saving', 'success', 'invalid', 'conflict', 'uncertain', 'loading', 'load-error', 'missing', 'session', 'forbidden', 'limited']) {
        await page.locator('#state').selectOption(state)
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `${width}/${state} overflow`)
        if (state === 'ready' || (width === 390 && ['invalid', 'conflict', 'uncertain', 'load-error', 'success'].includes(state))) {
          const name = `${state}-${width}.png`
          await page.screenshot({ path: path.join(__dirname, name), fullPage: true })
          captures.push(name)
        }
      }
      await page.locator('#state').selectOption('ready')
      const inputBounds = await page.locator('.input-group input').evaluateAll(nodes => nodes.map(node => node.getBoundingClientRect().right))
      assert.ok(Math.max(...inputBounds) - Math.min(...inputBounds) < 1, `${width} field alignment`)
      for (const id of ['lead', 'horizon', 'slot']) assert.ok(await page.locator(`label[for=${id}]`).isVisible())
      assert.equal(await page.locator('#save').isDisabled(), true)
      await page.locator('#lead').fill('90')
      assert.equal(await page.locator('#save').isEnabled(), true)
      await page.locator('#save').click()
      assert.equal(await page.locator('#save').isDisabled(), true)
      assert.match(await page.locator('#feedback').innerText(), /kaydedildi/)
      await page.locator('#lead').fill('1440')
      await page.locator('#horizon').fill('1')
      await page.locator('#save').click()
      assert.equal(await page.locator('#lead').getAttribute('aria-invalid'), 'true')
      assert.match(await page.locator('#lead-error').innerText(), /kısa olmalı/)
      await page.locator('#state').selectOption('saving')
      assert.equal(await page.locator('#save').isDisabled(), true)
      assert.equal(await page.locator('#lead').isDisabled(), true)
      if (width <= 900) {
        await page.locator('#mobile').click()
        assert.equal(await page.locator('#navigation').isVisible(), true)
        await page.keyboard.press('Escape')
        assert.equal(await page.locator('#navigation').isVisible(), false)
        assert.equal(await page.locator('#mobile').evaluate(node => node === document.activeElement), true)
      }
    }
    assert.deepEqual(errors, [])
    assert.deepEqual(requests, [])
    const report = { pass: true, scope: 'static mock only; no API or real save', widths: [1280, 768, 390, 320], states: 13, captures, errors, externalRequests: requests }
    fs.writeFileSync(path.join(__dirname, 'verification.json'), JSON.stringify(report, null, 2))
    console.log(JSON.stringify(report))
  } finally { await browser.close() }
})().catch(error => { console.error(error); process.exitCode = 1 })
