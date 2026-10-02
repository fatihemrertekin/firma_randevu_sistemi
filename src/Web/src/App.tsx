import useAuthentication from './features/auth/useAuthentication'
import AuthenticationScreens from './features/auth/AuthenticationScreens'
import ManagementLayout from './app/ManagementLayout'
import { useEffect, useState } from 'react'
import OwnerEmailConfirmation from './features/auth/OwnerEmailConfirmation'
import PasswordResetForm from './features/auth/PasswordResetForm'
import { postWithCsrf } from './app/api'
import styles from './features/auth/AuthenticationScreens.module.css'

export default function App() {
  const auth = useAuthentication()
  const [resetToken, setResetToken] = useState<string | null>(() => typeof window === 'undefined'
    ? null : new URLSearchParams(window.location.hash.slice(1)).get('reset-owner-password'))
  const [verificationToken, setVerificationToken] = useState<string | null>(() => typeof window === 'undefined'
    ? null : new URLSearchParams(window.location.hash.slice(1)).get('verify-owner-email'))
  useEffect(() => {
    const capture = () => {
      const reset = new URLSearchParams(window.location.hash.slice(1)).get('reset-owner-password')
      if (reset !== null) {
        window.history.replaceState(null, '', window.location.pathname + window.location.search)
        setResetToken(reset); setVerificationToken(null)
      }
      const token = new URLSearchParams(window.location.hash.slice(1)).get('verify-owner-email')
      if (token !== null) {
        // Keep the proof only in memory; opening a link does not consume it.
        window.history.replaceState(null, '', window.location.pathname + window.location.search)
        setVerificationToken(token); setResetToken(null)
      }
    }
    if (new URLSearchParams(window.location.hash.slice(1)).has('verify-owner-email') ||
      new URLSearchParams(window.location.hash.slice(1)).has('reset-owner-password')) {
      window.history.replaceState(null, '', window.location.pathname + window.location.search)
    }
    window.addEventListener('hashchange', capture)
    return () => window.removeEventListener('hashchange', capture)
  }, [])
  if (resetToken !== null) return <main className={`${styles.page} ${styles.confirmationPage}`}>
    <section className={`${styles.card} ${styles.confirmationCard}`} aria-labelledby="page-title">
      <span className={styles.eyebrow}>Randevu · İşletme paneli</span>
      <PasswordResetForm key={resetToken} linkToken={resetToken} onRequest={body => postWithCsrf('/api/auth/reset-password', body,
        AbortSignal.timeout(15000))} onCancel={() => setResetToken(null)} onDone={() => {
        setResetToken(null); auth.setAccount(null); auth.setMfaRequired(false)
        auth.setResettingPassword(false); auth.setResettingStaffPassword(false); auth.setAcceptingInvitation(false)
        auth.setUseRecoveryCode(false)
        auth.setPassword(''); auth.setCode(''); auth.clearPasswordFields()
        auth.setNotice('Parolanız sıfırlandı. Yeni parolanız ve ikinci adımla yeniden giriş yapın.')
      }} />
    </section>
  </main>
  if (verificationToken !== null) return <OwnerEmailConfirmation key={verificationToken} token={verificationToken}
    onClose={() => setVerificationToken(null)} />
  const account = auth.account
  if (!auth.loading && !auth.recoveryCodes && account &&
    (account.staffAccess || (account.mfaEnabled && account.ownerAccess))) {
    return <ManagementLayout auth={auth} account={account} />
  }
  return <AuthenticationScreens auth={auth} />
}
