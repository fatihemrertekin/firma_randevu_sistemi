import { useEffect, useState, type FormEvent } from 'react'
import styles from './App.module.css'

type Account = { email: string; mfaEnabled: boolean; ownerAccess: boolean }
type SetupInfo = { key: string; uri: string }

async function getAccount(): Promise<Account | null> {
  const response = await fetch('/api/auth/me')
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Hesap bilgisi alınamadı.')
  return (await response.json()) as Account
}

async function postWithCsrf(path: string, body: object): Promise<Response> {
  const tokenResponse = await fetch('/api/auth/csrf', { cache: 'no-store' })
  if (!tokenResponse.ok) throw new Error('İstek doğrulaması alınamadı.')
  const { token } = (await tokenResponse.json()) as { token: string }
  return fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: JSON.stringify(body),
  })
}

export default function App() {
  const [account, setAccount] = useState<Account | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [mfaRequired, setMfaRequired] = useState(false)
  const [useRecoveryCode, setUseRecoveryCode] = useState(false)
  const [code, setCode] = useState('')
  const [setupInfo, setSetupInfo] = useState<SetupInfo | null>(null)
  const [setupPassword, setSetupPassword] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)

  useEffect(() => {
    getAccount()
      .then(setAccount)
      .catch(() => setError('Oturum durumu alınamadı. Sayfayı yenileyin.'))
      .finally(() => setLoading(false))
  }, [])

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const response = await postWithCsrf('/api/auth/login', { email, password })
      setPassword('')
      if (response.status === 202) {
        setMfaRequired(true)
        return
      }
      if (!response.ok) {
        setError(response.status === 401 ? 'E-posta veya parola hatalı.' : 'Giriş yapılamadı.')
        return
      }
      setAccount(await getAccount())
    } catch {
      setError('Giriş yapılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleMfaLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const path = useRecoveryCode ? '/api/auth/mfa/recovery-login' : '/api/auth/mfa/login'
      const response = await postWithCsrf(path, { code })
      if (!response.ok) {
        setError('Kod doğrulanamadı. Lütfen yeniden deneyin.')
        return
      }
      setCode('')
      setMfaRequired(false)
      setUseRecoveryCode(false)
      setAccount(await getAccount())
    } catch {
      setError('Kod doğrulanamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleSetup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const response = await postWithCsrf('/api/auth/mfa/setup', { password: setupPassword })
      if (!response.ok) {
        setError(response.status === 401 ? 'Parola doğrulanamadı.' : 'Kurulum başlatılamadı.')
        return
      }
      setSetupInfo((await response.json()) as SetupInfo)
      setSetupPassword('')
    } catch {
      setError('Kurulum başlatılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleEnable(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const response = await postWithCsrf('/api/auth/mfa/enable', {
        password: setupPassword,
        code,
      })
      if (!response.ok) {
        setError('Parola veya doğrulama kodu hatalı.')
        return
      }
      const body = (await response.json()) as { recoveryCodes: string[] }
      setRecoveryCodes(body.recoveryCodes)
      setSetupInfo(null)
      setSetupPassword('')
      setCode('')
      setAccount(null)
    } catch {
      setError('İki aşamalı giriş açılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  async function handleLogout() {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const response = await postWithCsrf('/api/auth/logout', {})
      if (!response.ok) throw new Error('Çıkış başarısız.')
      setAccount(null)
      setSetupInfo(null)
      setSetupPassword('')
    } catch {
      setError('Çıkış yapılamadı. Lütfen yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className={styles.page}>
      <section className={styles.card} aria-labelledby="page-title">
        <span className={styles.eyebrow}>Randevu</span>
        {loading ? (
          <p role="status">Oturum kontrol ediliyor…</p>
        ) : recoveryCodes ? (
          <>
            <h1 id="page-title">Kurtarma kodlarını saklayın</h1>
            <p>Bu kodların her biri yalnız bir kez kullanılır. Güvenli bir yere kaydedin; tekrar gösterilmeyecekler.</p>
            <ul className={styles.codes}>
              {recoveryCodes.map(recoveryCode => <li key={recoveryCode}><code>{recoveryCode}</code></li>)}
            </ul>
            <button type="button" onClick={() => setRecoveryCodes(null)}>Kodları kaydettim</button>
          </>
        ) : account ? (
          account.mfaEnabled && account.ownerAccess ? (
            <>
              <h1 id="page-title">Hoş geldiniz</h1>
              <p>{account.email}</p>
              <p>İki aşamalı giriş açık. Yönetim ekranları hazırlanıyor.</p>
              <button type="button" onClick={handleLogout} disabled={busy}>
                {busy ? 'Çıkış yapılıyor…' : 'Çıkış yap'}
              </button>
            </>
          ) : account.mfaEnabled ? (
            <>
              <h1 id="page-title">Yeniden giriş yapın</h1>
              <p>İki aşamalı giriş etkin. Hesaba devam etmek için yeniden giriş yapın.</p>
              <button type="button" onClick={handleLogout} disabled={busy}>Çıkış yap</button>
            </>
          ) : (
            <>
              <h1 id="page-title">İki aşamalı girişi kurun</h1>
              <p>Owner hesabını korumak için doğrulayıcı uygulamanıza bir hesap ekleyin.</p>
              {setupInfo ? (
                <>
                  <p>Uygulamada “anahtar gir” seçeneğini açıp aşağıdaki anahtarı kullanın:</p>
                  <p className={styles.secret}><code>{setupInfo.key}</code></p>
                  <form onSubmit={handleEnable}>
                    <label htmlFor="enable-password">Parolanızı yeniden girin</label>
                    <input id="enable-password" type="password" autoComplete="current-password" required
                      value={setupPassword} onChange={event => setSetupPassword(event.target.value)} />
                    <label htmlFor="enable-code">Uygulamadaki altı haneli kod</label>
                    <input id="enable-code" type="text" inputMode="numeric" autoComplete="one-time-code" required
                      value={code} onChange={event => setCode(event.target.value)} />
                    <button type="submit" disabled={busy}>
                      {busy ? 'Açılıyor…' : 'İki aşamalı girişi aç'}
                    </button>
                  </form>
                </>
              ) : (
                <form onSubmit={handleSetup}>
                  <label htmlFor="setup-password">Parolanız</label>
                  <input id="setup-password" type="password" autoComplete="current-password" required
                    value={setupPassword} onChange={event => setSetupPassword(event.target.value)} />
                  <button type="submit" disabled={busy}>
                    {busy ? 'Hazırlanıyor…' : 'Kurulumu başlat'}
                  </button>
                </form>
              )}
              <button type="button" onClick={handleLogout} disabled={busy}>Çıkış yap</button>
            </>
          )
        ) : mfaRequired ? (
          <>
            <h1 id="page-title">İkinci adımı tamamlayın</h1>
            <form onSubmit={handleMfaLogin}>
              <label htmlFor="mfa-code">
                {useRecoveryCode ? 'Kurtarma kodu' : 'Doğrulayıcı uygulama kodu'}
              </label>
              <input id="mfa-code" type="text" required
                inputMode={useRecoveryCode ? 'text' : 'numeric'} autoComplete="one-time-code"
                value={code} onChange={event => setCode(event.target.value)} />
              <button type="submit" disabled={busy}>
                {busy ? 'Doğrulanıyor…' : 'Doğrula'}
              </button>
            </form>
            <button type="button" onClick={() => { setUseRecoveryCode(!useRecoveryCode); setCode(''); setError('') }}>
              {useRecoveryCode ? 'Uygulama kodu kullan' : 'Kurtarma kodu kullan'}
            </button>
            <button type="button" onClick={() => { setMfaRequired(false); setCode(''); setError('') }}>
              Girişe dön
            </button>
          </>
        ) : (
          <>
            <h1 id="page-title">İşletme girişi</h1>
            <form onSubmit={handleLogin}>
              <label htmlFor="email">E-posta</label>
              <input id="email" type="email" autoComplete="username" required
                value={email} onChange={event => setEmail(event.target.value)} />
              <label htmlFor="password">Parola</label>
              <input id="password" type="password" autoComplete="current-password" required
                value={password} onChange={event => setPassword(event.target.value)} />
              <button type="submit" disabled={busy}>
                {busy ? 'Giriş yapılıyor…' : 'Giriş yap'}
              </button>
            </form>
          </>
        )}
        {error && <p className={styles.error} role="alert">{error}</p>}
      </section>
    </main>
  )
}
