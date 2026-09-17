// Abertura de vendas de um evento de alta procura: muitos compradores ao mesmo tempo,
// todos passando pela fila virtual. Verifica que:
//   - a API não devolve erros 5xx sob pico;
//   - ninguém reserva sem passar pela fila;
//   - nunca se vende mais do que a capacidade.
//
// Requer LIMITE_AUTENTICACAO e LIMITE_RESERVAS altos no .env (todos os usuários virtuais têm o mesmo IP).
// docker compose --profile carga run --rm k6 run /scripts/pico-de-vendas.js
import http from 'k6/http'
import exec from 'k6/execution'
import { check, sleep } from 'k6'
import { Counter, Trend } from 'k6/metrics'
import { autenticado, BASE_URL, buscarEvento, criarCliente } from './comum.js'

const COMPRADORES = Number(__ENV.COMPRADORES || 300)

const tempoNaFila = new Trend('tempo_na_fila', true)
const reservasFeitas = new Counter('reservas_feitas')
const semEstoque = new Counter('reservas_sem_estoque')
const erros5xx = new Counter('erros_5xx')

export const options = {
  scenarios: {
    abertura: {
      executor: 'per-vu-iterations',
      vus: COMPRADORES,
      iterations: 1,
      maxDuration: '10m',
    },
  },
  thresholds: {
    erros_5xx: ['count==0'],
    'http_req_duration{rota:fila}': ['p(95)<200'],
    'http_req_duration{rota:reserva}': ['p(95)<500'],
    checks: ['rate>0.99'],
  },
}

export function setup() {
  const evento = buscarEvento('festival')
  return { eventoId: evento.id, setorId: evento.setores[0].id, capacidadeInicial: evento.setores[0].disponiveis }
}

function contar5xx(r) {
  if (r.status >= 500) erros5xx.add(1)
  return r
}

export default function (dados) {
  const token = criarCliente(`${exec.scenario.iterationInTest}-${Date.now()}`)
  const inicio = Date.now()

  let fila = contar5xx(http.post(`${BASE_URL}/api/eventos/${dados.eventoId}/fila`, null, { ...autenticado(token), tags: { rota: 'fila' } }))
  check(fila, { 'entrou na fila': (r) => r.status === 200 })

  // Sem passe, a reserva deve ser recusada.
  const semPasse = contar5xx(http.post(`${BASE_URL}/api/pedidos`,
    JSON.stringify({ eventoId: dados.eventoId, itens: [{ setorId: dados.setorId, quantidade: 1 }] }),
    autenticado(token, { 'Idempotency-Key': `${token.slice(-16)}-sem-passe` })))
  check(semPasse, { 'sem passe é recusado (403)': (r) => r.status === 403 })

  let situacao = fila.json('situacao')
  while (situacao !== 'Liberado' && Date.now() - inicio < 8 * 60 * 1000) {
    sleep(2 + Math.random())
    fila = contar5xx(http.get(`${BASE_URL}/api/eventos/${dados.eventoId}/fila`, { ...autenticado(token), tags: { rota: 'fila' } }))
    situacao = fila.json('situacao')
  }

  if (!check(fila, { 'foi liberado da fila': () => situacao === 'Liberado' })) return
  tempoNaFila.add(Date.now() - inicio)

  const reserva = contar5xx(http.post(`${BASE_URL}/api/pedidos`,
    JSON.stringify({ eventoId: dados.eventoId, itens: [{ setorId: dados.setorId, quantidade: 1 }] }),
    { ...autenticado(token, { 'Idempotency-Key': `${token.slice(-16)}-reserva`, 'X-Passe-Fila': fila.json('passe') }), tags: { rota: 'reserva' } }))

  check(reserva, { 'reserva criada ou esgotada': (r) => r.status === 201 || r.status === 409 })
  if (reserva.status === 201) reservasFeitas.add(1)
  if (reserva.status === 409) semEstoque.add(1)
}

export function teardown(dados) {
  const depois = http.get(`${BASE_URL}/api/eventos/${dados.eventoId}`).json('setores.0')
  const vendidos = dados.capacidadeInicial - depois.disponiveis
  check(depois, {
    'estoque nunca fica negativo': (s) => s.disponiveis >= 0,
    'reservas registradas não ultrapassam o que havia disponível': () => vendidos <= dados.capacidadeInicial,
  })
  console.log(`Disponíveis antes: ${dados.capacidadeInicial} · depois: ${depois.disponiveis} (${vendidos} reservados neste teste)`)
}
