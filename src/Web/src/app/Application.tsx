import { Navigate, useLocation, useNavigate } from 'react-router'
import useAuthentication from '../features/auth/useAuthentication'
import AuthenticationScreens from '../features/auth/AuthenticationScreens'
import ManagementLayout from './ManagementLayout'
import { authPaths, resolveRoute, safeReturnPath, sectionPaths } from './routes'
import useLinkProof, { type LinkProof } from '../features/auth/useLinkProof'
import OwnerEmailConfirmation from '../features/auth/OwnerEmailConfirmation'
import LinkPasswordReset from '../features/auth/LinkPasswordReset'
import AuthenticationLayout from '../features/auth/AuthenticationLayout'
import { postWithCsrf } from './api'
import NavigationLink from './NavigationLink'

export default function Application({ initialProof }: { initialProof: () => LinkProof }) {
  const auth = useAuthentication(), location = useLocation(), navigate = useNavigate()
  const { proof, clearProof } = useLinkProof(initialProof)
  const route = resolveRoute(location.pathname)
  const account = auth.account
  const ready = !auth.loading && !auth.recoveryCodes && account &&
    (account.staffAccess || (account.mfaEnabled && account.ownerAccess))
  const owner = account?.mfaEnabled && account.ownerAccess && !account.staffAccess
  const home = owner ? sectionPaths.business : sectionPaths.security
  if (proof?.kind === 'reset') return <LinkPasswordReset key={proof.token} linkToken={proof.token} onRequest={body => postWithCsrf('/api/auth/reset-password', body,
      AbortSignal.timeout(15000))} onCancel={() => { clearProof(); void navigate(authPaths.login, { replace: true }) }} onDone={() => {
      clearProof(); auth.setAccount(null); auth.setMfaRequired(false); auth.setUseRecoveryCode(false)
      auth.setPassword(''); auth.setCode(''); auth.clearPasswordFields()
      auth.setNotice('Parolanız sıfırlandı. Yeni parolanız ve ikinci adımla yeniden giriş yapın.')
      void navigate(authPaths.login, { replace: true })
    }} />
  if (proof?.kind === 'verify') return <OwnerEmailConfirmation key={proof.token} token={proof.token}
    onClose={() => { clearProof(); void navigate(authPaths.login, { replace: true }) }} />
  if (auth.loading) return <AuthenticationScreens auth={auth} />
  if (route.kind === 'home') return <Navigate replace to={ready ? home : authPaths.login} />
  if (route.kind === 'management' && !ready) return <Navigate replace
    to={authPaths.login + '?donus=' + encodeURIComponent(safeReturnPath(location.pathname + location.search) ?? sectionPaths.security)} />
  if (route.kind === 'auth' && ready) {
    const requested = safeReturnPath(new URLSearchParams(location.search).get('donus'))
    return <Navigate replace to={requested && (owner || requested.split('?')[0] === sectionPaths.security) ? requested : home} />
  }
  if (ready && account) return <ManagementLayout auth={auth} account={account} />
  if (route.kind === 'missing' || (route.kind === 'auth' && route.page === 'verify')) return <AuthenticationLayout>
    <h1 id="page-title">{route.kind === 'missing' ? 'Sayfa bulunamadı' : 'Doğrulama bağlantısı gerekli'}</h1>
    <p>{route.kind === 'missing' ? 'Bu adres uygulamada bulunmuyor.' : 'E-postanızdaki doğrulama bağlantısını yeniden açın.'}</p>
    <NavigationLink to={authPaths.login}>Girişe dön</NavigationLink>
  </AuthenticationLayout>
  return <AuthenticationScreens auth={auth} />
}
