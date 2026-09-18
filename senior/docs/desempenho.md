# Desempenho medido

Resultados reais das execuções do k6 (`tests/carga`). Os números dependem da máquina: aqui
**tudo roda no mesmo computador** — PostgreSQL, Redis, RabbitMQ, duas APIs, dois Workers, nginx,
Grafana e o próprio gerador de carga. Não é um ambiente de produção; é uma medição honesta do que
o sistema faz quando não há para onde escalar.

**Máquina:** Intel Core i5-1135G7 (4 núcleos, 2.40 GHz), 8 GB de RAM, Windows com Docker Desktop
(WSL2). **Data:** 18/09/2026.

## Vitrine — leitura sob carga (`vitrine.js`)

Até 300 requisições por segundo em `/api/eventos` e `/api/eventos/{id}`, as rotas mais acessadas.

| Indicador | Medido | Meta ([SLO](slos.md)) |
|---|---|---|
| Requisições | 31.116 em 1m45s (**294,6 req/s**) | 300 req/s de pico |
| Falhas | **0** (0,00%) | < 1% |
| p95 — vitrine | **66 ms** | < 300 ms |
| p95 — detalhe do evento | **63 ms** | < 300 ms |
| Mediana | 2,3 ms | — |
| Usuários virtuais simultâneos | até 189 | — |

A mediana de 2 ms é o cache híbrido respondendo da memória; o p95 de 66 ms é quando a chave
expirou e alguém foi ao banco. **Meta batida com folga de 4×.**

## Pico de vendas — 300 compradores pela fila virtual (`pico-de-vendas.js`)

300 compradores entram na fila do mesmo evento ao mesmo tempo. O Worker admite 50 por vez
(`COMPRADORES_SIMULTANEOS=50`), cada um recebe um passe de uso único e reserva um ingresso.

| Indicador | Medido | Meta |
|---|---|---|
| Compradores atendidos | **300 de 300** | todos |
| Reservas criadas | **300** | — |
| Erros 5xx | **0** | 0 |
| Respostas fora do previsto | **0** | 0 |
| Verificações do teste | **1.502 de 1.502** | > 99% |
| Ingressos vendidos além da capacidade | **0** | 0 |
| Reserva sem passar pela fila | **recusada em 100% das tentativas** | sempre |
| Tempo na fila (p95) | 22,8 s | — |
| p95 — entrar/consultar a fila | 2,0 s | < 200 ms (produção) |
| p95 — reservar | 4,3 s | < 500 ms (produção) |

**O que passou:** correção. Ninguém furou a fila, ninguém reservou sem passe, o estoque nunca ficou
negativo e não houve um único erro de servidor — com 300 pessoas disputando o mesmo evento.

**O que não passou:** latência. Os p95 de fila e reserva ficaram muito acima da meta de produção,
e a razão é a máquina: 300 usuários virtuais, quatro instâncias da aplicação, banco, cache e fila
dividindo 4 núcleos com o gerador de carga. As metas continuam valendo como SLO de produção; para
a execução local, o script de validação afrouxa os limites (`META_FILA_MS` e `META_RESERVA_MS`),
porque medir latência numa máquina saturada não mede a aplicação.

## O que o teste de carga encontrou (e foi corrigido)

A primeira execução com 300 compradores devolveu **103 erros 500**. O log dos containers mostrou:

```
53300: sorry, too many clients already
```

O PostgreSQL aceita 100 conexões por padrão e cada instância da aplicação mantém seu próprio pool,
que no Npgsql vai até 100. Com duas APIs e dois Workers, o pico pedia até 400 conexões para um
banco que aceitava 100 — não era lentidão, era recusa de conexão.

A correção foi orçar conexões em vez de aumentar o banco às cegas: teto por instância
(`Maximum Pool Size`) e `max_connections` maior no PostgreSQL local; na AWS, o mesmo cálculo está
documentado em [`infrastructure/terraform/README.md`](../infrastructure/terraform/README.md).
Depois disso, a mesma execução foi a **zero erros**.

Esse é o motivo de existir teste de carga: o problema só aparece com várias instâncias e carga
real, e nenhum teste de unidade o encontraria.

## Como reproduzir

```bash
cd senior
cp .env.example .env     # limites altos: toda a carga vem do mesmo IP
docker compose up -d --build
mkdir -p tests/carga/resultados
docker compose --profile carga run --rm --service-ports k6 run /scripts/vitrine.js
docker compose --profile carga run --rm --service-ports k6 run -e COMPRADORES=300 /scripts/pico-de-vendas.js
```

O painel ao vivo fica em http://localhost:5665 e o relatório HTML em `tests/carga/resultados/`.
Compare sempre execuções feitas na mesma máquina.
