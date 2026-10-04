import { useRef, useState, type FormEvent } from 'react'
import { postWithCsrf } from '../../app/api'
import ErrorMessage from '../../components/ErrorMessage'
import styles from './AuthenticationScreens.module.css'
import AuthenticationLayout from './AuthenticationLayout'

export default function OwnerEmailConfirmation({ token, onClose }: { token: string; onClose: () => void }) {
  const [busy, setBusy] = useState(false)
  const [confirmed, setConfirmed] = useState(false)
  const [error, setError] = useState('')
  const pending = useRef(false)
  async function confirm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current || confirmed) return
    pending.current = true
    setBusy(true)
    setError('')
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), 15000)
    try {
      const response = await postWithCsrf('/api/auth/recovery-email/confirm', { token }, controller.signal)
      if (response.ok) setConfirmed(true)
      else setError(response.status === 429 ? 'Çok fazla deneme. Daha sonra tekrar deneyin.'
        : response.status === 400 ? 'Doğrulama bağlantısı geçersiz veya süresi dolmuş. Hesabınızdan yeni bir ileti isteyin.'
        : 'Doğrulama tamamlanamadı. Yeniden deneyin.')
    } catch { setError('Sonuç doğrulanamadı. Hesabınızdan doğrulama durumunu kontrol edin.') }
    finally { clearTimeout(timer); setBusy(false); pending.current = false }
  }
  return <AuthenticationLayout>
      <h1 id="page-title">Hesap e-postasını doğrula</h1>
      {confirmed ? <p role="status">Hesap e-postanız doğrulandı. Hesabınıza dönebilirsiniz.</p> : <>
        <p>Bağlantıyı onaylayarak hesap e-postanıza erişebildiğinizi doğrulayın. Parolanız ve iki aşamalı girişiniz değişmez.</p>
      </>}
      <div className={styles.confirmationActions}>
        {!confirmed &&
        <form onSubmit={confirm} aria-label="Hesap e-postasını doğrulama" aria-busy={busy}>
          <button type="submit" disabled={busy}>{busy ? 'Doğrulanıyor…' : 'E-postamı doğrula'}</button>
        </form>
        }
        <button type="button" disabled={busy} onClick={onClose}>Hesaba dön</button>
      </div>
      <ErrorMessage message={error} />
  </AuthenticationLayout>
}
