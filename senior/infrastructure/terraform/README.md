# Infraestrutura na AWS (Terraform)

Descreve o ambiente de produção do Ingressa. **Nada aqui foi aplicado numa conta real** —
o código foi escrito para ser revisado e validado (`terraform fmt`, `validate`, `tflint`,
`checkov` rodam no CI). Aplicar gera custo; veja a estimativa no fim.

## Desenho

```
Internet ─► WAF ─► ALB (HTTPS, TLS 1.3)
                    ├─ /api/*  ─► ECS Fargate: API (2–20 tarefas, escala por CPU e requisições)
                    └─ resto   ─► ECS Fargate: Web (nginx + React)
                                   │  Service Connect ("api:8080")
ECS Fargate: Worker (outbox, fila virtual, e-mails) ─► Amazon MQ (RabbitMQ, AMQPS)
API / Worker ─► RDS PostgreSQL (Multi-AZ, TLS obrigatório, PITR)
            ─► ElastiCache Redis (réplica, TLS, AUTH)
            ─► Amazon SES (SMTP)
Cada tarefa ─► coletor OpenTelemetry ─► X-Ray + CloudWatch
```

| Arquivo | Conteúdo |
|---|---|
| `rede.tf` | VPC em 2 zonas, sub-redes públicas (só o ALB) e privadas, NAT por zona, flow logs |
| `seguranca.tf` | Chave KMS, security groups em camadas, WAF (regras gerenciadas + limite por IP) |
| `segredos.tf` | Senhas geradas (`random_password`) e guardadas no Secrets Manager |
| `dados.tf` | RDS, ElastiCache, Amazon MQ |
| `containers.tf` | ECR, cluster ECS, IAM, tarefas, serviços, autoscaling, tarefa de migração |
| `balanceador.tf` | ALB, listeners, roteamento, logs de acesso |
| `email.tf` | Domínio no SES e usuário SMTP com permissão mínima |
| `observabilidade.tf` | Alarmes (5xx, latência p95, saúde, banco, Redis, fila de falhas) → SNS/e-mail |
| `backup.tf` | AWS Backup diário e mensal, com cópia para outra região |

## Decisões

- **ECS Fargate em vez de Kubernetes**: três serviços não justificam operar um cluster (ADR 0014).
- **Nenhuma senha em código ou tfvars**: o Terraform gera as senhas; os containers as recebem
  do Secrets Manager. Elas ficam também no *state* — por isso o backend é um bucket S3
  criptografado e com acesso restrito.
- **Migrations fora do deploy da API**: o pipeline roda a tarefa `migracao`
  (`--migrar-e-sair`) e só implanta a nova versão se ela terminar com sucesso.
- **A API não recebe as senhas do RabbitMQ nem do SMTP**: quem publica e envia é o Worker.
- **Rollback automático**: o *circuit breaker* do ECS volta à versão anterior se as novas
  tarefas não ficarem saudáveis.

## Uso

Pré-requisitos: bucket S3 para o estado, certificado no ACM e domínio.

```bash
terraform init -backend-config=ambientes/homologacao.s3.tfbackend
terraform plan -var-file=ambientes/homologacao.tfvars -var versao_imagem=<sha-do-commit>
terraform apply ...
```

Depois do primeiro `apply`: verificar o domínio no SES (registros DKIM), confirmar a
inscrição de e-mail do SNS e apontar o DNS do domínio para `endereco_load_balancer`.

## Conexões com o banco

O PostgreSQL aceita um número fixo de conexões (cerca de 400 nesta classe de instância), e cada
tarefa mantém seu próprio pool. Por isso a connection string fixa `Maximum Pool Size=15`:

| | Tarefas (máx.) | Conexões por tarefa | Total |
|---|---|---|---|
| API | 20 | 15 | 300 |
| Worker | 6 | 15 | 90 |
| Migração (pontual) | 1 | 15 | 15 |

Se o número máximo de tarefas subir, o teto por tarefa precisa descer na mesma proporção — ou entra
um pool externo (PgBouncer / RDS Proxy), que é o caminho quando a aplicação cresce.

## Limitações conhecidas

- Não foi aplicado nem testado numa conta AWS; versões de imagens (ex.: coletor OTel) e
  classes de instância devem ser conferidas no momento do uso.
- A conta nova do SES começa em *sandbox* (só envia para endereços verificados).
- Não há ambiente de *disaster recovery* ativo: a outra região guarda só as cópias dos backups
  (RPO de até 24 h nessa situação; RTO de horas). Ver `docs/runbooks/restauracao.md`.

## Custo aproximado (produção, sa-east-1)

Ordem de grandeza: **US$ 700–900/mês** com os valores padrão (RDS Multi-AZ e Amazon MQ em
cluster são os maiores itens). Homologação com `homologacao.tfvars`: cerca de US$ 150/mês.
Valores estimados, sem consulta à calculadora oficial — confira antes de aplicar.
