import type { FormEvent } from 'react'

type Props = {
  currentPassword: string
  newPassword: string
  confirmPassword: string
  busy: boolean
  onCurrentPassword: (value: string) => void
  onNewPassword: (value: string) => void
  onConfirmPassword: (value: string) => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
}

export default function PasswordChangeForm(props: Props) {
  return (
    <form onSubmit={props.onSubmit} aria-label="Parola değiştirme" aria-busy={props.busy}>
      <h2>Parola değiştir</h2>
      <p id="password-rules">Yeni parola en az 12 karakter; büyük/küçük harf, rakam ve özel karakter içermeli.</p>
      <p>Değişiklik sonrası bütün oturumlar kapanır. Yeni parolanız ve ikinci adımla yeniden giriş yapın.</p>
      <label htmlFor="current-password">Mevcut parola</label>
      <input id="current-password" type="password" autoComplete="current-password" required maxLength={1024}
        disabled={props.busy} value={props.currentPassword}
        onChange={event => props.onCurrentPassword(event.target.value)} />
      <label htmlFor="new-password">Yeni parola</label>
      <input id="new-password" type="password" autoComplete="new-password" required minLength={12} maxLength={1024}
        aria-describedby="password-rules" disabled={props.busy} value={props.newPassword}
        onChange={event => props.onNewPassword(event.target.value)} />
      <label htmlFor="confirm-password">Yeni parola tekrarı</label>
      <input id="confirm-password" type="password" autoComplete="new-password" required maxLength={1024}
        disabled={props.busy} value={props.confirmPassword}
        onChange={event => props.onConfirmPassword(event.target.value)} />
      <button type="submit" disabled={props.busy}>
        {props.busy ? 'Parola değiştiriliyor…' : 'Parolayı değiştir'}
      </button>
    </form>
  )
}
