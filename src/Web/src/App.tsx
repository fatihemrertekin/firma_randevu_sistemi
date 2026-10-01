import useAuthentication from './features/auth/useAuthentication'
import AuthenticationScreens from './features/auth/AuthenticationScreens'
import ManagementLayout from './app/ManagementLayout'
import { useEffect, useState } from 'react'
import OwnerEmailConfirmation from './features/auth/OwnerEmailConfirmation'

export default function App() {
  const auth = useAuthentication()
  const [verificationToken, setVerificationToken] = useState<string | null>(() => typeof window === 'undefined'
    ? null : new URLSearchParams(window.location.hash.slice(1)).get('verify-owner-email'))
  useEffect(() => {
    const capture = () => {
      const token = new URLSearchParams(window.location.hash.slice(1)).get('verify-owner-email')
      if (token !== null) {
        // Keep the proof only in memory; opening a link does not consume it.
        window.history.replaceState(null, '', window.location.pathname + window.location.search)
        setVerificationToken(token)
      }
    }
    if (new URLSearchParams(window.location.hash.slice(1)).has('verify-owner-email')) {
      window.history.replaceState(null, '', window.location.pathname + window.location.search)
    }
    window.addEventListener('hashchange', capture)
    return () => window.removeEventListener('hashchange', capture)
  }, [])
  if (verificationToken !== null) return <OwnerEmailConfirmation key={verificationToken} token={verificationToken}
    onClose={() => setVerificationToken(null)} />
  const account = auth.account
  if (!auth.loading && !auth.recoveryCodes && account &&
    (account.staffAccess || (account.mfaEnabled && account.ownerAccess))) {
    return <ManagementLayout auth={auth} account={account} />
  }
  return <AuthenticationScreens auth={auth} />
}
