import { describe, expect, it } from 'vitest'
import { formatTryPrice, parseTryPrice } from './money'

describe('TRY fiyat metni', () => {
  it.each([['0', '0.00'], ['0,29', '0.29'], ['001,5', '1.50'], ['999999.99', '999999.99']])('geçerli %s değerini para hesabı yapmadan aktarır', (input, expected) => {
    expect(parseTryPrice(input)).toBe(expected)
  })
  it.each(['1.005', '1e3', '-1', '1.250,00', '1000000', '1.', '', 'NaN'])('%s tutarını sessizce yuvarlamaz veya yanlış yorumlamaz', input => {
    expect(parseTryPrice(input)).toBeNull()
  })
  it('Türkçe tutarı iki basamak ve binlik ayracıyla gösterir', () => {
    expect(formatTryPrice('1234.50')).toBe('1.234,50 ₺'); expect(formatTryPrice('0.29')).toBe('0,29 ₺')
  })
})
