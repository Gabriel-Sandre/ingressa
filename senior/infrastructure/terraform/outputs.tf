output "endereco_load_balancer" {
  description = "Aponte o DNS do domínio (CNAME/ALIAS) para este endereço."
  value       = aws_lb.principal.dns_name
}

output "repositorios_ecr" {
  description = "Onde o pipeline publica as imagens."
  value       = { for nome, repo in aws_ecr_repository.imagens : nome => repo.repository_url }
}

output "cluster_ecs" {
  value = aws_ecs_cluster.principal.name
}

output "tarefa_de_migracao" {
  description = "Definição usada pelo pipeline para aplicar migrations antes do deploy."
  value       = aws_ecs_task_definition.migracao.arn
}

output "subredes_privadas" {
  value = aws_subnet.privada[*].id
}

output "grupo_de_seguranca_apps" {
  value = aws_security_group.apps.id
}
