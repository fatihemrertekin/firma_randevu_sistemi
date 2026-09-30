import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('işletme girişi', () => {
  it('oturum kontrolü sürerken hesap veya parola göstermez', () => {
    const html = renderToStaticMarkup(<App />)

    expect(html).toContain('Oturum kontrol ediliyor')
    expect(html).not.toContain('type="password"')
  })
})
