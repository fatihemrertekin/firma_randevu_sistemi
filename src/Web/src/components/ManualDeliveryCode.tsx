import { useState } from 'react'

type Props = {
  inputId: string
  label: string
  token: string
  expiresAt: string
  busy: boolean
  onClear: () => void
}

export default function ManualDeliveryCode(props: Props) {
  const [showCode, setShowCode] = useState(false)
  return <div>
    <label htmlFor={props.inputId}>{props.label}</label>
    <input id={props.inputId} type={showCode ? 'text' : 'password'} autoComplete="off" readOnly value={props.token}
      onFocus={event => { if (showCode) event.currentTarget.select() }} />
    <p>Son kullanım: {new Date(props.expiresAt).toLocaleString('tr-TR', { timeZone: 'Europe/Istanbul' })} (İstanbul)</p>
    <button type="button" disabled={props.busy} onClick={() => setShowCode(!showCode)}>{showCode ? 'Kodu gizle' : 'Kodu göster'}</button>
    {showCode && <p>Kod alanına tıklayın; seçili kodu bilgisayarda Ctrl+C ile, telefonda kopyalama menüsüyle kopyalayın. Yalnız doğrulanmış çalışana teslim edin, ardından kodu temizleyin.</p>}
    <button type="button" disabled={props.busy} onClick={props.onClear}>Kodu teslim ettim, temizle</button>
  </div>
}
