import ErrorMessage from '../../components/ErrorMessage'
import { useRef, useState, type FormEvent } from 'react'
import styles from './AuthenticationScreens.module.css'

type ResetBody = { token: string; newPassword: string; confirmPassword: string }
type Props = { onRequest: (body: ResetBody) => Promise<Response>; onDone: () => void; onCancel: () => void; staff?: boolean; linkToken?: string }

export default function PasswordResetForm({ onRequest, onDone, onCancel, staff = false, linkToken }: Props) {
  const [token, setToken] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const pending = useRef(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending.current) return
    setError('')
    if (newPassword !== confirmPassword) {
      setNewPassword('')
      setConfirmPassword('')
      setError('Yeni parola ve tekrarı aynı olmalı.')
      return
    }
    pending.current = true
    setBusy(true)
    try {
      const response = await onRequest({ token: linkToken ?? token.trim(), newPassword, confirmPassword })
      if (response.status === 204) {
        onDone()
      } else if (response.status === 400 || response.status === 409) {
        const problem = (await response.json()) as { title?: string }
        setError(problem.title ?? 'Sıfırlama kodunu ve parola alanlarını kontrol edin.')
      } else if (response.status === 429) {
        setError('Çok fazla deneme. Daha sonra tekrar deneyin.')
      } else {
        setError('Sonuç doğrulanamadı. Yeni parolanızla giriş yapmayı deneyin; otomatik tekrar yapılmadı.')
      }
    } catch {
      setError('Sonuç doğrulanamadı. Yeni parolanızla giriş yapmayı deneyin; otomatik tekrar yapılmadı.')
    } finally {
      setToken('')
      setNewPassword('')
      setConfirmPassword('')
      pending.current = false
      setBusy(false)
    }
  }

  return (
    <>
      <h1 id="page-title">{staff ? 'Çalışan parola sıfırlama' : 'İşletme sahibi parola sıfırlama'}</h1>
      <p>{staff
        ? 'İşletme sahibinden, kimliğiniz doğrulandıktan sonra aldığınız 30 dakika geçerli kodu kullanın.'
        : linkToken !== undefined ? 'E-posta bağlantınızla yeni parolanızı belirleyin. Sonraki girişte mevcut ikinci adımınızı kullanın.'
        : 'Kimliğiniz doğrulandıktan sonra özel olarak teslim edilen 30 dakika geçerli kurtarma kodunu kullanın.'}</p>
      <form onSubmit={handleSubmit} aria-label="Parola sıfırlama" aria-busy={busy}>
        {linkToken === undefined && <><label htmlFor="reset-token">Sıfırlama kodu</label>
        <input id="reset-token" type="password" autoComplete="off" required maxLength={8192}
          disabled={busy} value={token} onChange={event => setToken(event.target.value)} /></>}
        <p id="reset-rules">Yeni parola en az 12 karakter; büyük/küçük harf, rakam ve özel karakter içermeli.</p>
        <label htmlFor="reset-password">Yeni parola</label>
        <input id="reset-password" type="password" autoComplete="new-password" required minLength={12} maxLength={1024}
          aria-describedby="reset-rules" disabled={busy} value={newPassword}
          onChange={event => setNewPassword(event.target.value)} />
        <label htmlFor="reset-confirm">Yeni parola tekrarı</label>
        <input id="reset-confirm" type="password" autoComplete="new-password" required maxLength={1024}
          disabled={busy} value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} />
        <div className={styles.confirmationActions}>
          <button type="submit" disabled={busy}>{busy ? 'Parola sıfırlanıyor…' : 'Parolayı sıfırla'}</button>
          <button type="button" onClick={onCancel} disabled={busy}>Girişe dön</button>
        </div>
      </form>
      <ErrorMessage message={error} />
    </>
  )
}
