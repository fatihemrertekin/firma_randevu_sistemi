import { useEffect, useRef, useState, type FormEvent } from 'react'
import PasswordChangeForm from './PasswordChangeForm'
import PasswordResetForm from './PasswordResetForm'
import StaffInvitations from './StaffInvitations'
import StaffInvitationAcceptForm from './StaffInvitationAcceptForm'
import StaffPasswordReset from './StaffPasswordReset'
import BusinessProfile from './BusinessProfile'
import styles from './App.module.css'

type Account = { email: string; mfaEnabled: boolean; ownerAccess: boolean; staffAccess: boolean }
type SetupInfo = { key: string; uri: string }

async function getAccount(): Promise<Account | null> {
  const response = await fetch('/api/auth/me')
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Hesap bilgisi alınamadı.')
  return (await response.json()) as Account
}

async function postWithCsrf(path: string, body: object, signal?: AbortSignal): Promise<Response> {
  const tokenResponse = await fetch('/api/auth/csrf', { cache: 'no-store', ...(signal ? { signal } : {}) })
  if (!tokenResponse.ok) throw new Error('İstek doğrulaması alınamadı.')
  const { token } = (await tokenResponse.json()) as { token: string }
  return fetch(path, {
    method: 'POST',
    ...(signal ? { signal } : {}),
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
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [notice, setNotice] = useState('')
  const passwordChangePending = useRef(false)
  const [resettingPassword, setResettingPassword] = useState(false)
  const [resettingStaffPassword, setResettingStaffPassword] = useState(false)
  const [acceptingInvitation, setAcceptingInvitation] = useState(false)

  function clearPasswordFields() {
    setCurrentPassword('')
    setNewPassword('')
    setConfirmPassword('')
  }

  async function handlePasswordChange(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy || passwordChangePending.current) return
    setError('')
    setNotice('')
    if (newPassword !== confirmPassword) {
      setError('Yeni parola ve tekrarı aynı olmalı.')
      return
    }
    passwordChangePending.current = true
    setBusy(true)
    try {
      const response = await postWithCsrf('/api/auth/change-password', {
        currentPassword, newPassword, confirmPassword,
      })
      clearPasswordFields()
      if (response.status === 401 || response.status === 403 || response.status === 409) {
        setAccount(null)
        setMfaRequired(false)
        setCode('')
        setError('Oturum geçersiz. Yeniden giriş yapın.')
        return
      }
      if (!response.ok) {
        if (response.status === 429) {
          setError('Çok fazla deneme. Daha sonra tekrar deneyin.')
        } else if (response.status === 400) {
          const problem = (await response.json()) as { title?: string }
          setError(problem.title ?? 'Parola alanlarını kontrol edin.')
        } else {
          setError('Sonuç doğrulanamadı. Yeniden giriş yapmayı deneyin.')
        }
        return
      }
      setAccount(null)
      setMfaRequired(false)
      setUseRecoveryCode(false)
      setSetupInfo(null)
      setRecoveryCodes(null)
      setSetupPassword('')
      setPassword('')
      setCode('')
      setNotice(account?.staffAccess && !account.ownerAccess
        ? 'Parolanız değişti ve bütün oturumlar kapatıldı. Yeni parolanızla yeniden giriş yapın.'
        : 'Parolanız değişti ve bütün oturumlar kapatıldı. Yeni parolanız ve ikinci adımla yeniden giriş yapın.')
    } catch {
      clearPasswordFields()
      setError('Sonuç doğrulanamadı. Yeniden giriş yapmayı deneyin.')
    } finally {
      passwordChangePending.current = false
      setBusy(false)
    }
  }

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
    setNotice('')
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
      clearPasswordFields()
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
        ) : acceptingInvitation ? (
          <StaffInvitationAcceptForm post={body => postWithCsrf('/api/staff-invitations/accept', body)}
            onCancel={() => setAcceptingInvitation(false)} onDone={() => {
              setAcceptingInvitation(false)
              setPassword('')
              setNotice('Staff hesabınız açıldı. E-postanız ve belirlediğiniz parolayla giriş yapın.')
            }} />
        ) : resettingPassword ? (
          <PasswordResetForm staff={resettingStaffPassword} onRequest={body => postWithCsrf(
            resettingStaffPassword ? '/api/staff-password-resets/complete' : '/api/auth/reset-password', body)}
            onCancel={() => setResettingPassword(false)} onDone={() => {
              setResettingPassword(false)
              setAccount(null)
              setMfaRequired(false)
              setPassword('')
              setCode('')
              clearPasswordFields()
              setNotice(resettingStaffPassword
                ? 'Parolanız sıfırlandı ve bütün oturumlar kapatıldı. Yeni parolanızla yeniden giriş yapın.'
                : 'Parolanız sıfırlandı ve bütün oturumlar kapatıldı. Yeni parolanız ve ikinci adımla yeniden giriş yapın.')
            }} />
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
          account.staffAccess ? (
            <>
              <h1 id="page-title">Staff hesabınız açık</h1>
              <p>{account.email}</p>
              <p>Çalışan ekranları hazırlanıyor. İşletme yönetimi erişiminiz yok.</p>
              <PasswordChangeForm requiresSecondFactor={account.mfaEnabled || account.ownerAccess}
                currentPassword={currentPassword} newPassword={newPassword}
                confirmPassword={confirmPassword} busy={busy}
                onCurrentPassword={setCurrentPassword} onNewPassword={setNewPassword}
                onConfirmPassword={setConfirmPassword} onSubmit={handlePasswordChange} />
              <button type="button" onClick={handleLogout} disabled={busy}>Çıkış yap</button>
            </>
          ) : account.mfaEnabled && account.ownerAccess ? (
            <>
              <h1 id="page-title">Hoş geldiniz</h1>
              <p>{account.email}</p>
              <p>İki aşamalı giriş açık. Yönetim ekranları hazırlanıyor.</p>
              <BusinessProfile post={postWithCsrf} />
              <PasswordChangeForm currentPassword={currentPassword} newPassword={newPassword}
                confirmPassword={confirmPassword} busy={busy}
                onCurrentPassword={setCurrentPassword} onNewPassword={setNewPassword}
                onConfirmPassword={setConfirmPassword} onSubmit={handlePasswordChange} />
              <StaffInvitations post={postWithCsrf} />
              <StaffPasswordReset post={postWithCsrf} />
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
            <button type="button" disabled={busy} onClick={() => {
              setResettingPassword(true)
              setResettingStaffPassword(false)
              setPassword('')
              setError('')
              setNotice('')
            }}>Parolamı unuttum</button>
            <button type="button" disabled={busy} onClick={() => {
              setResettingPassword(true)
              setResettingStaffPassword(true)
              setPassword('')
              setError('')
              setNotice('')
            }}>Staff parolamı unuttum</button>
            <button type="button" disabled={busy} onClick={() => {
              setAcceptingInvitation(true)
              setPassword('')
              setError('')
              setNotice('')
            }}>Staff davetim var</button>
          </>
        )}
        {error && <p className={styles.error} role="alert">{error}</p>}
        {notice && <p role="status">{notice}</p>}
      </section>
    </main>
  )
}
