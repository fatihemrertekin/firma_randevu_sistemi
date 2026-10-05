import { useCallback, useEffect, useEffectEvent, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { authPaths, resolveRoute, sectionPaths, type Section } from './routes'
import NavigationLink from './NavigationLink'
import useNavigationGuard from './useNavigationGuard'
import { CompletedNavigation } from './CompletedNavigation'
import { useNavigationChange } from './NavigationEvents'
import type { Account } from './api'
import { postWithCsrf } from './api'
import type useAuthentication from '../features/auth/useAuthentication'
import PasswordChangeForm from '../features/auth/PasswordChangeForm'
import BusinessProfile from '../features/business/BusinessProfile'
import BusinessHours from '../features/business/BusinessHours'
import StaffInvitations from '../features/staff/StaffInvitations'
import StaffPasswordReset from '../features/staff/StaffPasswordReset'
import StaffAccounts from '../features/staff/StaffAccounts'
import StaffMembers from '../features/staff/StaffMembers'
import Services from '../features/services/Services'
import styles from './ManagementLayout.module.css'
import ErrorMessage from '../components/ErrorMessage'
import OwnerRecoveryEmail from '../features/auth/OwnerRecoveryEmail'
import MfaRecoveryCodes from '../features/auth/MfaRecoveryCodes'
import AuditLog from '../features/audit/AuditLog'

type Group = 'business' | 'team' | 'account'
const groups = [
  { id: 'business', title: 'İşletme', initial: 'business' },
  { id: 'team', title: 'Ekip', initial: 'personnel' },
  { id: 'account', title: 'Hesap', initial: 'security' },
] as const
const sectionGroups: Record<Exclude<Section, 'audit'>, Group> = {
  business: 'business', hours: 'business', services: 'business', personnel: 'team', access: 'team', security: 'account',
}

function NavigationIcon({ kind }: { kind: Group | 'audit' }) {
  const paths = {
    business: 'M3 10h18M5 10v11h14V10M3 10l2-7h14l2 7M9 21v-7h6v7M3 10v2h4v-2m2 0v2h6v-2m2 0v2h4v-2',
    team: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M16 3a4 4 0 0 1 0 8M22 21v-2a4 4 0 0 0-3-3.87M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0',
    account: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18M16 10a4 4 0 1 1-8 0 4 4 0 0 1 8 0M5.5 18a7 7 0 0 1 13 0',
    audit: 'M6 2h9l4 4v16H6zM14 2v5h5M9 11h7M9 15h7M9 19h4',
  }
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={paths[kind]} /></svg>
}
const sections = [
  { id: 'business', title: 'İşletme bilgileri', description: 'İşletmenizin adını ve iletişim bilgilerini yönetin.' },
  { id: 'hours', title: 'İşletme saatleri', description: 'Haftalık açılış, kapanış ve kapalı günleri belirleyin.' },
  { id: 'personnel', title: 'Personel', description: 'İşletmede hizmet veren kişilerin adını ve aktiflik durumunu yönetin.' },
  { id: 'services', title: 'Hizmetler', description: 'Hizmetlerin adını, süresini ve fiyatını yönetin.' },
  { id: 'security', title: 'Hesap ve güvenlik', description: 'Hesabınızı koruyun ve parolanızı değiştirin.' },
  { id: 'access', title: 'Çalışan erişimleri', description: 'Çalışan hesaplarını, davetleri ve parola sıfırlama işlemlerini yönetin.' },
  { id: 'audit', title: 'Değişiklik kayıtları', description: 'Yapılan işlemlerin zamanını, yapan hesabı ve ilgili kaydı görüntüleyin.' },
] as const

type Props = { auth: ReturnType<typeof useAuthentication>; account: Account }

export default function ManagementLayout({ auth, account }: Props) {
  const owner = account.mfaEnabled && account.ownerAccess && !account.staffAccess
  const location = useLocation(), go = useNavigate()
  const route = resolveRoute(location.pathname)
  const forbidden = route.kind === 'management' && !owner && route.section !== 'security'
  const section = route.kind === 'management' && !forbidden ? route.section : null
  const [pendingRequests, setPendingRequests] = useState(0)
  const [definitionBusy, setDefinitionBusy] = useState(false)
  const [profileBusy, setProfileBusy] = useState(false)
  const guard = useRef({ dirty: false, profileDirty: false, busy: false })
  const setDefinitionDirty = useCallback((value: boolean) => { guard.current.dirty = value }, [])
  const setProfileDirty = useCallback((value: boolean) => { guard.current.profileDirty = value }, [])
  const [drawerOpen, setDrawerOpen] = useState(true)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const menuButton = useRef<HTMLButtonElement>(null)
  const groupButton = useRef<HTMLAnchorElement>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const blocked = auth.busy || pendingRequests > 0 || definitionBusy || profileBusy
  const current = sections.find(item => item.id === section)
  const currentGroup = section === null || section === 'audit' ? null : sectionGroups[section]
  useEffect(() => { guard.current.busy = blocked }, [blocked])
  useNavigationGuard(guard, useCallback(() => { guard.current.dirty = false }, []))
  const post = useCallback(async (path: string, body: object, signal?: AbortSignal) => {
    setPendingRequests(current => current + 1)
    try { return await postWithCsrf(path, body, signal) }
    finally { setPendingRequests(current => current - 1) }
  }, [])

  useNavigationChange(() => {
    auth.clearPasswordFields()
    auth.setError('')
    auth.setNotice('')
    setMobileMenuOpen(false)
    setDrawerOpen(section !== 'audit')
  })
  const focus = useEffectEvent(() => {
    heading.current?.focus()
    document.title = `${current?.title ?? (forbidden ? 'Erişim izni yok' : 'Sayfa bulunamadı')} · Randevu`
  })
  useEffect(() => { focus() }, [location.pathname, location.search])

  function logout() {
    if (blocked) return
    if ((guard.current.dirty || guard.current.profileDirty) && !window.confirm('Kaydedilmemiş değişiklikler silinsin ve çıkış yapılsın mı?')) return
    void auth.handleLogout().then(success => {
      if (success) { guard.current = { dirty: false, profileDirty: false, busy: false }; void go(authPaths.login, { replace: true }) }
    })
  }

  function closeMenu() {
    setMobileMenuOpen(false)
    setDrawerOpen(false)
    if (window.innerWidth <= 900) menuButton.current?.focus()
    else groupButton.current?.focus()
  }

  return <CompletedNavigation value={(to, options) => { guard.current.dirty = false; guard.current.busy = false; void go(to, options) }}>
    <div className={`management-theme ${styles.page}`} onKeyDown={event => {
    if (event.key === 'Escape' && mobileMenuOpen) { event.stopPropagation(); closeMenu() }
  }}>
    <a className={styles.skipLink} href="#management-main">İçeriğe geç</a>
    <header className={styles.header}>
      <span className={styles.brand}>Randevu</span>
      <button className={styles.mobileToggle} ref={menuButton} type="button" aria-controls="management-navigation"
        aria-expanded={mobileMenuOpen} disabled={blocked} onClick={() => {
          setMobileMenuOpen(value => !value); setDrawerOpen(true)
        }}>Menü</button>
      <div className={styles.account}><div><span>{owner ? 'İşletme sahibi' : 'Çalışan'}</span><span className={styles.email}>{account.email}</span></div>
        <button type="button" disabled={blocked} onClick={logout}>Çıkış yap</button>
      </div>
    </header>
    <div className={styles.layout} data-drawer-open={drawerOpen && currentGroup !== null} data-mobile-open={mobileMenuOpen}>
      <div id="management-navigation" className={styles.navigation}>
        <nav className={styles.rail} aria-label="Yönetim grupları">
          {groups.filter(group => owner || group.id === 'account').map(group => <NavigationLink key={group.id}
            to={sectionPaths[group.initial]} ref={group.id === currentGroup ? groupButton : undefined} disabled={blocked}
            data-group-active={group.id === currentGroup} aria-controls="management-context" onClick={event => {
              if (group.id === currentGroup && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); setDrawerOpen(true) }
            }}><NavigationIcon kind={group.id} /><span>{group.title}</span></NavigationLink>)}
          {owner && <NavigationLink className={styles.auditLink} to={sectionPaths.audit} disabled={blocked}
            aria-current={section === 'audit' ? 'page' : undefined}>
            <NavigationIcon kind="audit" /><span>Değişiklik kayıtları</span>
          </NavigationLink>}
        </nav>
        <aside id="management-context" className={styles.drawer} hidden={!drawerOpen || currentGroup === null}>
          <nav aria-label="Yönetim bölümleri">
            {groups.filter(group => owner || group.id === 'account').map(group => <div key={group.id} hidden={group.id !== currentGroup}>
              <h2>{group.title}</h2>
              {sections.filter(item => item.id !== 'audit' && sectionGroups[item.id] === group.id && (owner || item.id === 'security')).map(item =>
                <NavigationLink key={item.id} to={sectionPaths[item.id]} aria-current={section === item.id ? 'page' : undefined}
                  disabled={blocked} onClick={() => { if (section === item.id) { setMobileMenuOpen(false); heading.current?.focus() } }}>{item.title}</NavigationLink>)}
            </div>)}
            <button className={styles.closeMenu} type="button" disabled={blocked} onClick={closeMenu}>Menüyü kapat</button>
          </nav>
        </aside>
        <div className={styles.mobileAccount}><p>{account.email}</p><button type="button" disabled={blocked} onClick={logout}>Çıkış yap</button></div>
      </div>
      <main id="management-main" className={styles.content}>
        <h1 ref={heading} tabIndex={-1}>{current?.title ?? (forbidden ? 'Erişim izni yok' : 'Sayfa bulunamadı')}</h1>
        <p>{current?.description ?? (forbidden ? 'Hesabınızın bu bölüme erişim izni yok.' : 'Bu adres uygulamada bulunmuyor.')}</p>
        {!current && <NavigationLink to={owner ? sectionPaths.business : sectionPaths.security}>Yetkili ekrana dön</NavigationLink>}
        {owner && <div hidden={section !== 'business'} className={styles.profilePanel}>
          <BusinessProfile post={post} disabled={auth.busy || pendingRequests > 0 || definitionBusy}
            onDirtyChange={setProfileDirty} onBusyChange={setProfileBusy} />
        </div>}
        {owner && section === 'personnel' && <div className={styles.profilePanel}>
          <StaffMembers post={post} onDirtyChange={setDefinitionDirty} onBusyChange={setDefinitionBusy} />
        </div>}
        {owner && section === 'hours' && <div className={styles.profilePanel}>
          <BusinessHours post={post} onDirtyChange={setDefinitionDirty} onBusyChange={setDefinitionBusy} />
        </div>}
        {owner && section === 'services' && <div className={styles.panel}>
          <Services post={post} onDirtyChange={setDefinitionDirty} onBusyChange={setDefinitionBusy} />
        </div>}
        {owner && section === 'audit' && <div className={styles.panel}><AuditLog /></div>}
        {owner && section === 'security' && <div className={styles.panel}>
          <OwnerRecoveryEmail post={post} disabled={blocked} />
        </div>}
        {owner && section === 'security' && <div className={styles.panel}>
          <MfaRecoveryCodes post={post} disabled={blocked} onReplaced={codes => {
            auth.setAccount(null); auth.setRecoveryCodes(codes); auth.setPassword(''); auth.setCode('')
            auth.setMfaRequired(false); auth.setUseRecoveryCode(false); auth.clearPasswordFields()
            auth.setNotice('Eski MFA kurtarma kodları geçersiz. Yeni kodları saklayıp yeniden giriş yapın.')
          }} />
        </div>}
        {section === 'security' && <div className={styles.panel}>
          <p>{account.mfaEnabled ? 'İki aşamalı giriş açık.' : 'Çalışan hesabınız açık.'}</p>
          <PasswordChangeForm requiresSecondFactor={account.mfaEnabled || account.ownerAccess}
            mismatch={auth.error === 'Yeni parola ve tekrarı aynı olmalı.'}
            currentPassword={auth.currentPassword} newPassword={auth.newPassword}
            confirmPassword={auth.confirmPassword} busy={blocked}
            onCurrentPassword={auth.setCurrentPassword} onNewPassword={auth.setNewPassword}
            onConfirmPassword={auth.setConfirmPassword} onSubmit={auth.handlePasswordChange} />
          <ErrorMessage message={auth.error} />
          {auth.notice && <p role="status">{auth.notice}</p>}
        </div>}
        {owner && section === 'access' && <>
          <div className={styles.panel}><StaffAccounts post={post} /></div>
          <p>Bölümden ayrıldığınızda ekrandaki teslim kodları temizlenir.</p>
          <div className={styles.panel}><StaffInvitations post={post} /></div>
          <div className={styles.panel}><StaffPasswordReset post={post} /></div>
        </>}
        {section !== 'security' && <ErrorMessage message={auth.error} />}
      </main>
    </div>
  </div></CompletedNavigation>
}
