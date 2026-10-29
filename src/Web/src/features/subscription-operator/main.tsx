import { createRoot } from 'react-dom/client'
import OperatorApplication from './OperatorApplication'
import '../../styles/global.css'

const root = document.getElementById('root')
if (!root) throw new Error('Uygulama kök öğesi bulunamadı.')
createRoot(root).render(<OperatorApplication />)
