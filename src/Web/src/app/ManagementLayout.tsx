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
  { id: 'business', title: 'İşletme' },
  { id: 'team', title: 'Ekip' },
  { id: 'account', title: 'Hesap' },
] as const
const sectionGroups: Record<Exclude<Section, 'audit'>, Group> = {
  business: 'business', hours: 'business', services: 'business', personnel: 'team', access: 'team', security: 'account',
}

function NavigationIcon({ kind }: { kind: Section }) {
  const paths = {
    business: 'M3 10h18M5 10v11h14V10M3 10l2-7h14l2 7M9 21v-7h6v7M3 10v2h4v-2m2 0v2h6v-2m2 0v2h4v-2',
    personnel: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M16 3a4 4 0 0 1 0 8M22 21v-2a4 4 0 0 0-3-3.87M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0',
    hours: 'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0M12 7v5l3 2',
    services: 'M9 6h12M9 12h12M9 18h12M3 6h2M3 12h2M3 18h2',
    access: 'M12 3l8 3v6c0 5-8 9-8 9s-8-4-8-9V6zM8 12l3 3 5-6',
    security: 'M5 10h14v11H5zM8 10V7a4 4 0 0 1 8 0v3M12 14v3',
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
  const passwordMismatch = auth.error === 'Yeni parola ve tekrarı aynı olmalı.'
  const location = useLocation(), go = useNavigate()
  const route = resolveRoute(location.pathname)
  const forbidden = route.kind === 'management' && !owner && route.section !== 'security'
  const section = route.kind === 'management' && !forbidden ? route.section : null
  const [pendingRequests, setPendingRequests] = useState(0)
  const [accessBusy, setAccessBusy] = useState(0)
  const onAccessBusyChange = useCallback((busy: boolean) => { setAccessBusy(current => current + (busy ? 1 : -1)) }, [])
  const [definitionBusy, setDefinitionBusy] = useState(false)
  const [profileBusy, setProfileBusy] = useState(false)
  const [businessName, setBusinessName] = useState('')
  const guard = useRef({ dirty: false, profileDirty: false, busy: false })
  const setDefinitionDirty = useCallback((value: boolean) => { guard.current.dirty = value }, [])
  const setProfileDirty = useCallback((value: boolean) => { guard.current.profileDirty = value }, [])
  const [navigationCollapsed, setNavigationCollapsed] = useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const menuButton = useRef<HTMLButtonElement>(null)
  const collapseButton = useRef<HTMLButtonElement>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const blocked = auth.busy || pendingRequests > 0 || definitionBusy || profileBusy || accessBusy > 0
  const current = sections.find(item => item.id === section)
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
    menuButton.current?.focus()
  }

  const completeNavigation = useCallback((to: string, options?: import('react-router').NavigateOptions, preserveDraft = false) => {
    if (!preserveDraft) guard.current.dirty = false
    guard.current.busy = false; void go(to, options)
  }, [go])
  return <CompletedNavigation value={completeNavigation}>
    <div className={`management-theme ${styles.page}`} onKeyDown={event => {
    if (event.key === 'Escape') {
      if (mobileMenuOpen) { event.stopPropagation(); closeMenu() }
      else if (navigationCollapsed && event.target instanceof HTMLElement && event.target.closest('#management-navigation')) collapseButton.current?.focus()
    }
  }}>
    <a className={styles.skipLink} href="#management-main">İçeriğe geç</a>
    <header className={styles.header}>
      <span className={styles.brand}>Randevu</span>
      <button className={styles.mobileToggle} ref={menuButton} type="button" aria-controls="management-navigation"
        aria-expanded={mobileMenuOpen} disabled={blocked} onClick={() => {
          setMobileMenuOpen(value => !value)
        }}>Menü</button>
      <div className={styles.account}><div>
        <div className={styles.accountIdentity}>{owner && businessName && <><span className={styles.businessName}>{businessName}</span><span aria-hidden="true">●</span></>}
          <span>{owner ? 'İşletme Sahibi' : 'Çalışan'}</span></div><span className={styles.email}>{account.email}</span></div>
        <button type="button" disabled={blocked} onClick={logout}>Çıkış yap</button>
      </div>
    </header>
    <div className={styles.layout} data-mobile-open={mobileMenuOpen} data-navigation-collapsed={navigationCollapsed}>
      <div id="management-navigation" className={styles.navigation}>
        <nav className={styles.rail} aria-label="Yönetim">
          <div className={styles.navigationTools}><span>Yönetim</span><button ref={collapseButton} type="button" className={styles.collapseMenu} disabled={blocked}
            aria-label={navigationCollapsed ? 'Menüyü genişlet' : 'Menüyü daralt'} title={navigationCollapsed ? 'Menüyü genişlet' : 'Menüyü daralt'}
            aria-controls="management-navigation" aria-expanded={!navigationCollapsed} onClick={() => setNavigationCollapsed(value => !value)}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M9 3v18M4 3h16v18H4z" /></svg>
          </button></div>
          {groups.filter(group => owner || group.id === 'account').map(group => <section className={styles.group} key={group.id} aria-labelledby={`management-group-${group.id}`}>
            <h2 id={`management-group-${group.id}`}>{group.title}</h2>
            {sections.filter(item => item.id !== 'audit' && sectionGroups[item.id] === group.id && (owner || item.id === 'security')).map(item =>
              <NavigationLink key={item.id} to={sectionPaths[item.id]} aria-current={section === item.id ? 'page' : undefined}
                aria-label={item.title} disabled={blocked} onClick={() => { if (section === item.id) { setMobileMenuOpen(false); heading.current?.focus() } }}>
                <NavigationIcon kind={item.id} /><span className={styles.navLabel}>{item.title}</span></NavigationLink>)}
          </section>)}
          {owner && <NavigationLink to={sectionPaths.audit} disabled={blocked}
            aria-label="Değişiklik kayıtları" title={navigationCollapsed ? 'Değişiklik kayıtları' : undefined} aria-current={section === 'audit' ? 'page' : undefined}>
            <NavigationIcon kind="audit" /><span className={styles.navLabel}>Değişiklik kayıtları</span>
          </NavigationLink>}
        </nav>
        <div className={styles.mobileAccount}><div>
          <div className={styles.accountIdentity}>{owner && businessName && <><span className={styles.businessName}>{businessName}</span><span aria-hidden="true">●</span></>}
            <span>{owner ? 'İşletme Sahibi' : 'Çalışan'}</span></div><p>{account.email}</p></div><button type="button" disabled={blocked} onClick={logout}>Çıkış yap</button></div>
      </div>
      <main id="management-main" className={styles.content}>
        {!(owner && section === 'services') && <>
          <h1 ref={heading} tabIndex={-1}>{current?.title ?? (forbidden ? 'Erişim izni yok' : 'Sayfa bulunamadı')}</h1>
          <p>{current?.description ?? (forbidden ? 'Hesabınızın bu bölüme erişim izni yok.' : 'Bu adres uygulamada bulunmuyor.')}</p>
        </>}
        {!current && <NavigationLink to={owner ? sectionPaths.business : sectionPaths.security}>Yetkili ekrana dön</NavigationLink>}
        {owner && <div hidden={section !== 'business'} className={styles.profilePanel}>
          <BusinessProfile post={post} disabled={auth.busy || pendingRequests > 0 || definitionBusy}
            onDirtyChange={setProfileDirty} onBusyChange={setProfileBusy} onConfirmedNameChange={setBusinessName} />
        </div>}
        {owner && section === 'personnel' && <div className={styles.profilePanel}>
          <StaffMembers post={post} onDirtyChange={setDefinitionDirty} onBusyChange={setDefinitionBusy} />
        </div>}
        {owner && section === 'hours' && <div className={styles.profilePanel}>
          <BusinessHours post={post} onDirtyChange={setDefinitionDirty} onBusyChange={setDefinitionBusy} />
        </div>}
        {owner && section === 'services' && <div className={styles.profilePanel}>
          <Services post={post} onDirtyChange={setDefinitionDirty} onBusyChange={setDefinitionBusy} headingRef={heading} />
        </div>}
        {owner && section === 'audit' && <div className={styles.panel}><AuditLog /></div>}
        {section === 'security' && <div className={styles.taskPanels + ' ' + styles.securityPanels}>
        {owner && <div className={styles.panel}>
          <OwnerRecoveryEmail post={post} disabled={blocked} />
        </div>}
        {owner && <div className={styles.panel}>
          <MfaRecoveryCodes post={post} disabled={blocked} onBusyChange={setDefinitionBusy} onReplaced={codes => {
            auth.setAccount(null); auth.setRecoveryCodes(codes); auth.setPassword(''); auth.setCode('')
            auth.setMfaRequired(false); auth.setUseRecoveryCode(false); auth.clearPasswordFields()
            auth.setError('')
            auth.setNotice('Eski MFA kurtarma kodları geçersiz. Yeni kodları saklayıp yeniden giriş yapın.')
          }} />
        </div>}
        <div className={styles.panel}>
          <p>{account.mfaEnabled ? 'İki aşamalı giriş açık.' : 'Çalışan hesabınız açık.'}</p>
          <PasswordChangeForm requiresSecondFactor={account.mfaEnabled || account.ownerAccess}
            mismatch={passwordMismatch}
            currentPassword={auth.currentPassword} newPassword={auth.newPassword}
            confirmPassword={auth.confirmPassword} busy={blocked}
            onCurrentPassword={auth.setCurrentPassword}
            onNewPassword={value => { auth.setNewPassword(value); if (passwordMismatch) auth.setError('') }}
            onConfirmPassword={value => { auth.setConfirmPassword(value); if (passwordMismatch) auth.setError('') }}
            onSubmit={auth.handlePasswordChange} />
          <ErrorMessage message={passwordMismatch ? '' : auth.error} />
          {auth.notice && <p role="status">{auth.notice}</p>}
        </div>
        </div>}
        {owner && section === 'access' && <>
          <p className={styles.deliveryNote}>Bölümden ayrıldığınızda ekrandaki teslim kodları temizlenir.</p>
          <div className={styles.taskPanels + ' ' + styles.accessPanels}>
          <div className={styles.panel}><StaffAccounts post={post} disabled={blocked} onBusyChange={onAccessBusyChange} /></div>
          <div className={styles.panel}><StaffInvitations post={post} disabled={blocked} onBusyChange={onAccessBusyChange} /></div>
          <div className={styles.panel}><StaffPasswordReset post={post} disabled={blocked} onBusyChange={onAccessBusyChange} /></div>
          </div>
        </>}
        {section !== 'security' && <ErrorMessage message={auth.error} />}
      </main>
    </div>
  </div></CompletedNavigation>
}
