import { Link, type LinkProps } from 'react-router'
import type { Ref } from 'react'

type Props = LinkProps & { disabled?: boolean; ref?: Ref<HTMLAnchorElement> }
export default function NavigationLink({ disabled = false, onClick, ...props }: Props) {
  return <Link {...props} data-navigation="true" aria-disabled={disabled || undefined} tabIndex={disabled ? -1 : props.tabIndex}
    onClick={event => {
      if (disabled) event.preventDefault()
      else if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) onClick?.(event)
    }} />
}
