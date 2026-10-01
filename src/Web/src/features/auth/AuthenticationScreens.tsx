import ErrorMessage from '../../components/ErrorMessage'
import PasswordResetForm from './PasswordResetForm'
import StaffInvitationAcceptForm from './StaffInvitationAcceptForm'
import type useAuthentication from './useAuthentication'
import { postWithCsrf } from '../../app/api'
import styles from './AuthenticationScreens.module.css'

export default function AuthenticationScreens({ auth }: { auth: ReturnType<typeof useAuthentication> }) {
  const {
    account, setAccount, loading, busy, error, setError, email, setEmail,
    password, setPassword, mfaRequired, setMfaRequired, useRecoveryCode, setUseRecoveryCode,
    code, setCode, setupInfo, setupPassword, setSetupPassword, recoveryCodes, setRecoveryCodes,
    notice, setNotice, resettingPassword, setResettingPassword,
    resettingStaffPassword, setResettingStaffPassword, acceptingInvitation, setAcceptingInvitation,
    clearPasswordFields, handleLogin, handleMfaLogin, handleSetup, handleEnable, handleLogout,
  } = auth
  return (
    <main className={styles.page}>
      <section className={styles.card} aria-labelledby="page-title">
        <span className={styles.eyebrow}>Randevu · İşletme paneli</span>
        {loading ? (
          <p role="status">Oturum kontrol ediliyor…</p>
        ) : acceptingInvitation ? (
          <StaffInvitationAcceptForm post={body => postWithCsrf('/api/staff-invitations/accept', body)}
            onCancel={() => setAcceptingInvitation(false)} onDone={() => {
              setAcceptingInvitation(false)
              setPassword('')
              setNotice('Çalışan hesabınız açıldı. E-postanız ve belirlediğiniz parolayla giriş yapın.')
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
            <form className={styles.loginForm} onSubmit={handleLogin}>
              <label htmlFor="email">E-posta</label>
              <input id="email" type="email" autoComplete="username" required
                value={email} onChange={event => setEmail(event.target.value)} />
              <div className={styles.passwordHeading}>
                <label htmlFor="password">Parola</label>
                <button className={styles.textAction} type="button" disabled={busy} onClick={() => {
                  setResettingPassword(true)
                  setResettingStaffPassword(false)
                  setPassword('')
                  setError('')
                  setNotice('')
                }}>Parolamı unuttum</button>
              </div>
              <input id="password" type="password" autoComplete="current-password" required
                value={password} onChange={event => setPassword(event.target.value)} />
              <button type="submit" disabled={busy}>
                {busy ? 'Giriş yapılıyor…' : 'Giriş yap'}
              </button>
            </form>
            <div className={styles.loginOptions} role="group" aria-label="Çalışan giriş seçenekleri">
              <button className={styles.textAction} type="button" disabled={busy} onClick={() => {
                setResettingPassword(true)
                setResettingStaffPassword(true)
                setPassword('')
                setError('')
                setNotice('')
              }}>Çalışan parolamı unuttum</button>
              <button className={styles.textAction} type="button" disabled={busy} onClick={() => {
                setAcceptingInvitation(true)
                setPassword('')
                setError('')
                setNotice('')
              }}>Çalışan davetim var</button>
            </div>
          </>
        )}
        <ErrorMessage message={error} />
        {notice && <p role="status">{notice}</p>}
      </section>
      <aside className={styles.hero} aria-label="İşletme paneli hakkında">
        <div className={styles.heroContent}>
          <span className={styles.badge}>Berber ve kuaför işletmeleri için</span>
          <h2>İşletmenize odaklanın.<br /><span>Kontrol sizde olsun.</span></h2>
          <p>İşletme bilgilerinizi ve ekibinizin erişimlerini tek yerden yönetin.</p>
          <div className={styles.highlights}>
            <div><strong>Güvenli erişim</strong><p>İşletme sahibi hesabında iki aşamalı giriş.</p></div>
            <div><strong>Size ait bilgiler</strong><p>İşletme profiliniz ve kontrollü çalışan davetleri.</p></div>
          </div>
        </div>
      </aside>
    </main>
  )
}
