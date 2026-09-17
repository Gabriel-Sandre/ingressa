data "aws_caller_identity" "atual" {}

# Uma chave KMS para criptografar banco, cache, logs, segredos e imagens.
resource "aws_kms_key" "principal" {
  description             = "Criptografia dos dados do Ingressa (${var.ambiente})"
  enable_key_rotation     = true
  deletion_window_in_days = 30

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "Administracao"
        Effect    = "Allow"
        Principal = { AWS = "arn:aws:iam::${data.aws_caller_identity.atual.account_id}:root" }
        Action    = "kms:*"
        Resource  = "*"
      },
      {
        Sid       = "AlarmesENotificacoes"
        Effect    = "Allow"
        Principal = { Service = "cloudwatch.amazonaws.com" }
        Action    = ["kms:Decrypt", "kms:GenerateDataKey*"]
        Resource  = "*"
      },
      {
        Sid       = "CloudWatchLogs"
        Effect    = "Allow"
        Principal = { Service = "logs.${var.regiao}.amazonaws.com" }
        Action    = ["kms:Encrypt*", "kms:Decrypt*", "kms:ReEncrypt*", "kms:GenerateDataKey*", "kms:Describe*"]
        Resource  = "*"
      }
    ]
  })
}

resource "aws_kms_alias" "principal" {
  name          = "alias/${local.nome}"
  target_key_id = aws_kms_key.principal.key_id
}

# ---------- Grupos de segurança: cada camada só aceita a camada anterior ----------

resource "aws_security_group" "alb" {
  name        = "${local.nome}-alb"
  description = "Load balancer: HTTPS da internet"
  vpc_id      = aws_vpc.principal.id
}

resource "aws_vpc_security_group_ingress_rule" "alb_https" {
  security_group_id = aws_security_group.alb.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}

resource "aws_vpc_security_group_ingress_rule" "alb_http" {
  security_group_id = aws_security_group.alb.id
  description       = "Somente para redirecionar para HTTPS"
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 80
  to_port           = 80
}

resource "aws_vpc_security_group_egress_rule" "alb_para_apps" {
  security_group_id            = aws_security_group.alb.id
  referenced_security_group_id = aws_security_group.apps.id
  ip_protocol                  = "tcp"
  from_port                    = 8080
  to_port                      = 8080
}

resource "aws_security_group" "apps" {
  name        = "${local.nome}-apps"
  description = "Containers da API, do Worker e da interface"
  vpc_id      = aws_vpc.principal.id
}

# Service Connect: a interface fala com a API pelo nome "api:8080" dentro da VPC.
resource "aws_vpc_security_group_ingress_rule" "apps_entre_si" {
  security_group_id            = aws_security_group.apps.id
  description                  = "Chamadas entre tarefas (ECS Service Connect)"
  referenced_security_group_id = aws_security_group.apps.id
  ip_protocol                  = "tcp"
  from_port                    = 8080
  to_port                      = 8080
}

resource "aws_vpc_security_group_ingress_rule" "apps_do_alb" {
  security_group_id            = aws_security_group.apps.id
  referenced_security_group_id = aws_security_group.alb.id
  ip_protocol                  = "tcp"
  from_port                    = 8080
  to_port                      = 8080
}

resource "aws_vpc_security_group_egress_rule" "apps_saida" {
  security_group_id = aws_security_group.apps.id
  description       = "Banco, cache, mensageria, e-mail e APIs da AWS"
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "-1"
}

resource "aws_security_group" "dados" {
  name        = "${local.nome}-dados"
  description = "PostgreSQL, Redis e RabbitMQ: somente a partir dos containers"
  vpc_id      = aws_vpc.principal.id
}

resource "aws_vpc_security_group_ingress_rule" "dados_das_apps" {
  for_each = {
    postgres = 5432
    redis    = 6379
    amqps    = 5671
  }

  security_group_id            = aws_security_group.dados.id
  referenced_security_group_id = aws_security_group.apps.id
  description                  = each.key
  ip_protocol                  = "tcp"
  from_port                    = each.value
  to_port                      = each.value
}

# ---------- WAF: regras gerenciadas + limite por IP antes de chegar na aplicação ----------

resource "aws_wafv2_web_acl" "principal" {
  name  = local.nome
  scope = "REGIONAL"

  default_action {
    allow {}
  }

  rule {
    name     = "regras-comuns-aws"
    priority = 1

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        vendor_name = "AWS"
        name        = "AWSManagedRulesCommonRuleSet"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "regras-comuns"
      sampled_requests_enabled   = true
    }
  }

  rule {
    name     = "ips-com-ma-reputacao"
    priority = 2

    override_action {
      none {}
    }

    statement {
      managed_rule_group_statement {
        vendor_name = "AWS"
        name        = "AWSManagedRulesAmazonIpReputationList"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "ip-reputacao"
      sampled_requests_enabled   = true
    }
  }

  rule {
    name     = "limite-por-ip"
    priority = 3

    action {
      block {}
    }

    statement {
      rate_based_statement {
        limit              = var.limite_requisicoes_por_ip
        aggregate_key_type = "IP"
      }
    }

    visibility_config {
      cloudwatch_metrics_enabled = true
      metric_name                = "limite-por-ip"
      sampled_requests_enabled   = true
    }
  }

  visibility_config {
    cloudwatch_metrics_enabled = true
    metric_name                = local.nome
    sampled_requests_enabled   = true
  }
}

resource "aws_wafv2_web_acl_association" "alb" {
  resource_arn = aws_lb.principal.arn
  web_acl_arn  = aws_wafv2_web_acl.principal.arn
}
