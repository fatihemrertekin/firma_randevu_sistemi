export const logoEndpoint = '/api/business-logo/'
export const maxLogoBytes = 1024 * 1024
export type Logo = { hasLogo: boolean; version: string; imageUrl: string | null; width: number | null; height: number | null }
export class LogoError extends Error {
  constructor(public status: number) { super('Logo yanıtı doğrulanamadı.') }
}
export async function readLogo(response: Response): Promise<Logo> {
  if (!response.ok) throw new LogoError(response.status)
  const value: unknown = await response.json()
  if (typeof value !== 'object' || value === null || !('hasLogo' in value) || typeof value.hasLogo !== 'boolean' ||
    !('version' in value) || typeof value.version !== 'string' || !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/iu.test(value.version) ||
    !('imageUrl' in value) || !('width' in value) || !('height' in value)) throw new LogoError(500)
  if (value.hasLogo ? value.imageUrl !== `/api/business-logo/image/${value.version}` ||
    typeof value.width !== 'number' || !Number.isInteger(value.width) || value.width < 1 || value.width > 512 ||
    typeof value.height !== 'number' || !Number.isInteger(value.height) || value.height < 1 || value.height > 512 :
    value.imageUrl !== null || value.width !== null || value.height !== null) throw new LogoError(500)
  return { hasLogo: value.hasLogo, version: value.version, imageUrl: value.imageUrl as string | null,
    width: value.width as number | null, height: value.height as number | null }
}
export function logoFailure(status: number) {
  if (status === 400) return 'Logo dosyasını kontrol edin.'
  if (status === 413) return 'Logo dosyası çok büyük. En fazla 1 MB dosya seçin.'
  if (status === 401) return 'Oturumunuz sona erdi. Yeniden giriş yapın.'
  if (status === 403) return 'Bu işlem için yetkiniz yok.'
  if (status === 409) return 'Logo başka bir işlemde değişti. Güncel logoyu yükleyin; seçiminiz korunuyor.'
  if (status === 429) return 'Çok sık denendi. Bekleyip yeniden deneyin.'
  return 'Sonuç doğrulanamadı. Güncel logoyu yükleyerek kontrol edin.'
}
