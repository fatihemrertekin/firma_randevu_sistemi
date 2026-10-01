import useAuthentication from './features/auth/useAuthentication'
import AuthenticationScreens from './features/auth/AuthenticationScreens'
import ManagementLayout from './app/ManagementLayout'

export default function App() {
  const auth = useAuthentication()
  const account = auth.account
  if (!auth.loading && !auth.recoveryCodes && account &&
    (account.staffAccess || (account.mfaEnabled && account.ownerAccess))) {
    return <ManagementLayout auth={auth} account={account} />
  }
  return <AuthenticationScreens auth={auth} />
}
