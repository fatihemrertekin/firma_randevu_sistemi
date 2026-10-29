import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { postWithCsrf, requestErrorMessage } from '../../app/api'
import SubscriptionOperator from './SubscriptionOperator'
import styles from './SubscriptionOperator.module.css'

type Account = { email: string; mfaEnabled: boolean; operatorAccess: boolean }
async function readAccount(): Promise<Account | null> {
  const response = await fetch('/api/auth/me', { cache: 'no-store', signal: AbortSignal.timeout(15000) })
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Oturum bilgisi alınamadı. Yeniden deneyin.')
  const value: unknown = await response.json()
  if (!value || typeof value !== 'object' || !('email' in value) || typeof value.email !== 'string' ||
    !('mfaEnabled' in value) || typeof value.mfaEnabled !== 'boolean' || !('operatorAccess' in value) || typeof value.operatorAccess !== 'boolean') throw new Error('Oturum bilgisi doğrulanamadı.')
  return { email: value.email, mfaEnabled: value.mfaEnabled, operatorAccess: value.operatorAccess }
}
export default function OperatorApplication() {
  const [account, setAccount] = useState<Account | null>(null), [loading, setLoading] = useState(true)
  const [email, setEmail] = useState(''), [password, setPassword] = useState(''), [code, setCode] = useState('')
  const [mfa, setMfa] = useState(false), [recovery, setRecovery] = useState(false), [key, setKey] = useState('')
  const [codes, setCodes] = useState<string[]>([]), [busy, setBusy] = useState(false), [error, setError] = useState('')
  const [unsaved, setUnsaved] = useState(false)
  const pending = useRef(false)
  const expired = useCallback(() => { setAccount(null); setMfa(false); setUnsaved(false) }, [])
  async function load() { setAccount(await readAccount()) }
  useEffect(() => { let active = true; void readAccount().then(value => { if (active) setAccount(value) }).catch(() => { if (active) setError('Oturum bilgisi alınamadı. Yeniden deneyin.') }).finally(() => { if (active) setLoading(false) }); return () => { active = false } }, [])
  async function submit(event: FormEvent) {
    event.preventDefault(); if (pending.current) return
    pending.current = true; setBusy(true); setError('')
    try {
      const path = account ? (key ? '/api/auth/mfa/enable' : '/api/auth/mfa/setup') : mfa ? '/api/auth/mfa/login' : '/api/auth/login'
      const response = await postWithCsrf(path, account ? { password, value: code } : mfa ? { value: code, recovery } : { email, password })
      setPassword(''); setCode('')
      if (!response.ok) {
        if (response.status === 429) throw new Error('Çok fazla deneme. Belirtilen bekleme süresinden sonra yeniden deneyin.')
        throw new Error(response.status === 401 ? 'Giriş bilgileri doğrulanamadı. Bilgilerinizi kontrol edin.' : 'İşlem tamamlanamadı. Bilgilerinizi kontrol edip yeniden deneyin.')
      }
      if (response.status === 202) { setMfa(true); return }
      if (path.endsWith('/setup')) {
        const value: unknown = await response.json()
        if (!value || typeof value !== 'object' || !('key' in value) || typeof value.key !== 'string') throw new Error('Kurulum anahtarı alınamadı.')
        setKey(value.key); return
      }
      if (path.endsWith('/enable')) {
        const value: unknown = await response.json()
        if (!value || typeof value !== 'object' || !('recoveryCodes' in value) || !Array.isArray(value.recoveryCodes) || value.recoveryCodes.length !== 8 || value.recoveryCodes.some(c => typeof c !== 'string')) throw new Error('Kurtarma kodları doğrulanamadı. Yeniden giriş yapın.')
        setCodes(value.recoveryCodes as string[]); setKey(''); setAccount(null); setMfa(false); return
      }
      await load(); setMfa(false)
    } catch (problem) { setError(requestErrorMessage(problem, problem instanceof Error ? problem.message : 'Giriş sonucu doğrulanamadı. Yeniden deneyin.')) }
    finally { pending.current = false; setBusy(false) }
  }
  async function logout() {
    if (pending.current) return
    if (unsaved && !window.confirm('Kaydedilmemiş bilgiler silinip çıkış yapılsın mı?')) return
    pending.current = true; setBusy(true); setError('')
    try {
      const response = await postWithCsrf('/api/auth/logout', {})
      if (!response.ok) throw new Error('Çıkış yapılamadı. Yeniden deneyin.')
      setAccount(null); setPassword(''); setCode(''); setKey(''); setMfa(false); setCodes([]); setUnsaved(false)
    } catch { setError('Çıkış yapılamadı. Yeniden deneyin.') }
    finally { pending.current = false; setBusy(false) }
  }
  return <div className={`management-theme ${styles.page}`}>
    <header className={styles.header}><span className={styles.brand}>Randevu</span><span>Abonelik yönetimi</span>{account?.operatorAccess && <button disabled={busy} onClick={() => void logout()}>Çıkış yap</button>}</header>
    {error && <p className={styles.error} role="alert">{error}</p>}
    {loading ? <main className={styles.main}><h1>Oturum kontrol ediliyor…</h1></main> : codes.length ? <main className={styles.auth}>
      <h1>Kurtarma kodlarını saklayın</h1><p>Bu kodlar bir kez gösterilir. Her biri yalnız bir kez kullanılabilir.</p><ul>{codes.map(c => <li key={c}><code>{c}</code></li>)}</ul>
      <button onClick={() => setCodes([])}>Kodları kaydettim</button>
    </main> : account?.operatorAccess ? <SubscriptionOperator onExpired={expired} onUnsaved={setUnsaved} onBusy={setBusy} /> : <main className={styles.auth}>
      <h1>{account ? 'İki aşamalı girişi kurun' : mfa ? 'İkinci adımı tamamlayın' : 'Abonelik yönetimine giriş'}</h1>
      {error && !account && !mfa && <button disabled={busy} onClick={() => { setLoading(true); void load().catch(() => setError('Oturum bilgisi alınamadı.')).finally(() => setLoading(false)) }}>Oturumu yeniden kontrol et</button>}
      <p>{account ? 'Doğrulayıcı uygulamanıza operatör hesabınızı ekleyin. Kurulum tamamlanmadan firma kayıtları açılamaz.' : mfa ? (recovery ? 'Sakladığınız kurtarma kodlarından birini girin.' : 'Doğrulayıcı uygulamanızdaki altı haneli kodu girin.') : 'Bu alan yalnız yazılım aboneliklerini yöneten operatöre aittir.'}</p>
      {key && <p>Uygulamada “anahtar gir” seçeneğini kullanın: <code className={styles.secret}>{key}</code></p>}
      <form onSubmit={e => void submit(e)}>
        {!account && !mfa && <><label htmlFor="operator-email">E-posta</label><input id="operator-email" type="email" autoComplete="username" maxLength={254} required disabled={busy} value={email} onChange={e => setEmail(e.target.value)} /></>}
        {(!mfa || account) && <><label htmlFor="operator-password">Parola</label><input id="operator-password" type="password" autoComplete="current-password" required maxLength={1024} disabled={busy} value={password} onChange={e => setPassword(e.target.value)} /></>}
        {(mfa || key) && <><label htmlFor="operator-code">{recovery ? 'Kurtarma kodu' : 'Doğrulayıcı uygulama kodu'}</label><input id="operator-code" autoComplete="one-time-code" inputMode={recovery ? 'text' : 'numeric'} required maxLength={recovery ? 32 : 6} pattern={recovery ? undefined : '[0-9]{6}'} disabled={busy} value={code} onChange={e => setCode(e.target.value)} /></>}
        <button disabled={busy} type="submit">{busy ? 'Doğrulanıyor…' : account ? key ? 'İki aşamalı girişi aç' : 'Kuruluma başla' : mfa ? 'Doğrula' : 'Giriş yap'}</button>
      </form>
      {mfa && <button disabled={busy} onClick={() => { setRecovery(!recovery); setCode('') }}>{recovery ? 'Uygulama kodu kullan' : 'Kurtarma kodu kullan'}</button>}
      {account && <button disabled={busy} onClick={() => void logout()}>Çıkış yap</button>}
    </main>}
  </div>
}
