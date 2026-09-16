terraform {
  required_version = ">= 1.9"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # O estado fica num bucket S3 com versionamento e trava nativa (use_lockfile).
  # Os valores vêm de um arquivo por ambiente:
  #   terraform init -backend-config=ambientes/producao.s3.tfbackend
  backend "s3" {}
}

provider "aws" {
  region = var.regiao

  default_tags {
    tags = {
      Projeto    = "ingressa"
      Ambiente   = var.ambiente
      Gerenciado = "terraform"
    }
  }
}
