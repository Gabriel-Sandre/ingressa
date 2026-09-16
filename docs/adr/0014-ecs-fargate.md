# ADR 0014 — ECS Fargate em vez de Kubernetes

**Status:** aceita · **Versão:** Sênior · **Data:** 2026-09

## Contexto

A versão Sênior descreve um ambiente de produção na AWS. São três aplicações em containers (API, Worker, interface) e uma tarefa de migração.

## Alternativas

1. EC2 com Docker Compose — barato, mas sem autoscaling nem substituição automática.
2. EKS (Kubernetes gerenciado).
3. **ECS com Fargate.**
4. App Runner / Elastic Beanstalk.

## Decisão

Opção 3, descrita em Terraform.

## Justificativa

- Sem servidores nem plano de controle para atualizar; integração nativa com ALB, Secrets Manager, CloudWatch e autoscaling.
- Kubernetes compensa com muitos serviços, vários times ou necessidade de portabilidade entre nuvens — nenhum dos três se aplica. O custo fixo do EKS e o esforço de operação seriam maiores que o benefício.
- App Runner não permite o Worker sem porta HTTP nem controle fino de rede.

## Consequências

- ✅ Deploy gradual com rollback automático (*circuit breaker*), escala por CPU e por requisições.
- ❌ Dependência da AWS (o Terraform não é portável). As imagens e o compose continuam rodando em qualquer lugar.
- ❌ Fargate custa mais por vCPU que EC2; aceito pelo menor custo de operação.
