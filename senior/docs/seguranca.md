# Segurança — modelo de ameaças (STRIDE)

Escopo: compra de ingressos na versão Sênior (interface, API, Worker, PostgreSQL, Redis,
RabbitMQ) e o ambiente AWS descrito em `infrastructure/terraform`.

## Fronteiras de confiança

1. Internet → WAF/ALB (TLS 1.3)
2. ALB → tarefas nas sub-redes privadas (security group só aceita o ALB)
3. Aplicações → dados (security group só aceita as aplicações; TLS em PostgreSQL, Redis e RabbitMQ)
4. Pipeline → AWS (quem pode publicar imagens e aplicar Terraform)

## Ameaças e controles

| STRIDE | Ameaça | Controle implementado | Risco residual |
|---|---|---|---|
| **S**poofing | Roubo de sessão | Access token de 15 min; refresh token em cookie `HttpOnly; Secure; SameSite=Strict`; rotação com detecção de reuso (revoga a família) | Malware no dispositivo do cliente |
| S | Força bruta de senha | Limite distribuído `autenticacao` (10/min por IP), PBKDF2 600 mil iterações, WAF | Ataque distribuído em muitos IPs |
| S | Token forjado | HS256 com chave ≥ 32 bytes do Secrets Manager; rotação com `ChavesAnteriores` | Vazamento da chave → runbook de rotação |
| **T**ampering | Alterar preço/quantidade | Preço sempre lido do banco; validação no domínio; CHECK `Ocupados <= Capacidade` | — |
| T | Furar a fila virtual | Passe de uso único ligado ao usuário, validado e consumido atomicamente no Redis | Várias contas do mesmo robô (ver abaixo) |
| T | Webhook de pagamento falso | Endpoint só simula o provedor; um provedor real exigiria assinatura HMAC | **Aberto** até integrar um provedor real |
| **R**epudiation | "Não fui eu que comprei" | Logs estruturados com usuário e trace; ações de admin registradas | Logs não são imutáveis (sem WORM) |
| **I**nformation disclosure | Vazamento de dados | TLS em todos os trechos; KMS em RDS, Redis, MQ, logs, backups; nenhuma senha no repositório (gitleaks no CI) | Senhas estão no *state* do Terraform (bucket restrito e criptografado) |
| I | Enumeração de e-mails | Mesma resposta e mesmo custo para e-mail inexistente e senha errada | Cadastro revela e-mail já usado |
| I | Erros com detalhes internos | Problem Details sem stack trace fora de Development | — |
| **D**enial of service | Pico de acesso / robôs | WAF (limite por IP + regras gerenciadas), limitador global por IP, limites distribuídos, fila virtual, autoscaling | Ataque volumétrico grande (AWS Shield Advanced não incluído) |
| D | Redis fora do ar | Limites em *fail-open*; cache cai para o banco; eventos com fila param de vender (decisão do ADR 0009) | Vendas com fila interrompidas |
| **E**levation of privilege | Cliente acessando rotas de admin/organizador | Autorização por perfil e por dono do recurso, testada na integração | — |
| E | Container comprometido | Usuário sem root, sistema de arquivos somente leitura (API/Worker), permissões IAM mínimas, API sem senha do RabbitMQ/SMTP | — |
| E | Dependência vulnerável | Dependabot, `dotnet list package --vulnerable`, `npm audit`, Trivy nas imagens, CodeQL | Janela entre divulgação e correção |

## Robôs e cambistas

Não resolvido por completo. Hoje: limite por IP, WAF, passe por usuário e uma reserva por
passe. Próximos passos: CAPTCHA na entrada da fila, verificação de telefone, limite de
ingressos por CPF.

## Exceções aceitas nas varreduras

| Ferramenta | Regra | Motivo |
|---|---|---|
| checkov | ALB sem *listener* HTTP bloqueado | A porta 80 só redireciona para HTTPS |
| checkov | Tarefa `web` com sistema de arquivos gravável | O nginx gera a configuração a partir do template ao iniciar |
| ZAP | Avisos informativos sobre respostas `/api/*` sem CSP | CSP é enviada pela interface (nginx); a API só devolve JSON com `nosniff` e `no-store` |

## Como reportar

Problemas de segurança: abra uma *security advisory* privada no GitHub, não uma issue pública.
