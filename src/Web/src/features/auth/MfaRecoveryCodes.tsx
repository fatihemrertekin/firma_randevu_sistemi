import { useEffect, useRef, useState, type FormEvent } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import styles from './MfaRecoveryCodes.module.css'

type Props = {
  post: (path: string, body: object, signal?: AbortSignal) => Promise<Response>
  disabled: boolean
  onReplaced: (codes: string[]) => void
}

export default function MfaRecoveryCodes({ post, disabled, onReplaced }: Props) {
  const [remaining, setRemaining] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [password, setPassword] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const pending = useRef(false)
  useEffect(() => {
    const controller = new AbortController()
    fetch('/api/auth/mfa/recovery-codes', { cache: 'no-store', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) })
      .then(async response => {
        if (!response.ok) throw new Error('status')
        const body: unknown = await response.json()
        if (typeof body !== 'object' || body === null || !('remaining' in body) ||
          typeof body.remaining !== 'number' || !Number.isInteger(body.remaining) || body.remaining < 0) throw new Error('count')
        if (!controller.signal.aborted) setRemaining(body.remaining)
      }).catch(() => { if (!controller.signal.aborted) setError('Kod sayısı alınamadı. Bölümü yeniden açın.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [])
  async function replace(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current || disabled || !confirmed) return
    pending.current = true; setBusy(true); setError('')
    try {
      const response = await post('/api/auth/mfa/recovery-codes', { currentPassword: password })
      if (!response.ok) {
        setError(response.status === 429 ? 'Çok fazla deneme. Daha sonra tekrar deneyin.'
          : response.status === 400 ? 'Mevcut parola doğrulanamadı veya hesap geçici olarak kilitli.'
          : response.status === 401 || response.status === 403 || response.status === 409 ? 'Oturum geçersiz. Yeniden giriş yapın.'
          : 'Sonuç doğrulanamadı. Yeniden giriş yaptıktan sonra kod sayısını kontrol edin.')
        return
      }
      const body: unknown = await response.json()
      if (typeof body !== 'object' || body === null || !('recoveryCodes' in body) ||
        !Array.isArray(body.recoveryCodes) || body.recoveryCodes.length !== 8 ||
        !body.recoveryCodes.every((code: unknown): code is string => typeof code === 'string' && code.length > 0)) throw new Error('codes')
      onReplaced(body.recoveryCodes)
    } catch { setError('Sonuç doğrulanamadı. Yeniden giriş yapıp kodları tekrar oluşturun; eski kodlar yenilenmiş olabilir.') }
    finally { setPassword(''); pending.current = false; setBusy(false) }
  }
  return <section aria-labelledby="recovery-title">
    <h2 id="recovery-title">MFA kurtarma kodları</h2>
    <p>Telefonunuza erişemediğinizde parolanızdan sonraki ikinci adımda kullanılır. Parola sıfırlamaz.</p>
    <p role="status">{loading ? 'Kod sayısı kontrol ediliyor…' : remaining === null ? 'Kod sayısı şu anda bilinmiyor.' : `Kalan kullanılmamış kod: ${remaining}`}</p>
    <p id="recovery-warning">Yeni kodlar oluşturunca eski kodlar geçersiz olur ve bütün oturumlar kapanır. Parolanız ve doğrulayıcı uygulamanız değişmez. Yeni kodlar yalnız bir kez gösterilir.</p>
    <form onSubmit={replace} aria-label="MFA kurtarma kodlarını yenile" aria-busy={busy}>
      <label htmlFor="recovery-password">Mevcut parola</label>
      <input id="recovery-password" type="password" autoComplete="current-password" required maxLength={1024}
        disabled={busy || disabled} value={password} onChange={event => setPassword(event.target.value)} />
      <label className={styles.confirmation} htmlFor="recovery-confirm">
      <input id="recovery-confirm" type="checkbox" required checked={confirmed} aria-describedby="recovery-warning"
        disabled={busy || disabled} onChange={event => setConfirmed(event.target.checked)} />
        Eski kodların geçersiz olacağını anladım</label>
      <button type="submit" disabled={busy || disabled || !confirmed}>{busy ? 'Yenileniyor…' : 'Yeni MFA kurtarma kodları oluştur'}</button>
    </form>
    <ErrorMessage message={error} />
  </section>
}
