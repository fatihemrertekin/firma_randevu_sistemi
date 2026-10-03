// TRY tutarı ondalık metin olarak kalır; para hesabı ve float yuvarlaması yoktur.
export function parseTryPrice(value: string): string | null {
  const trimmed = value.trim()
  if (!/^[0-9]{1,6}([.,][0-9]{1,2})?$/u.test(trimmed)) return null
  const [whole, fraction = ''] = trimmed.replace(',', '.').split('.')
  return `${whole.replace(/^0+(?=\d)/u, '')}.${fraction.padEnd(2, '0')}`
}
export function formatTryPrice(value: string): string {
  const [whole, fraction] = value.split('.')
  return `${whole.replace(/\B(?=(\d{3})+(?!\d))/gu, '.')},${fraction} ₺`
}
