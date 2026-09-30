import { useRef, useState, type FormEvent } from 'react'

type Body = { email: string; token: string; password: string; confirmPassword: string }
type Props = { post: (body: Body) => Promise<Response>; onDone: () => void; onCancel: () => void }

export default function StaffInvitationAcceptForm({ post, onDone, onCancel }: Props) {
  const [email, setEmail] = useState('')
  const [token, setToken] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const pending = useRef(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current) return
    setError('')
    if (password !== confirmPassword) {
      setPassword(''); setConfirmPassword('')
      setError('Parola ve tekrarı aynı olmalı.')
      return
    }
    pending.current = true
    setBusy(true)
    try {
      const response = await post({ email: email.trim(), token: token.trim(), password, confirmPassword })
      if (response.status === 204) onDone()
      else if ([400, 409].includes(response.status)) {
        const problem = (await response.json()) as { title?: string }
        setError(problem.title ?? 'Davet bilgilerini ve parola kurallarını kontrol edin.')
      } else if (response.status === 429) setError('Çok fazla deneme. Daha sonra tekrar deneyin.')
      else setError('Hesap açma sonucu doğrulanamadı. Normal giriş yapmayı deneyin; otomatik tekrar yapılmadı.')
    } catch { setError('Hesap açma sonucu doğrulanamadı. Normal giriş yapmayı deneyin; otomatik tekrar yapılmadı.') }
    finally { setToken(''); setPassword(''); setConfirmPassword(''); pending.current = false; setBusy(false) }
  }
  return <>
    <h1 id="page-title">Staff davetini kabul et</h1>
    <p>İşletme sahibinin size verdiği kodu ve davet edilen e-postanızı kullanın. Kod 24 saat geçerlidir.</p>
    <form onSubmit={submit} aria-label="Staff davetini kabul et" aria-busy={busy}>
      <label htmlFor="accept-email">Davet edilen e-posta</label>
      <input id="accept-email" type="email" autoComplete="username" required maxLength={256} disabled={busy}
        value={email} onChange={event => setEmail(event.target.value)} />
      <label htmlFor="accept-token">Davet kodu</label>
      <input id="accept-token" type="password" autoComplete="off" required maxLength={43} disabled={busy}
        value={token} onChange={event => setToken(event.target.value)} />
      <p id="invite-password-rules">Parola en az 12 karakter; büyük/küçük harf, rakam ve özel karakter içermeli.</p>
      <label htmlFor="accept-password">Parola</label>
      <input id="accept-password" type="password" autoComplete="new-password" required minLength={12} maxLength={1024}
        aria-describedby="invite-password-rules" disabled={busy} value={password} onChange={event => setPassword(event.target.value)} />
      <label htmlFor="accept-confirm">Parola tekrarı</label>
      <input id="accept-confirm" type="password" autoComplete="new-password" required maxLength={1024} disabled={busy}
        value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} />
      <button type="submit" disabled={busy}>{busy ? 'Hesap açılıyor…' : 'Hesabımı aç'}</button>
    </form>
    <button type="button" disabled={busy} onClick={onCancel}>Girişe dön</button>
    {error && <p role="alert">{error}</p>}
  </>
}
