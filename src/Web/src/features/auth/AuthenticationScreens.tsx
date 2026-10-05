import ErrorMessage from '../../components/ErrorMessage'
import PasswordResetForm from './PasswordResetForm'
import OwnerPasswordResetRequest from './OwnerPasswordResetRequest'
import StaffInvitationAcceptForm from './StaffInvitationAcceptForm'
import type useAuthentication from './useAuthentication'
import { postWithCsrf } from '../../app/api'
import styles from './AuthenticationScreens.module.css'
import AuthenticationLayout from './AuthenticationLayout'
import { useCallback, useEffect, useEffectEvent, useRef } from 'react'
import { useBlocker, useLocation, useNavigate } from 'react-router'
import { authPaths, resolveRoute } from '../../app/routes'
import NavigationLink from '../../app/NavigationLink'
import { useNavigationChange } from '../../app/NavigationEvents'

export default function AuthenticationScreens({ auth }: { auth: ReturnType<typeof useAuthentication> }) {
  const {
    account, setAccount, loading, busy, error, setError, email, setEmail,
    password, setPassword, mfaRequired, setMfaRequired, useRecoveryCode, setUseRecoveryCode,
    code, setCode, setupInfo, setSetupInfo, setupPassword, setSetupPassword, recoveryCodes, setRecoveryCodes,
    notice, setNotice,
    clearPasswordFields, handleLogin, handleMfaLogin, handleSetup, handleEnable, handleLogout: logout,
  } = auth
  const location = useLocation(), navigate = useNavigate()
  const route = resolveRoute(location.pathname)
  const page = route.kind === 'auth' ? route.page : 'login'
  const resettingStaffPassword = page === 'staffReset'
  const childBusy = useRef(false)
  const reportBusy = useCallback((value: boolean) => { childBusy.current = value }, [])
  const blocker = useBlocker(() => busy || childBusy.current)
  useEffect(() => { if (blocker.state === 'blocked') blocker.reset() }, [blocker])
  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => { if (busy || childBusy.current) { event.preventDefault(); event.returnValue = '' } }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [busy])
  useNavigationChange(() => { setPassword(''); setCode(''); setSetupPassword(''); setSetupInfo(null); clearPasswordFields(); setError('') })
  const title = useEffectEvent(() => { document.title = `${document.getElementById('page-title')?.textContent ?? 'İşletme girişi'} · Randevu` })
  useEffect(() => { title() }, [location.pathname, loading, mfaRequired, recoveryCodes, account])
  function finish() { childBusy.current = false; void navigate(authPaths.login, { replace: true }) }
  function cancel() { void navigate(authPaths.login) }
  function handleLogout() { void logout().then(success => { if (success) finish() }) }
  function clearFeedback() { setPassword(''); setError(''); setNotice('') }
  return (
    <AuthenticationLayout>
        {loading ? (
          <p role="status">Oturum kontrol ediliyor…</p>
        ) : page === 'invitation' ? (
          <StaffInvitationAcceptForm onBusyChange={reportBusy} post={body => postWithCsrf('/api/staff-invitations/accept', body)}
            onCancel={cancel} onDone={() => {
              finish()
              setPassword('')
              setNotice('Çalışan hesabınız açıldı. E-postanız ve belirlediğiniz parolayla giriş yapın.')
            }} />
        ) : page === 'reset' ? (
          <OwnerPasswordResetRequest onBusyChange={reportBusy} onManual={() => { void navigate(authPaths.resetCode) }} onCancel={cancel} onDone={() => {
            finish(); setAccount(null); setMfaRequired(false)
            setPassword(''); setCode(''); clearPasswordFields()
            setNotice('Parolanız sıfırlandı. Yeni parolanız ve ikinci adımla yeniden giriş yapın.')
          }} />
        ) : page === 'resetCode' || resettingStaffPassword ? (
          <PasswordResetForm key={page} onBusyChange={reportBusy} staff={resettingStaffPassword} onRequest={body => postWithCsrf(
            resettingStaffPassword ? '/api/staff-password-resets/complete' : '/api/auth/reset-password', body)}
            onCancel={cancel} onDone={() => {
              finish()
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
          account.mfaEnabled ? (
            <>
              <h1 id="page-title">Yeniden giriş yapın</h1>
              <p>İki aşamalı giriş etkin. Hesaba devam etmek için yeniden giriş yapın.</p>
              <button type="button" onClick={handleLogout} disabled={busy}>Çıkış yap</button>
            </>
          ) : (
            <>
              <h1 id="page-title">İki aşamalı girişi kurun</h1>
              <p>İşletme sahibi hesabını korumak için doğrulayıcı uygulamanıza bir hesap ekleyin.</p>
              {setupInfo ? (
                <>
                  <p>Uygulamada “anahtar gir” seçeneğini açıp aşağıdaki anahtarı kullanın:</p>
                  <p className={styles.secret}><code>{setupInfo.key}</code></p>
                  <form onSubmit={handleEnable}>
                    <label htmlFor="enable-password">Parolanızı yeniden girin</label>
                    <input id="enable-password" type="password" autoComplete="current-password" required disabled={busy} maxLength={1024}
                      value={setupPassword} onChange={event => setSetupPassword(event.target.value)} />
                    <label htmlFor="enable-code">Uygulamadaki altı haneli kod</label>
                    <input id="enable-code" type="text" inputMode="numeric" autoComplete="one-time-code" required disabled={busy} pattern="[0-9]{6}" maxLength={6}
                      value={code} onChange={event => setCode(event.target.value)} />
                    <button type="submit" disabled={busy}>
                      {busy ? 'Açılıyor…' : 'İki aşamalı girişi aç'}
                    </button>
                  </form>
                </>
              ) : (
                <form onSubmit={handleSetup}>
                  <label htmlFor="setup-password">Parolanız</label>
                  <input id="setup-password" type="password" autoComplete="current-password" required disabled={busy} maxLength={1024}
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
            <p id="mfa-help">{useRecoveryCode
              ? 'MFA kurulurken sakladığınız kodlardan birini girin. Her kod yalnız bir kez kullanılır; parolanızın yerine geçmez.'
              : 'Doğrulayıcı uygulamanızdaki güncel altı haneli kodu girin. Telefonunuza erişemiyorsanız sakladığınız MFA kurtarma kodunu kullanabilirsiniz.'}</p>
            <form onSubmit={handleMfaLogin}>
              <label htmlFor="mfa-code">
                {useRecoveryCode ? 'Kurtarma kodu' : 'Doğrulayıcı uygulama kodu'}
              </label>
              <input id="mfa-code" type="text" required disabled={busy} aria-describedby="mfa-help"
                pattern={useRecoveryCode ? undefined : '[0-9]{6}'} maxLength={useRecoveryCode ? 32 : 6}
                inputMode={useRecoveryCode ? 'text' : 'numeric'} autoComplete="one-time-code"
                value={code} onChange={event => setCode(event.target.value)} />
              <div className={styles.confirmationActions}><button type="submit" disabled={busy}>
                {busy ? 'Doğrulanıyor…' : 'Doğrula'}
              </button>
              <button type="button" onClick={handleLogout} disabled={busy}>Girişe dön</button></div>
            </form>
            <button className={styles.textAction} type="button" disabled={busy} onClick={() => { setUseRecoveryCode(!useRecoveryCode); setCode(''); setError('') }}>
              {useRecoveryCode ? 'Uygulama kodu kullan' : 'Kurtarma kodu kullan'}
            </button>
          </>
        ) : (
          <>
            <div className={styles.login} data-login-buttons>
            <h1 id="page-title">İşletme girişi</h1>
            <form className={styles.loginForm} onSubmit={handleLogin}>
              <label htmlFor="email">E-posta</label>
              <input id="email" type="email" autoComplete="username" required disabled={busy} maxLength={254}
                value={email} onChange={event => setEmail(event.target.value)} />
              <div className={styles.passwordHeading}>
                <label htmlFor="password">Parola</label>
                <NavigationLink className={styles.textAction} to={authPaths.reset} disabled={busy} onClick={clearFeedback}>Parolamı unuttum</NavigationLink>
              </div>
              <input id="password" type="password" autoComplete="current-password" required disabled={busy} maxLength={1024}
                value={password} onChange={event => setPassword(event.target.value)} />
              <button type="submit" disabled={busy}>
                {busy ? 'Giriş yapılıyor…' : 'Giriş yap'}
              </button>
            </form>
            <div className={styles.loginOptions} role="group" aria-label="Çalışan giriş seçenekleri">
              <NavigationLink className={styles.textAction} to={authPaths.staffReset} disabled={busy} onClick={clearFeedback}>Çalışan parolamı unuttum</NavigationLink>
              <NavigationLink className={styles.textAction} to={authPaths.invitation} disabled={busy} onClick={clearFeedback}>Çalışan davetim var</NavigationLink>
            </div>
            </div>
          </>
        )}
        <ErrorMessage message={error} />
        {notice && <p role="status">{notice}</p>}
    </AuthenticationLayout>
  )
}
