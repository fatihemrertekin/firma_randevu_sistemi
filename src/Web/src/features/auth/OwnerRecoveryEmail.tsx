import { useEffect, useRef, useState, type FormEvent } from 'react'
import ErrorMessage from '../../components/ErrorMessage'
import styles from './OwnerRecoveryEmail.module.css'

type EmailStatus = { email: string; verifiedAt: string | null; deliveryAvailable: boolean }
type Props = { post: (path: string, body: object, signal?: AbortSignal) => Promise<Response>; disabled?: boolean }

export default function OwnerRecoveryEmail({ post, disabled = false }: Props) {
  const [status, setStatus] = useState<EmailStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [reload, setReload] = useState(0)
  const pending = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    let active = true
    const timer = setTimeout(() => controller.abort(), 15000)
    fetch('/api/auth/recovery-email/', { cache: 'no-store', signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error('E-posta doğrulama durumu alınamadı. Yeniden deneyin.')
        const value: unknown = await response.json()
        if (typeof value !== 'object' || value === null || !('email' in value) || typeof value.email !== 'string' ||
          !('verifiedAt' in value) || (value.verifiedAt !== null && typeof value.verifiedAt !== 'string') ||
          !('deliveryAvailable' in value) || typeof value.deliveryAvailable !== 'boolean') {
          throw new Error('E-posta doğrulama durumu alınamadı. Yeniden deneyin.')
        }
        if (active) setStatus({ email: value.email, verifiedAt: value.verifiedAt, deliveryAvailable: value.deliveryAvailable })
      })
      .catch(() => { if (active) setError('E-posta doğrulama durumu alınamadı. Yeniden deneyin.') })
      .finally(() => { clearTimeout(timer); if (active) setLoading(false) })
    return () => { active = false; clearTimeout(timer); controller.abort() }
  }, [reload])

  async function request(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (disabled || pending.current || loading || !status?.deliveryAvailable || status.verifiedAt) return
    pending.current = true
    setBusy(true)
    setError('')
    setNotice('')
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), 15000)
    try {
      const response = await post('/api/auth/recovery-email/request', {}, controller.signal)
      if (!response.ok) {
        setError(response.status === 429 ? 'Yeni doğrulama istemeden önce bir dakika bekleyin.'
          : response.status === 401 || response.status === 403 ? 'Oturum geçersiz. Yeniden giriş yapın.'
          : response.status === 409 ? 'Hesap e-postanız zaten doğrulanmış. Durumu yenileyin.'
          : 'Doğrulama iletisi teslim edilemedi. Daha sonra yeniden deneyin.')
        return
      }
      setNotice('Doğrulama iletisi hazırlandı. E-postanızdaki bağlantıyı 30 dakika içinde açıp onaylayın.')
    } catch { setError('İletinin teslimi doğrulanamadı. Bir dakika sonra yeniden deneyin.') }
    finally { clearTimeout(timer); pending.current = false; setBusy(false) }
  }

  return <section aria-labelledby="recovery-email-title" aria-busy={loading || busy}>
    <h2 id="recovery-email-title">Kurtarma e-postası</h2>
    <p>Hesap e-postanıza erişebildiğinizi doğrulayın. Parolanız ve iki aşamalı girişiniz değişmez.</p>
    {loading ? <p role="status">Doğrulama durumu yükleniyor…</p> : status && <>
      <div className={styles.row}><span className={styles.address}>{status.email}</span>
        <span className={styles.state}>{status.verifiedAt ? 'Doğrulandı' : 'Henüz doğrulanmadı'}</span></div>
      {!status.verifiedAt && !status.deliveryAvailable && <p>E-posta doğrulama gönderimi şu anda kullanılamıyor.</p>}
    </>}
    <div className={styles.actions}>
      {!loading && status && !status.verifiedAt && <form aria-label="Kurtarma e-postasını doğrulama" onSubmit={request}>
        <button type="submit" disabled={disabled || busy || !status.deliveryAvailable}>
          {busy ? 'Hazırlanıyor…' : 'Doğrulama gönder'}</button>
      </form>}
      <button type="button" disabled={disabled || loading || busy} onClick={() => {
        setLoading(true); setError(''); setNotice(''); setReload(current => current + 1)
      }}>Durumu yenile</button>
    </div>
    <ErrorMessage message={error} />
    {notice && <p role="status">{notice}</p>}
  </section>
}
