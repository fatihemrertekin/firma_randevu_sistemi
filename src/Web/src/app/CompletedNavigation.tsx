import { createContext, useCallback, useContext } from 'react'
import { useNavigate, type NavigateOptions } from 'react-router'

// Sunucunun onayladığı kayıt/silme veya geçerli liste sayfasına dönüş içindir.
export const CompletedNavigation = createContext<((to: string, options?: NavigateOptions, preserveDraft?: boolean) => void) | null>(null)
export function useCompletedNavigation() {
  const completion = useContext(CompletedNavigation), navigate = useNavigate()
  const fallback = useCallback((to: string, options?: NavigateOptions) => { void navigate(to, options) }, [navigate])
  return completion ?? fallback
}
