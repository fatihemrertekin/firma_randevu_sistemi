import ErrorMessage from '../../components/ErrorMessage'
import { useRef, useState, type FormEvent } from 'react'
import ManualDeliveryCode from '../../components/ManualDeliveryCode'

type Issued = { token: string; expiresAt: string }
type Props = { post: (path: string, body: object) => Promise<Response> }

export default function StaffPasswordReset({ post }: Props) {
  const [email, setEmail] = useState('')
  const [verified, setVerified] = useState(false)
  const [issued, setIssued] = useState<Issued | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const pending = useRef(false)

  async function issue(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current || !verified) return
    pending.current = true
    setBusy(true)
    setError('')
    setIssued(null)
    try {
      const response = await post('/api/staff-password-resets/', { email: email.trim(), verifiedRecipient: verified })
      if (response.status === 200) {
        setIssued(await response.json() as Issued)
      } else if (response.status === 400) {
        const problem = await response.json() as { title?: string }
        setError(problem.title ?? 'Hesabı ve alıcı onayını kontrol edin.')
      } else if (response.status === 401 || response.status === 403) {
        setError('İşletme sahibi oturumu geçersiz. Yeniden giriş yapın.')
      } else if (response.status === 429) {
        setError('Çok fazla deneme. Daha sonra tekrar deneyin.')
      } else {
        setError('Kod üretimi doğrulanamadı; otomatik tekrar yapılmadı. Çalışana kod teslim etmeyin.')
      }
    } catch {
      setError('Kod üretimi doğrulanamadı; otomatik tekrar yapılmadı. Çalışana kod teslim etmeyin.')
    } finally {
      setEmail('')
      setVerified(false)
      pending.current = false
      setBusy(false)
    }
  }

  return <section aria-labelledby="staff-reset-title">
    <h2 id="staff-reset-title">Çalışan parola sıfırlama kodu</h2>
    <p>Parolasını unutan mevcut çalışan için 30 dakikalık kod üretin. Çalışan yeni parolasını kendisi belirler.
      Kodu yalnız kimliğini ve e-postasını doğruladığınız çalışana özel kanaldan teslim edin.</p>
    <form onSubmit={issue} aria-label="Çalışan sıfırlama kodu üret" aria-busy={busy}>
      <label htmlFor="staff-reset-email">Parolasını unutan çalışanın e-postası</label>
      <input id="staff-reset-email" type="email" required maxLength={256} autoComplete="off" disabled={busy}
        value={email} onChange={event => setEmail(event.target.value)} />
      <label><input type="checkbox" checked={verified} disabled={busy} onChange={event => setVerified(event.target.checked)} />
        Çalışanın kimliğini ve e-postasını doğruladım; sıfırlama kodunu yalnız kendisine teslim edeceğim.</label>
      <button type="submit" disabled={busy || !verified}>{busy ? 'Kod üretiliyor…' : 'Sıfırlama kodu üret'}</button>
    </form>
    {issued && <ManualDeliveryCode key={issued.token} inputId="issued-staff-reset"
      label="Çalışan sıfırlama kodu — yalnız bu ekranda gösterilir" token={issued.token}
      expiresAt={issued.expiresAt} busy={busy} onClear={() => setIssued(null)} />}
    <ErrorMessage message={error} />
  </section>
}
