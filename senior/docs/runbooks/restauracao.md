# Restauração do banco e recuperação de desastre

## Metas

| Cenário | RPO (perda máxima de dados) | RTO (tempo para voltar) | Como |
|---|---|---|---|
| Falha da instância / zona | 0 | ~2 min | RDS Multi-AZ faz failover sozinho |
| Dado apagado ou corrompido por erro | até 5 min | ~1 h | Recuperação a um instante (PITR) |
| Região da AWS indisponível | até 24 h | algumas horas | Cópia diária do AWS Backup em `us-east-1` |

## 1. Recuperar a um instante (PITR)

Use quando alguém apagou ou alterou dados por engano. **Não sobrescreva** a instância atual:
restaure ao lado, confira e só então troque.

```bash
# 1. Anote o instante imediatamente ANTES do problema (UTC)
aws rds restore-db-instance-to-point-in-time \
  --source-db-instance-identifier ingressa-producao \
  --target-db-instance-identifier ingressa-producao-restaurado \
  --restore-time 2026-09-20T14:05:00Z \
  --db-subnet-group-name ingressa-producao \
  --vpc-security-group-ids <sg-dados> \
  --multi-az

# 2. Aguarde ficar disponível
aws rds wait db-instance-available --db-instance-identifier ingressa-producao-restaurado
```

3. Conecte pela tarefa de migração (sub-rede privada) e confira os dados.
4. Escolha:
   - **Recuperar só alguns registros**: exporte da restaurada (`pg_dump -t`) e importe na atual.
   - **Trocar o banco inteiro**: pause as vendas (serviços `api` e `worker` com 0 tarefas),
     renomeie as instâncias (`modify-db-instance --new-db-instance-identifier`), deixando a
     restaurada com o nome original, e volte os serviços.
5. Rode `terraform plan` para confirmar que o estado continua coerente.
6. Apague a instância antiga só depois de alguns dias.

## 2. Região indisponível

1. Confirme no AWS Health Dashboard que o problema é regional.
2. Na região de recuperação, restaure o último ponto do cofre `ingressa-producao-recuperacao`
   (`aws backup start-restore-job`).
3. Aplique o Terraform nessa região com `regiao = "us-east-1"` e um *backend* separado,
   apontando a aplicação para o banco restaurado.
4. Recrie Redis e RabbitMQ vazios (não guardam dado durável: a fila virtual recomeça e a
   outbox no banco republica o que faltava).
5. Troque o DNS para o novo load balancer.

> Este procedimento **não foi exercitado** numa conta real. Um teste semestral é recomendado.

## Confirmação

- `/health/ready` responde 200 em todas as tarefas.
- Contagem de pedidos pagos do dia bate com o relatório do provedor de pagamento.
- Nenhuma mensagem presa em `GET /api/admin/outbox/falhas`.
