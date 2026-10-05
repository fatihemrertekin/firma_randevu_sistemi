import { createContext, useContext } from 'react'
import { useNavigate, type NavigateOptions } from 'react-router'

// Only server-confirmed save/delete callbacks use this path: their request is finished.
export const CompletedNavigation = createContext<((to: string, options?: NavigateOptions) => void) | null>(null)
export function useCompletedNavigation() {
  const completion = useContext(CompletedNavigation), navigate = useNavigate()
  return completion ?? ((to: string, options?: NavigateOptions) => { void navigate(to, options) })
}
