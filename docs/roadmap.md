# Roadmap do Ingressa

## Versão Júnior ✅

- [x] Cadastro e login com JWT; senha com PBKDF2
- [x] CRUD de eventos e setores com regras de propriedade
- [x] Vitrine com busca, filtro, ordenação e paginação
- [x] Compra com validação de estoque e cancelamento
- [x] Tratamento de erros com Problem Details
- [x] Vitrine em JavaScript puro
- [x] 54 testes automatizados

## Versão Pleno ✅

- [x] Camadas Domain / Application / Infrastructure / Api
- [x] PostgreSQL com migrations
- [x] `UPDATE` condicional atômico na baixa de estoque (e concorrência otimista no pedido), com experimento e testes que provam a correção
- [x] Reserva com expiração e pagamento simulado
- [x] Perfil administrador e aprovação de organizadores
- [x] Refresh token com rotação
- [x] RabbitMQ + padrão Outbox para emitir ingressos e e-mails
- [x] Logs estruturados, health checks, Docker Compose
- [x] Testes de integração com Testcontainers; GitHub Actions
- [x] Frontend React + TypeScript

## Versão Sênior ✅

- [x] Fila virtual (sala de espera) para vendas de alta demanda — [ADR 0009](adr/0009-fila-virtual-no-redis.md)
- [x] Limites distribuídos por usuário/IP e limite global; WAF na AWS
- [x] Idempotência nas reservas e nos pagamentos — [ADR 0010](adr/0010-idempotencia-por-chave.md)
- [x] Cache híbrido da vitrine — [ADR 0011](adr/0011-cache-hibrido.md)
- [x] OpenTelemetry (traces, métricas e logs correlacionados) — [ADR 0012](adr/0012-observabilidade-opentelemetry.md)
- [x] Testes de carga com k6 e metas de desempenho (SLOs)
- [x] Testes ponta a ponta com Playwright
- [x] Infraestrutura como código (Terraform) na AWS — [ADR 0014](adr/0014-ecs-fargate.md)
- [x] Backup, recuperação de desastre, modelo de ameaças e runbooks
- [x] Extração de serviço avaliada e **descartada** com justificativa — [ADR 0013](adr/0013-monolito-modular-mantido.md)

## Próximos passos (fora do escopo das três versões)

- [ ] Provedor de pagamento real com webhook assinado e conciliação
- [ ] CAPTCHA na entrada da fila e limite de ingressos por CPF (anti-cambista)
- [ ] Gravar a resposta idempotente na mesma transação do pedido
- [ ] Aplicar o Terraform numa conta de homologação e exercitar a recuperação regional
- [ ] CDN para a vitrine e imagens dos eventos
