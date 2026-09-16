# ---------- PostgreSQL (RDS) ----------

resource "aws_db_subnet_group" "postgres" {
  name       = local.nome
  subnet_ids = aws_subnet.privada[*].id
}

resource "aws_db_parameter_group" "postgres" {
  name   = "${local.nome}-pg17"
  family = "postgres17"

  # Só conexões criptografadas e registro de consultas lentas (acima de 500 ms).
  parameter {
    name  = "rds.force_ssl"
    value = "1"
  }

  parameter {
    name  = "log_min_duration_statement"
    value = "500"
  }
}

resource "aws_db_instance" "postgres" {
  identifier     = local.nome
  engine         = "postgres"
  engine_version = "17"
  instance_class = var.banco.classe

  db_name  = "ingressa"
  username = "ingressa"
  # A senha fica no Secrets Manager e no estado do Terraform (bucket S3 criptografado).
  password = random_password.postgres.result

  allocated_storage     = var.banco.armazenamento_gb
  max_allocated_storage = var.banco.armazenamento_gb * 4
  storage_type          = "gp3"
  storage_encrypted     = true
  kms_key_id            = aws_kms_key.principal.arn

  multi_az               = var.banco.multi_az
  db_subnet_group_name   = aws_db_subnet_group.postgres.name
  vpc_security_group_ids = [aws_security_group.dados.id]
  parameter_group_name   = aws_db_parameter_group.postgres.name
  publicly_accessible    = false

  # Recuperação a qualquer instante dos últimos N dias (point-in-time recovery).
  backup_retention_period   = var.banco.retencao_backup
  backup_window             = "06:00-07:00"
  maintenance_window        = "sun:07:30-sun:08:30"
  copy_tags_to_snapshot     = true
  delete_automated_backups  = false
  deletion_protection       = var.ambiente == "producao"
  skip_final_snapshot       = var.ambiente != "producao"
  final_snapshot_identifier = "${local.nome}-final"

  performance_insights_enabled    = true
  performance_insights_kms_key_id = aws_kms_key.principal.arn
  enabled_cloudwatch_logs_exports = ["postgresql"]
  auto_minor_version_upgrade      = true
}

# ---------- Redis (ElastiCache): fila virtual, limites e cache ----------

resource "aws_elasticache_subnet_group" "redis" {
  name       = local.nome
  subnet_ids = aws_subnet.privada[*].id
}

resource "aws_elasticache_replication_group" "redis" {
  replication_group_id = local.nome
  description          = "Fila virtual, limites distribuídos e cache"
  engine               = "redis"
  engine_version       = "7.1"
  node_type            = var.redis_classe
  port                 = 6379

  # Primário + réplica em outra zona, com failover automático.
  num_cache_clusters         = 2
  automatic_failover_enabled = true
  multi_az_enabled           = true

  subnet_group_name  = aws_elasticache_subnet_group.redis.name
  security_group_ids = [aws_security_group.dados.id]

  at_rest_encryption_enabled = true
  kms_key_id                 = aws_kms_key.principal.arn
  transit_encryption_enabled = true
  auth_token                 = random_password.redis.result

  snapshot_retention_limit = 3
  snapshot_window          = "05:00-06:00"
  apply_immediately        = false
}

# ---------- RabbitMQ (Amazon MQ) ----------

resource "aws_mq_broker" "rabbitmq" {
  broker_name        = local.nome
  engine_type        = "RabbitMQ"
  engine_version     = "4.2"
  host_instance_type = var.mensageria_classe
  # Cluster de 3 nós em produção; instância única em homologação para economizar.
  deployment_mode            = var.ambiente == "producao" ? "CLUSTER_MULTI_AZ" : "SINGLE_INSTANCE"
  subnet_ids                 = var.ambiente == "producao" ? aws_subnet.privada[*].id : [aws_subnet.privada[0].id]
  security_groups            = [aws_security_group.dados.id]
  publicly_accessible        = false
  auto_minor_version_upgrade = true

  user {
    username = "ingressa"
    password = random_password.rabbitmq.result
  }

  encryption_options {
    kms_key_id        = aws_kms_key.principal.arn
    use_aws_owned_key = false
  }

  logs {
    general = true
  }

  maintenance_window_start_time {
    day_of_week = "SUNDAY"
    time_of_day = "08:00"
    time_zone   = "UTC"
  }
}
