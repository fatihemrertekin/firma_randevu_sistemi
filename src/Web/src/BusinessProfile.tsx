import { useEffect, useRef, useState, type FormEvent } from 'react'

type Profile = { name: string; phone: string | null; email: string | null; address: string | null; version: string }
type Props = { post: (path: string, body: object, signal?: AbortSignal) => Promise<Response> }

async function readProfile(response: Response): Promise<Profile> {
  const value: unknown = await response.json()
  if (typeof value !== 'object' || value === null || !('name' in value) || typeof value.name !== 'string' ||
    !('version' in value) || typeof value.version !== 'string' ||
    !('phone' in value) || (value.phone !== null && typeof value.phone !== 'string') ||
    !('email' in value) || (value.email !== null && typeof value.email !== 'string') ||
    !('address' in value) || (value.address !== null && typeof value.address !== 'string')) {
    throw new Error('Profil yanıtı doğrulanamadı.')
  }
  return { name: value.name, phone: value.phone, email: value.email, address: value.address, version: value.version }
}

export default function BusinessProfile({ post }: Props) {
  const [profile, setProfile] = useState<Profile | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [reloadRequired, setReloadRequired] = useState(false)
  const [reload, setReload] = useState(0)
  const pending = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    let active = true
    const timer = setTimeout(() => controller.abort(), 15000)
    fetch('/api/business-profile/', { cache: 'no-store', signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error(response.status === 401 || response.status === 403
          ? 'Owner oturumu geçersiz. Yeniden giriş yapın.' : 'Profil yüklenemedi. Yeniden yüklemeyi deneyin.')
        const loaded = await readProfile(response)
        if (active) { setProfile(loaded); setReloadRequired(false) }
      })
      .catch((failure: unknown) => {
        if (active) {
          setReloadRequired(true)
          setError(failure instanceof Error && failure.name !== 'AbortError'
            ? failure.message : 'Profil yüklenemedi. Yeniden yüklemeyi deneyin.')
        }
      })
      .finally(() => { clearTimeout(timer); if (active) setLoading(false) })
    return () => { active = false; clearTimeout(timer); controller.abort() }
  }, [reload])

  function change(field: 'name' | 'phone' | 'email' | 'address', value: string) {
    setNotice('')
    setProfile(current => current && ({ ...current, [field]: value }))
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!profile || loading || reloadRequired || pending.current) return
    pending.current = true
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const response = await post('/api/business-profile/', profile, AbortSignal.timeout(20000))
      if (response.status === 200) {
        setProfile(await readProfile(response))
        setNotice('İşletme profili kaydedildi.')
      } else if (response.status === 400) {
        const problem = await response.json() as { title?: string }
        setError(problem.title ?? 'Profil alanlarını kontrol edin.')
      } else if (response.status === 429) {
        setError('Çok fazla istek. Daha sonra tekrar deneyin.')
      } else {
        setReloadRequired(true)
        setError(response.status === 409
          ? 'Profil başka bir işlemde değişti. Güncel bilgileri yükleyip yeniden düzenleyin.'
          : response.status === 401 || response.status === 403
            ? 'Owner oturumu geçersiz. Yeniden giriş yapın.'
            : 'Kayıt sonucu doğrulanamadı; otomatik tekrar yapılmadı. Güncel bilgileri yükleyin.')
      }
    } catch {
      setReloadRequired(true)
      setError('Kayıt sonucu doğrulanamadı; otomatik tekrar yapılmadı. Güncel bilgileri yükleyin.')
    } finally { pending.current = false; setBusy(false) }
  }

  return <section aria-labelledby="business-profile-title">
    <h2 id="business-profile-title">İşletme profili</h2>
    <p>İşletme adını ve iletişim bilgilerini düzenleyin. Telefon, e-posta ve adres isteğe bağlıdır.</p>
    {loading ? <p role="status">Profil yükleniyor…</p> : profile && <>
      {!profile.name && <p>İşletme profili henüz doldurulmadı.</p>}
      <form aria-label="İşletme profilini düzenle" aria-busy={busy} onSubmit={save}>
        <label htmlFor="business-name">İşletme adı</label>
        <input id="business-name" required minLength={2} maxLength={150} autoComplete="organization"
          disabled={busy || reloadRequired} value={profile.name} onChange={event => change('name', event.target.value)} />
        <label htmlFor="business-phone">İletişim telefonu (isteğe bağlı)</label>
        <input id="business-phone" type="tel" maxLength={32} autoComplete="tel" placeholder="0212 000 00 00"
          disabled={busy || reloadRequired} value={profile.phone ?? ''} onChange={event => change('phone', event.target.value)} />
        <label htmlFor="business-email">İletişim e-postası (isteğe bağlı)</label>
        <input id="business-email" type="email" maxLength={254} autoComplete="email"
          disabled={busy || reloadRequired} value={profile.email ?? ''} onChange={event => change('email', event.target.value)} />
        <label htmlFor="business-address">Açık adres (isteğe bağlı)</label>
        <textarea id="business-address" maxLength={500} rows={3} autoComplete="street-address"
          disabled={busy || reloadRequired} value={profile.address ?? ''} onChange={event => change('address', event.target.value)} />
        <button type="submit" disabled={busy || reloadRequired}>{busy ? 'Profil kaydediliyor…' : 'Profili kaydet'}</button>
      </form>
    </>}
    <button type="button" disabled={busy || loading} onClick={() => {
      setLoading(true); setError(''); setNotice(''); setReload(current => current + 1)
    }}>Güncel bilgileri yükle</button>
    {!loading && profile && <p>Yeniden yükleme, formdaki kaydedilmemiş değişiklikleri siler.</p>}
    {error && <p role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
  </section>
}
