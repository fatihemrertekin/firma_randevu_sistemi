import { useCallback, useRef, useState } from 'react'
import type { Account } from './api'
import { postWithCsrf } from './api'
import type useAuthentication from '../features/auth/useAuthentication'
import PasswordChangeForm from '../features/auth/PasswordChangeForm'
import BusinessProfile from '../features/business/BusinessProfile'
import StaffInvitations from '../features/staff/StaffInvitations'
import StaffPasswordReset from '../features/staff/StaffPasswordReset'
import styles from './ManagementLayout.module.css'
import ErrorMessage from '../components/ErrorMessage'
import OwnerRecoveryEmail from '../features/auth/OwnerRecoveryEmail'

type Section = 'business' | 'security' | 'access'
const sections = [
  { id: 'business', title: 'İşletme bilgileri', description: 'İşletmenizin adını ve iletişim bilgilerini yönetin.' },
  { id: 'security', title: 'Hesap ve güvenlik', description: 'Hesabınızı koruyun ve parolanızı değiştirin.' },
  { id: 'access', title: 'Çalışan erişimleri', description: 'Çalışanları davet edin ve parola sıfırlama kodu oluşturun.' },
] as const

type Props = { auth: ReturnType<typeof useAuthentication>; account: Account }

export default function ManagementLayout({ auth, account }: Props) {
  const owner = account.mfaEnabled && account.ownerAccess && !account.staffAccess
  const [section, setSection] = useState<Section>(owner ? 'business' : 'security')
  const [pendingRequests, setPendingRequests] = useState(0)
  const heading = useRef<HTMLHeadingElement>(null)
  const blocked = auth.busy || pendingRequests > 0
  const current = sections.find(item => item.id === section) ?? sections[1]
  const post = useCallback(async (path: string, body: object, signal?: AbortSignal) => {
    setPendingRequests(current => current + 1)
    try { return await postWithCsrf(path, body, signal) }
    finally { setPendingRequests(current => current - 1) }
  }, [])

  function navigate(next: Section) {
    if (blocked || section === next) return
    auth.clearPasswordFields()
    auth.setError('')
    auth.setNotice('')
    setSection(next)
    heading.current?.focus()
  }

  return <div className={styles.page}>
    <header className={styles.header}>
      <div><span className={styles.brand}>Randevu</span><span className={styles.subtitle}>Yönetim</span></div>
      <div className={styles.account}><span>{account.email}</span>
        <button type="button" disabled={blocked} onClick={auth.handleLogout}>Çıkış yap</button>
      </div>
    </header>
    <div className={styles.layout}>
      <aside className={styles.sidebar}>
        <p className={styles.role}>{owner ? 'İşletme sahibi' : 'Çalışan'}</p>
        <nav aria-label="Yönetim bölümleri">
          {sections.filter(item => owner || item.id === 'security').map(item =>
            <button key={item.id} type="button" aria-current={section === item.id ? 'page' : undefined}
              disabled={blocked} onClick={() => navigate(item.id)}>{item.title}</button>)}
        </nav>
      </aside>
      <main className={styles.content}>
        <h1 ref={heading} tabIndex={-1}>{current.title}</h1>
        <p>{current.description}</p>
        {owner && <div hidden={section !== 'business'} className={styles.panel}>
          <BusinessProfile post={post} />
        </div>}
        {owner && section === 'security' && <div className={styles.panel}>
          <OwnerRecoveryEmail post={post} disabled={blocked} />
        </div>}
        {section === 'security' && <div className={styles.panel}>
          <p>{account.mfaEnabled ? 'İki aşamalı giriş açık.' : 'Çalışan hesabınız açık.'}</p>
          <PasswordChangeForm requiresSecondFactor={account.mfaEnabled || account.ownerAccess}
            mismatch={auth.error === 'Yeni parola ve tekrarı aynı olmalı.'}
            currentPassword={auth.currentPassword} newPassword={auth.newPassword}
            confirmPassword={auth.confirmPassword} busy={auth.busy}
            onCurrentPassword={auth.setCurrentPassword} onNewPassword={auth.setNewPassword}
            onConfirmPassword={auth.setConfirmPassword} onSubmit={auth.handlePasswordChange} />
          <ErrorMessage message={auth.error} />
          {auth.notice && <p role="status">{auth.notice}</p>}
        </div>}
        {owner && section === 'access' && <>
          <p>Bölümden ayrıldığınızda ekrandaki teslim kodları temizlenir.</p>
          <div className={styles.panel}><StaffInvitations post={post} /></div>
          <div className={styles.panel}><StaffPasswordReset post={post} /></div>
        </>}
        {section !== 'security' && <ErrorMessage message={auth.error} />}
      </main>
    </div>
  </div>
}
