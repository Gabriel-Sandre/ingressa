# Roadmap do Ingressa

## Versão Júnior ✅

- [x] Cadastro e login com JWT; senha com PBKDF2
- [x] CRUD de eventos e setores com regras de propriedade
- [x] Vitrine com busca, filtro, ordenação e paginação
- [x] Compra com validação de estoque e cancelamento
- [x] Tratamento de erros com Problem Details
- [x] Vitrine em JavaScript puro
- [x] 54 testes automatizados

## Versão Pleno 🚧

- [ ] Camadas Domain / Application / Infrastructure / Api
- [ ] PostgreSQL com migrations
- [ ] Concorrência otimista na baixa de estoque, com teste que prova a correção
- [ ] Reserva com expiração e pagamento simulado (webhook)
- [ ] Perfil administrador e aprovação de organizadores
- [ ] Refresh token com rotação
- [ ] RabbitMQ + padrão Outbox para emitir ingressos e e-mails
- [ ] Serilog, health checks, Docker Compose
- [ ] Testes de integração com Testcontainers; GitHub Actions
- [ ] Frontend React + TypeScript

## Versão Sênior ⏳

- [ ] Fila virtual (sala de espera) para vendas de alta demanda
- [ ] Rate limiting e proteção contra robôs
- [ ] Idempotência de ponta a ponta nos pagamentos
- [ ] OpenTelemetry (traces, métricas e logs correlacionados)
- [ ] Testes de carga com k6 e metas de desempenho (SLOs)
- [ ] Infraestrutura como código (Terraform) na AWS
- [ ] Backup, recuperação de desastre e documentação operacional
- [ ] Extração de serviço apenas onde a medição justificar
