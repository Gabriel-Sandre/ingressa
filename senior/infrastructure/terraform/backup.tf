# Backups diários com cópia para outra região (recuperação de desastre regional).
# O RDS também guarda backups contínuos para recuperação a qualquer instante (PITR).

provider "aws" {
  alias  = "recuperacao"
  region = var.regiao_de_recuperacao
}

resource "aws_backup_vault" "principal" {
  name        = local.nome
  kms_key_arn = aws_kms_key.principal.arn
}

resource "aws_kms_key" "recuperacao" {
  provider                = aws.recuperacao
  description             = "Backups do Ingressa (${var.ambiente}) na região de recuperação"
  enable_key_rotation     = true
  deletion_window_in_days = 30
}

resource "aws_backup_vault" "recuperacao" {
  provider    = aws.recuperacao
  name        = "${local.nome}-recuperacao"
  kms_key_arn = aws_kms_key.recuperacao.arn
}

resource "aws_backup_plan" "principal" {
  name = local.nome

  rule {
    rule_name         = "diario"
    target_vault_name = aws_backup_vault.principal.name
    schedule          = "cron(0 5 * * ? *)"

    lifecycle {
      delete_after = 35
    }

    copy_action {
      destination_vault_arn = aws_backup_vault.recuperacao.arn

      lifecycle {
        delete_after = 35
      }
    }
  }

  rule {
    rule_name         = "mensal"
    target_vault_name = aws_backup_vault.principal.name
    schedule          = "cron(0 6 1 * ? *)"

    lifecycle {
      cold_storage_after = 30
      delete_after       = 365
    }
  }
}

resource "aws_iam_role" "backup" {
  name = "${local.nome}-backup"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "backup.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "backup" {
  role       = aws_iam_role.backup.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSBackupServiceRolePolicyForBackup"
}

resource "aws_backup_selection" "banco" {
  name         = "${local.nome}-banco"
  plan_id      = aws_backup_plan.principal.id
  iam_role_arn = aws_iam_role.backup.arn
  resources    = [aws_db_instance.postgres.arn]
}
