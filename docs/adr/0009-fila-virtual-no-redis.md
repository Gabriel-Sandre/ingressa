# ADR 0009 — Fila virtual no Redis com scripts Lua

**Status:** aceita · **Versão:** Sênior · **Data:** 2026-09

## Contexto

Na abertura de vendas de um evento disputado, milhares de pessoas chegam no mesmo segundo. A Pleno já não vende a mais (UPDATE atômico), mas todo mundo disputa o banco ao mesmo tempo: a latência sobe para todos, o pool de conexões esgota e quem clicou primeiro não tem vantagem nenhuma. Precisamos **controlar quantos compram ao mesmo tempo** e dar a cada pessoa uma posição justa.

## Alternativas

1. Só aumentar recursos (mais réplicas, banco maior) — o gargalo é o banco, que não escala horizontalmente para escrita.
2. Rate limit por IP/usuário — protege a API, mas é loteria: não há ordem de chegada.
3. Fila na tabela do PostgreSQL — ordem garantida, mas coloca justamente no banco a carga que queremos tirar dele.
4. Fila no RabbitMQ — feita para trabalho assíncrono, não para "qual é a minha posição agora?".
5. **Redis: sorted set por evento + passes com validade, operações atômicas em Lua.**
6. Serviço gerenciado de sala de espera (ex.: Cloudflare Waiting Room, Queue-it).

## Decisão

Opção 5, ativada por evento (`Evento.FilaVirtual`).

## Justificativa

- `ZADD`/`ZRANK` respondem posição em O(log n) em memória: a consulta da sala de espera não toca o banco.
- Cada operação (entrar, admitir, usar o passe, devolver, concluir) é um script Lua — o Redis executa atomicamente, então duas réplicas da API ou do Worker nunca admitem a mesma pessoa duas vezes nem passam do limite de compradores simultâneos. Os 17 cenários dos scripts foram testados contra um Redis real.
- A **admissão** roda no Worker a cada 2 s e respeita `CompradoresSimultaneos`; o passe vale 10 minutos e é **consumido** na reserva (não dá para reutilizar nem compartilhar) e **devolvido** se a reserva falhar por regra de negócio.
- A opção 6 é o que eu recomendaria para um evento realmente nacional (a fila fica fora da nossa infraestrutura), mas esconderia exatamente o que este projeto quer demonstrar.

## Consequências

- ✅ O banco recebe no máximo `CompradoresSimultaneos` compradores por evento, em qualquer pico.
- ✅ Ordem de chegada justa e posição visível para o cliente.
- ❌ O Redis passa a ser dependência crítica da compra em eventos com fila (AOF ligado no compose; réplica com failover na AWS). Se o Redis cair, esses eventos param de vender — decisão consciente: é melhor parar do que abrir a porta para todos de uma vez.
- ❌ Robôs podem entrar na fila várias vezes com contas diferentes; mitigado pelo limite por IP e pelo WAF, não resolvido (exigiria CAPTCHA ou verificação de conta).
