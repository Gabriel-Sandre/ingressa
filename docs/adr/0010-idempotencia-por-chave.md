# ADR 0010 — Idempotência com a chave `Idempotency-Key`

**Status:** aceita · **Versão:** Sênior · **Data:** 2026-09

## Contexto

Em pico, a rede do cliente falha e o navegador ou o usuário repete o pedido. Na Pleno, repetir "reservar" criava uma segunda reserva, e repetir "pagar" dependia da regra de estado do pedido para não cobrar de novo. O cliente não tem como saber se a primeira tentativa chegou.

## Alternativas

1. Confiar nas regras de domínio (pedido já pago não paga de novo) — não cobre "reservar" e devolve erro em vez da resposta original.
2. Desabilitar o botão no front — não protege contra retentativa da rede ou de outro cliente da API.
3. **Cabeçalho `Idempotency-Key` + tabela com índice único (usuário, chave) + resposta armazenada.**

## Decisão

Opção 3, aplicada com o atributo `[Idempotente]` em reservar e pagar.

## Justificativa

- É o padrão usado por provedores de pagamento (Stripe, Adyen) e está em rascunho na IETF.
- `INSERT ... ON CONFLICT DO NOTHING` decide quem é o primeiro sem *lock* explícito, inclusive entre réplicas.
- A mesma chave com **outro corpo** é recusada (422): evita que um bug no cliente reaproveite a chave em outra compra.
- Pedido repetido enquanto o primeiro ainda executa recebe 409; depois de concluído, recebe a resposta original com `Idempotent-Replayed: true`.

## Consequências

- ✅ Retentativas seguras; o front gera a chave uma vez por intenção de compra.
- ❌ Uma tabela a mais, limpa depois de 24 h pela `LimpezaDeDados`.
- ⚠️ Se o processo cair no meio, a chave fica "em andamento". Depois de 2 minutos ela pode ser retomada por uma nova tentativa (troca atômica da data), sem esperar a limpeza.
- ❌ Se a queda acontecer **depois** do commit e **antes** de guardar a resposta, a retomada executa de novo: para reservar, a regra de domínio de passe/estoque ainda protege; é o limite conhecido desta abordagem sem transação única entre chave e pedido.
