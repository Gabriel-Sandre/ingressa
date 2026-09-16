# Mensagens com falha (ingressos ou e-mails não enviados)

## Como perceber

- Alarme `ingressa-producao-fila-de-falhas` (mensagem na fila `ingressa.falhas`), ou
- Cliente pagou e não recebeu o ingresso, ou
- A seção **Mensagens com falha de publicação** do painel de administração mostra itens.

Há **dois** lugares onde uma mensagem pode parar:

| Onde | Significa | Limite |
|---|---|---|
| Outbox (banco) | O Worker não conseguiu **publicar** no RabbitMQ | 10 tentativas |
| Fila `ingressa.falhas` (RabbitMQ) | Foi publicada, mas o **consumidor** falhou (ex.: SMTP fora) | 5 entregas |

## Outbox

1. Liste: `GET /api/admin/outbox/falhas` (ou a seção no painel de administração). Veja `ultimoErro`.
2. Corrija a causa (RabbitMQ fora, senha trocada...).
3. Reenvie cada mensagem: `POST /api/admin/outbox/{id}/reprocessar` (botão "Reprocessar").
4. O despachante publica em até alguns segundos.

## Fila de falhas no RabbitMQ

1. No console do RabbitMQ, abra a fila `ingressa.falhas` e use **Get messages** (modo *Nack requeue true*)
   para ler o cabeçalho `x-death` e descobrir a fila de origem.
2. Veja o log do Worker pelo `trace_id` da mensagem (CloudWatch Logs / Grafana).
3. Corrija a causa.
4. Mova as mensagens de volta com **Move messages** (plugin *shovel*) para a exchange
   `ingressa.eventos`, mantendo a *routing key* original.
5. Os consumidores são idempotentes: reprocessar uma mensagem já tratada não emite ingresso em dobro.

## Confirmação

- `ingressa.falhas` vazia e `GET /api/admin/outbox/falhas` sem itens.
- O cliente vê o ingresso em "Meus pedidos".
