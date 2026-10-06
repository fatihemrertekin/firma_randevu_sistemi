import { useId } from 'react'
import styles from './Pagination.module.css'

type Props = { page: number; pageSize: number; itemCount: number; totalCount?: number; knownPages?: number;
  hasNext: boolean; disabled: boolean; label: string; onPageChange: (page: number) => void; onPageSizeChange: (size: number) => void }
function Arrow({ direction, edge = false }: { direction: 'left' | 'right'; edge?: boolean }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d={direction === 'left' ? 'm14 6-6 6 6 6' : 'm10 6 6 6-6 6'} />
    {edge && <path d={direction === 'left' ? 'M5 6v12' : 'M19 6v12'} />}
  </svg>
}
export default function Pagination({ page, pageSize, itemCount, totalCount, knownPages = page, hasNext, disabled, label, onPageChange, onPageSizeChange }: Props) {
  const sizeId = useId(), last = totalCount === undefined ? knownPages : Math.min(10000, Math.max(1, Math.ceil(totalCount / pageSize)))
  const pages = [...new Set([1, ...[page - 1, page, page + 1].filter(value => value >= 1 && value <= last), last])].sort((a, b) => a - b)
  const start = itemCount > 0 ? (page - 1) * pageSize + 1 : 0, end = itemCount > 0 ? start + itemCount - 1 : 0
  const multiple = last > 1 || hasNext || page > 1
  return <nav className={styles.pagination} aria-label={label}>
    <span className={styles.range}>{totalCount === undefined ? `${start}–${end} kayıt · Sayfa ${page}` : `${totalCount} kayıttan ${start}–${end}`}</span>
    {multiple && <div className={styles.pages}>
      <button type="button" disabled={disabled || page === 1} aria-label="İlk sayfa" onClick={() => onPageChange(1)}><Arrow direction="left" edge /></button>
      <button type="button" disabled={disabled || page === 1} aria-label="Önceki sayfa" onClick={() => onPageChange(page - 1)}><Arrow direction="left" /></button>
      {pages.map((value, index) => <span className={`${styles.numberGroup} ${Math.abs(value - page) > 1 ? styles.distantGroup : ''}`} key={value}>
        {index > 0 && value > pages[index - 1] + 1 && <span className={styles.ellipsis} aria-hidden="true">…</span>}
        <button type="button" aria-label={`Sayfa ${value}`}
          aria-current={value === page ? 'page' : undefined} disabled={disabled || value === page} onClick={() => onPageChange(value)}>{value}</button>
      </span>)}
      <button type="button" disabled={disabled || !hasNext || page >= 10000} aria-label="Sonraki sayfa" onClick={() => onPageChange(page + 1)}><Arrow direction="right" /></button>
      {totalCount !== undefined && <button type="button" className={styles.last} disabled={disabled || page === last} aria-label="Son sayfa" onClick={() => onPageChange(last)}><Arrow direction="right" edge /></button>}
    </div>}
    {(multiple || (totalCount ?? itemCount) > 10) && <div className={styles.size}><label htmlFor={sizeId}>Sayfada</label>
      <select id={sizeId} aria-label="Sayfadaki kayıt sayısı" disabled={disabled} value={pageSize} onChange={event => onPageSizeChange(Number(event.target.value))}>
        {[10, 20, 50].map(size => <option key={size} value={size}>{size} / sayfa</option>)}
      </select></div>}
  </nav>
}
