import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('başlangıç ekranı', () => {
  it('rezervasyon henüz açılmadığında hazırlık metnini gösterir', () => {
    const html = renderToStaticMarkup(<App />)

    expect(html).toContain('İşletmenizin günü, tek bakışta.')
    expect(html).toContain('ekranları burada yer alacak')
  })
})
