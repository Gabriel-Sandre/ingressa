# ADR 0006 — Transactional Outbox + RabbitMQ

**Status:** aceita · **Versão:** Pleno · **Data:** 2026-09

## Contexto

Depois do pagamento, é preciso emitir ingressos e avisar o cliente. Fazer isso dentro da requisição deixa o cliente esperando e falha junto com o SMTP. Publicar uma mensagem logo após o `COMMIT` perde a mensagem se o processo cair entre os dois passos (*dual write*).

## Alternativas

1. Processar tudo na requisição.
2. `Task.Run` / `BackgroundService` com fila em memória — perde tudo se o processo reiniciar.
3. Publicar direto no RabbitMQ após o commit — *dual write*.
4. **Outbox no banco + despachante + RabbitMQ.**
5. Só a outbox, com o Worker lendo a tabela como fila (sem broker).

## Decisão

Opção 4.

## Justificativa

- A outbox é gravada **na mesma transação** da mudança: não há como pagar sem registrar a mensagem.
- O RabbitMQ entrega para filas diferentes por tipo, com *dead letter* e limite de entregas, e permite adicionar consumidores sem mexer em quem publica.
- A opção 5 seria suficiente hoje, mas não mostra roteamento, filas de falha e *publisher confirms* — assuntos centrais para quem vai trabalhar com sistemas distribuídos. É uma escolha consciente com viés didático, registrada aqui.

## Consequências

- ✅ Nenhuma mensagem se perde; o cliente recebe a resposta sem esperar e-mail.
- ❌ Entrega "pelo menos uma vez": consumidores precisam ser idempotentes (e são).
- ❌ Mais um serviço para operar; atraso de ~1 s entre pagar e emitir.
- ❌ A tabela de outbox cresce: limpeza de mensagens antigas fica para a Sênior.
