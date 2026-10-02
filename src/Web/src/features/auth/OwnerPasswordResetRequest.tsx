import { useEffect, useRef, useState, type FormEvent } from 'react'
import { postWithCsrf } from '../../app/api'
import ErrorMessage from '../../components/ErrorMessage'
import PasswordResetForm from './PasswordResetForm'
import styles from './AuthenticationScreens.module.css'

export default function OwnerPasswordResetRequest({ onCancel, onDone }: { onCancel: () => void; onDone: () => void }) {
  const [email, setEmail] = useState('')
  const [available, setAvailable] = useState(false)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [manual, setManual] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const pending = useRef(false)
  useEffect(() => {
    const controller = new AbortController()
    let active = true
    const timer = setTimeout(() => controller.abort(), 15000)
    fetch('/api/auth/password-reset-options', { cache: 'no-store', signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error('status')
        const result: unknown = await response.json()
        if (typeof result !== 'object' || result === null || !('available' in result) || typeof result.available !== 'boolean')
          throw new Error('status')
        if (active) setAvailable(result.available)
      })
      .catch(() => { if (active) setError('Gönderim durumu alınamadı. Daha sonra yeniden deneyin.') })
      .finally(() => { clearTimeout(timer); if (active) setLoading(false) })
    return () => { active = false; clearTimeout(timer); controller.abort() }
  }, [])
  async function request(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current || !available) return
    pending.current = true; setBusy(true); setError(''); setNotice('')
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), 15000)
    try {
      const response = await postWithCsrf('/api/auth/password-reset-request', { email: email.trim() }, controller.signal)
      if (response.status === 202) setNotice('Bilgiler uygunsa e-postanıza sıfırlama bağlantısı hazırlanacaktır. Bağlantı 30 dakika geçerlidir.')
      else setError(response.status === 429 ? 'Çok fazla istek. Daha sonra yeniden deneyin.' : 'İstek tamamlanamadı. Daha sonra yeniden deneyin.')
    } catch { setError('İsteğin sonucu doğrulanamadı. Daha sonra yeniden deneyin.') }
    finally { clearTimeout(timer); pending.current = false; setBusy(false) }
  }
  if (manual) return <PasswordResetForm onRequest={body => postWithCsrf('/api/auth/reset-password', body, AbortSignal.timeout(15000))}
    onCancel={onCancel} onDone={onDone} />
  return <>
    <h1 id="page-title">Parolanızı yenileyin</h1>
    <p>Önceden doğruladığınız hesap e-postasına sıfırlama bağlantısı isteyin. İki aşamalı girişiniz korunur.</p>
    {loading ? <p role="status">Gönderim durumu kontrol ediliyor…</p>
      : !available && <p>Otomatik e-posta gönderimi bu kurulumda henüz kullanılamıyor.</p>}
    <form onSubmit={request} aria-label="Parola sıfırlama bağlantısı iste" aria-busy={busy}>
      <label htmlFor="reset-email">Hesap e-postası</label>
      <input id="reset-email" type="email" autoComplete="email" required maxLength={254}
        disabled={busy || loading || !available} value={email} onChange={event => setEmail(event.target.value)} />
      <div className={styles.confirmationActions}>
        <button type="submit" disabled={busy || loading || !available}>{busy ? 'İsteniyor…' : 'Bağlantı iste'}</button>
        <button type="button" disabled={busy} onClick={onCancel}>Girişe dön</button>
      </div>
    </form>
    <ErrorMessage message={error} />
    {notice && <p role="status">{notice}</p>}
    <button className={styles.textAction} type="button" disabled={busy} onClick={() => setManual(true)}>Parola sıfırlama kodum var</button>
  </>
}
