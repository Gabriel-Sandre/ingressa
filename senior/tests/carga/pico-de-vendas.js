// Abertura de vendas de um evento de alta procura: muitos compradores ao mesmo tempo,
// todos passando pela fila virtual. Verifica que:
//   - a API não devolve erros 5xx sob pico;
//   - ninguém reserva sem passar pela fila;
//   - nunca se vende mais do que a capacidade;
//   - a fila e a reserva respondem dentro das metas (docs/slos.md).
//
// As contas são criadas no setup, fora da janela medida: cada cadastro/login gasta
// PBKDF2 com 600 mil iterações, e isso mediria o custo do hash de senha, não a fila.
//
// Requer limites altos no .env (todos os usuários virtuais saem do mesmo IP):
// LIMITE_AUTENTICACAO, LIMITE_RESERVAS e LIMITE_GLOBAL_POR_MINUTO.
//
// docker compose --profile carga run --rm --service-ports k6 run /scripts/pico-de-vendas.js
import http from 'k6/http'
import exec from 'k6/execution'
import { check, sleep } from 'k6'
import { Counter, Trend } from 'k6/metrics'
import { autenticado, BASE_URL, buscarEvento, criarCliente } from './comum.js'

const COMPRADORES = Number(__ENV.COMPRADORES || 300)
// Metas de serviço; podem ser afrouxadas ao rodar tudo (banco, filas e k6) num notebook.
const META_FILA_MS = Number(__ENV.META_FILA_MS || 200)
const META_RESERVA_MS = Number(__ENV.META_RESERVA_MS || 500)

const tempoNaFila = new Trend('tempo_na_fila', true)
const reservasFeitas = new Counter('reservas_feitas')
const semEstoque = new Counter('reservas_sem_estoque')
const erros5xx = new Counter('erros_5xx')
const respostasInesperadas = new Counter('respostas_inesperadas')

export const options = {
  scenarios: {
    abertura: {
      executor: 'per-vu-iterations',
      vus: COMPRADORES,
      iterations: 1,
      maxDuration: '10m',
    },
  },
  setupTimeout: '15m',
  thresholds: {
    erros_5xx: ['count==0'],
    respostas_inesperadas: ['count==0'],
    'http_req_duration{rota:fila}': [`p(95)<${META_FILA_MS}`],
    'http_req_duration{rota:reserva}': [`p(95)<${META_RESERVA_MS}`],
    checks: ['rate>0.99'],
  },
}

export function setup() {
  const evento = buscarEvento('festival')
  // O setor com mais lugares livres: o objetivo é medir a fila, não esgotar o estoque.
  const setor = evento.setores.reduce((maior, s) => (s.disponiveis > maior.disponiveis ? s : maior))

  console.log(`Criando ${COMPRADORES} contas (fora da medição)...`)
  const tokens = []
  for (let i = 0; i < COMPRADORES; i++) {
    tokens.push(criarCliente(`${i}-${Date.now()}`))
  }

  return { eventoId: evento.id, setorId: setor.id, disponiveisAntes: setor.disponiveis, tokens }
}

/** Marca respostas fora do previsto para que apareçam no resumo em vez de passarem batido. */
function conferir(r, esperados) {
  if (r.status >= 500) erros5xx.add(1)
  if (!esperados.includes(r.status)) {
    respostasInesperadas.add(1, { status: String(r.status) })
    console.error(`Resposta ${r.status} em ${r.request.method} ${r.request.url}: ${String(r.body).slice(0, 200)}`)
  }
  return r
}

export default function (dados) {
  const token = dados.tokens[exec.vu.idInTest - 1]
  const inicio = Date.now()

  let fila = conferir(
    http.post(`${BASE_URL}/api/eventos/${dados.eventoId}/fila`, null, {
      ...autenticado(token),
      tags: { rota: 'fila' },
    }),
    [200],
  )
  check(fila, { 'entrou na fila': (r) => r.status === 200 })

  // Sem passe, a reserva tem de ser recusada — é a garantia de que a fila não é enfeite.
  const semPasse = conferir(
    http.post(
      `${BASE_URL}/api/pedidos`,
      JSON.stringify({ eventoId: dados.eventoId, itens: [{ setorId: dados.setorId, quantidade: 1 }] }),
      {
        ...autenticado(token, { 'Idempotency-Key': `${token.slice(-16)}-sem-passe` }),
        responseCallback: http.expectedStatuses(403),
      },
    ),
    [403],
  )
  check(semPasse, { 'sem passe é recusado (403)': (r) => r.status === 403 })

  let situacao = fila.json('situacao')
  while (situacao !== 'Liberado' && Date.now() - inicio < 8 * 60 * 1000) {
    sleep(2 + Math.random())
    fila = conferir(
      http.get(`${BASE_URL}/api/eventos/${dados.eventoId}/fila`, { ...autenticado(token), tags: { rota: 'fila' } }),
      [200],
    )
    situacao = fila.json('situacao')
  }

  if (!check(fila, { 'foi liberado da fila': () => situacao === 'Liberado' })) return
  tempoNaFila.add(Date.now() - inicio)

  const reserva = conferir(
    http.post(
      `${BASE_URL}/api/pedidos`,
      JSON.stringify({ eventoId: dados.eventoId, itens: [{ setorId: dados.setorId, quantidade: 1 }] }),
      {
        ...autenticado(token, {
          'Idempotency-Key': `${token.slice(-16)}-reserva`,
          'X-Passe-Fila': fila.json('passe'),
        }),
        tags: { rota: 'reserva' },
        // 409 = setor esgotou durante o teste: resultado legítimo, não é erro.
        responseCallback: http.expectedStatuses(201, 409),
      },
    ),
    [201, 409],
  )

  check(reserva, { 'reserva criada ou esgotada': (r) => r.status === 201 || r.status === 409 })
  if (reserva.status === 201) reservasFeitas.add(1)
  if (reserva.status === 409) semEstoque.add(1)
}

export function teardown(dados) {
  const setores = http.get(`${BASE_URL}/api/eventos/${dados.eventoId}`).json('setores')
  const setor = setores.find((s) => s.id === dados.setorId)
  const vendidos = dados.disponiveisAntes - setor.disponiveis

  check(setor, {
    'estoque nunca fica negativo': (s) => s.disponiveis >= 0,
    'não se vendeu mais do que havia disponível': () => vendidos <= dados.disponiveisAntes,
  })
  console.log(`Setor ${setor.nome}: ${dados.disponiveisAntes} disponíveis antes, ${setor.disponiveis} depois (${vendidos} reservados).`)
}
