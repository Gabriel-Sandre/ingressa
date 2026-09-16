import { Link, Route, Routes } from 'react-router-dom'
import { Layout } from './componentes/Layout'
import { Protegida } from './componentes/Comum'
import { Cadastro, Entrar } from './paginas/Acesso'
import { Admin } from './paginas/Admin'
import { EventoDetalhe } from './paginas/EventoDetalhe'
import { MeusPedidos } from './paginas/MeusPedidos'
import { Organizador } from './paginas/Organizador'
import { Pagamento } from './paginas/Pagamento'
import { Vitrine } from './paginas/Vitrine'

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Vitrine />} />
        <Route path="eventos/:id" element={<EventoDetalhe />} />
        <Route path="entrar" element={<Entrar />} />
        <Route path="cadastro" element={<Cadastro />} />
        <Route path="pedidos" element={<Protegida perfil="Cliente"><MeusPedidos /></Protegida>} />
        <Route path="pedidos/:id/pagamento" element={<Protegida perfil="Cliente"><Pagamento /></Protegida>} />
        <Route path="organizador" element={<Protegida perfil="Organizador"><Organizador /></Protegida>} />
        <Route path="admin" element={<Protegida perfil="Admin"><Admin /></Protegida>} />
        <Route
          path="*"
          element={
            <p>
              Página não encontrada. <Link to="/">Voltar à vitrine</Link>
            </p>
          }
        />
      </Route>
    </Routes>
  )
}
