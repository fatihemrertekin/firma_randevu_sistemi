import { createContext, useContext, useEffect, useEffectEvent } from 'react'
import type { Location } from 'react-router'

export type SubscribeNavigation = (listener: (location: Location) => void) => () => void
export const NavigationEvents = createContext<SubscribeNavigation | null>(null)
export function useNavigationChange(callback: (location: Location) => void) {
  const subscribe = useContext(NavigationEvents), changed = useEffectEvent(callback)
  useEffect(() => subscribe?.(location => changed(location)), [subscribe])
}
