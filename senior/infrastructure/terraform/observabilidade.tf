resource "aws_sns_topic" "alertas" {
  name              = "${local.nome}-alertas"
  kms_master_key_id = aws_kms_key.principal.id
}

resource "aws_sns_topic_subscription" "email" {
  topic_arn = aws_sns_topic.alertas.arn
  protocol  = "email"
  endpoint  = var.email_alertas
}

locals {
  # Alarmes ligados aos objetivos de serviço (SLOs) definidos em docs/slos.md.
  alarmes = {
    erros-5xx = {
      descricao   = "Mais de 50 respostas 5xx por minuto, 3 minutos seguidos"
      namespace   = "AWS/ApplicationELB"
      metrica     = "HTTPCode_Target_5XX_Count"
      estatistica = "Sum"
      percentil   = null
      limite      = 50
      periodos    = 3
      comparacao  = "GreaterThanThreshold"
      dimensoes   = tomap({ LoadBalancer = aws_lb.principal.arn_suffix })
    }
    latencia-api = {
      descricao   = "p95 da API acima de 500 ms"
      namespace   = "AWS/ApplicationELB"
      metrica     = "TargetResponseTime"
      estatistica = null
      percentil   = "p95"
      limite      = 0.5
      periodos    = 5
      comparacao  = "GreaterThanThreshold"
      dimensoes   = tomap({ LoadBalancer = aws_lb.principal.arn_suffix, TargetGroup = aws_lb_target_group.alvos["api"].arn_suffix })
    }
    alvos-sem-saude = {
      descricao   = "Instâncias da API sem saúde"
      namespace   = "AWS/ApplicationELB"
      metrica     = "UnHealthyHostCount"
      estatistica = "Maximum"
      percentil   = null
      limite      = 0
      periodos    = 3
      comparacao  = "GreaterThanThreshold"
      dimensoes   = tomap({ LoadBalancer = aws_lb.principal.arn_suffix, TargetGroup = aws_lb_target_group.alvos["api"].arn_suffix })
    }
    banco-cpu = {
      descricao   = "CPU do PostgreSQL acima de 80%"
      namespace   = "AWS/RDS"
      metrica     = "CPUUtilization"
      estatistica = "Average"
      percentil   = null
      limite      = 80
      periodos    = 5
      comparacao  = "GreaterThanThreshold"
      dimensoes   = tomap({ DBInstanceIdentifier = aws_db_instance.postgres.identifier })
    }
    banco-espaco = {
      descricao   = "Menos de 10 GB livres no PostgreSQL"
      namespace   = "AWS/RDS"
      metrica     = "FreeStorageSpace"
      estatistica = "Minimum"
      percentil   = null
      limite      = 10737418240
      periodos    = 1
      comparacao  = "LessThanThreshold"
      dimensoes   = tomap({ DBInstanceIdentifier = aws_db_instance.postgres.identifier })
    }
    redis-memoria = {
      descricao   = "Memória do Redis acima de 80%"
      namespace   = "AWS/ElastiCache"
      metrica     = "DatabaseMemoryUsagePercentage"
      estatistica = "Maximum"
      percentil   = null
      limite      = 80
      periodos    = 3
      comparacao  = "GreaterThanThreshold"
      dimensoes   = tomap({ ReplicationGroupId = aws_elasticache_replication_group.redis.id })
    }
  }
}

resource "aws_cloudwatch_metric_alarm" "alarmes" {
  for_each = local.alarmes

  alarm_name          = "${local.nome}-${each.key}"
  alarm_description   = each.value.descricao
  namespace           = each.value.namespace
  metric_name         = each.value.metrica
  statistic           = each.value.estatistica
  extended_statistic  = each.value.percentil
  period              = 60
  evaluation_periods  = each.value.periodos
  threshold           = each.value.limite
  comparison_operator = each.value.comparacao
  dimensions          = each.value.dimensoes
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alertas.arn]
  ok_actions          = [aws_sns_topic.alertas.arn]
}

# Mensagens paradas na fila de falhas indicam um bug ou uma dependência fora do ar.
resource "aws_cloudwatch_metric_alarm" "fila_de_falhas" {
  alarm_name          = "${local.nome}-fila-de-falhas"
  alarm_description   = "Há mensagens na fila de falhas do RabbitMQ (ver runbook 'mensagens com falha')"
  namespace           = "AWS/AmazonMQ"
  metric_name         = "MessageCount"
  statistic           = "Maximum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 0
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alertas.arn]

  dimensions = {
    Broker      = aws_mq_broker.rabbitmq.broker_name
    VirtualHost = "/"
    Queue       = "ingressa.falhas"
  }
}
