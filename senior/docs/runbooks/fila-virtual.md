# Fila virtual numa abertura de vendas

## Antes da abertura

- [ ] Evento com **fila virtual** marcada (tela do organizador).
- [ ] `FilaVirtual__CompradoresSimultaneos` adequado: comece com o número que o teste de
      carga sustentou com folga (ver `docs/desempenho.md`).
- [ ] Mínimo de tarefas da API elevado **antes** do horário (o autoscaling reage em minutos):
      `aws application-autoscaling register-scalable-target ... --min-capacity 10`.
- [ ] Painel aberto com `ingressa.fila.tamanho`, `ingressa.fila.liberados`, latência e 5xx.

## Sintomas e ações

| Sintoma | Causa provável | Ação |
|---|---|---|
| Ninguém sai da fila (`liberados` = 0) | Worker parado | Ver tarefas do serviço `worker`; logs de `AdmissaoDaFilaVirtual` |
| Fila anda, mas reservas dão 403 "entre na fila" | Passe vencido (usuário demorou > 10 min) ou relógio | Esperado em parte; se for geral, conferir hora das tarefas |
| Latência da reserva sobe | Muitos compradores simultâneos para o banco | Reduza `CompradoresSimultaneos` e faça deploy do Worker (a API não precisa) |
| Erros de Redis nos logs | ElastiCache com memória cheia ou failover | Ver alarme `redis-memoria`; o failover leva ~1 min e a fila é preservada na réplica |
| Muitos 429 | Limites por IP (clientes atrás do mesmo NAT, ex.: universidade) | Ajuste `LimitesDistribuidos__fila__Limite` temporariamente |

## Depois

- Desmarque a fila virtual quando a procura normalizar.
- Volte o mínimo de tarefas ao normal.
- Registre os números do dia em `docs/desempenho.md`.
