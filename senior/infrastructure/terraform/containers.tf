# ---------- Registro de imagens ----------

resource "aws_ecr_repository" "imagens" {
  for_each = toset(["api", "worker", "web"])

  name                 = "ingressa/${each.key}"
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }

  encryption_configuration {
    encryption_type = "KMS"
    kms_key         = aws_kms_key.principal.arn
  }
}

resource "aws_ecr_lifecycle_policy" "imagens" {
  for_each   = aws_ecr_repository.imagens
  repository = each.value.name

  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Mantém as 30 imagens mais recentes"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 30
      }
      action = { type = "expire" }
    }]
  })
}

# ---------- Cluster ECS (Fargate: sem servidores para administrar) ----------

# Namespace do Service Connect: dentro do cluster, "api:8080" resolve para as tarefas da API.
resource "aws_service_discovery_http_namespace" "principal" {
  name = local.nome
}

resource "aws_ecs_cluster" "principal" {
  name = local.nome

  service_connect_defaults {
    namespace = aws_service_discovery_http_namespace.principal.arn
  }

  setting {
    name  = "containerInsights"
    value = "enhanced"
  }
}

resource "aws_cloudwatch_log_group" "apps" {
  for_each          = toset(["api", "worker", "web", "otel"])
  name              = "/ingressa/${var.ambiente}/${each.key}"
  retention_in_days = 30
  kms_key_id        = aws_kms_key.principal.arn
}

# ---------- Permissões ----------

data "aws_iam_policy_document" "tarefas_ecs" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# Usado pelo ECS para baixar a imagem, ler os segredos e escrever logs.
resource "aws_iam_role" "execucao" {
  name               = "${local.nome}-execucao"
  assume_role_policy = data.aws_iam_policy_document.tarefas_ecs.json
}

resource "aws_iam_role_policy_attachment" "execucao" {
  role       = aws_iam_role.execucao.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "execucao_segredos" {
  role = aws_iam_role.execucao.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = [aws_secretsmanager_secret.aplicacao.arn]
      },
      {
        Effect   = "Allow"
        Action   = ["kms:Decrypt"]
        Resource = [aws_kms_key.principal.arn]
      }
    ]
  })
}

# Usado pela aplicação em execução: só o necessário para enviar telemetria.
resource "aws_iam_role" "aplicacao" {
  name               = "${local.nome}-aplicacao"
  assume_role_policy = data.aws_iam_policy_document.tarefas_ecs.json
}

resource "aws_iam_role_policy" "aplicacao_telemetria" {
  role = aws_iam_role.aplicacao.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "xray:PutTraceSegments",
        "xray:PutTelemetryRecords",
        "cloudwatch:PutMetricData",
        "logs:PutLogEvents",
        "logs:CreateLogStream"
      ]
      Resource = "*"
    }]
  })
}

# ---------- Definições das tarefas ----------

locals {
  segredos = [
    for chave in ["Jwt__Chave", "ConnectionStrings__Redis", "ConnectionStrings__Ingressa", "RabbitMq__Senha", "PGPASSWORD", "Email__Usuario", "Email__Senha"] : {
      name      = chave
      valueFrom = "${aws_secretsmanager_secret.aplicacao.arn}:${chave}::"
    }
  ]

  # A API e a migração não enviam mensagens nem e-mails: não recebem essas senhas.
  segredos_api = [for s in local.segredos : s if !contains(["RabbitMq__Senha", "Email__Usuario", "Email__Senha"], s.name)]

  ambiente_comum = [
    { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
    { name = "Otlp__Endpoint", value = "http://localhost:4317" },
    { name = "OTEL_RESOURCE_ATTRIBUTES", value = "deployment.environment.name=${var.ambiente}" }
  ]

  # Coletor OpenTelemetry da AWS ao lado de cada aplicação: envia traces ao X-Ray
  # e métricas ao CloudWatch.
  coletor_otel = {
    name      = "otel"
    image     = "public.ecr.aws/aws-observability/aws-otel-collector:v0.45.0"
    essential = false
    command   = ["--config=/etc/ecs/ecs-default-config.yaml"]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.apps["otel"].name
        awslogs-region        = var.regiao
        awslogs-stream-prefix = "otel"
      }
    }
  }

  imagem = { for nome, repo in aws_ecr_repository.imagens : nome => "${repo.repository_url}:${var.versao_imagem}" }
}

resource "aws_ecs_task_definition" "api" {
  family                   = "${local.nome}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.api.cpu
  memory                   = var.api.memoria
  execution_role_arn       = aws_iam_role.execucao.arn
  task_role_arn            = aws_iam_role.aplicacao.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([
    {
      name                   = "api"
      image                  = local.imagem["api"]
      essential              = true
      readonlyRootFilesystem = true
      portMappings           = [{ name = "http", containerPort = 8080, protocol = "tcp" }]
      environment = concat(local.ambiente_comum, [
        { name = "Banco__InicializarAoIniciar", value = "false" },
        { name = "Banco__DadosDeDemonstracao", value = "false" }
      ])
      secrets = local.segredos_api
      healthCheck = {
        command     = ["CMD-SHELL", "curl -fsS http://localhost:8080/health/live || exit 1"]
        interval    = 15
        timeout     = 5
        retries     = 3
        startPeriod = 30
      }
      mountPoints = [{ sourceVolume = "tmp", containerPath = "/tmp" }]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.apps["api"].name
          awslogs-region        = var.regiao
          awslogs-stream-prefix = "api"
        }
      }
    },
    local.coletor_otel
  ])

  volume {
    name = "tmp"
  }
}

resource "aws_ecs_task_definition" "worker" {
  family                   = "${local.nome}-worker"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.worker.cpu
  memory                   = var.worker.memoria
  execution_role_arn       = aws_iam_role.execucao.arn
  task_role_arn            = aws_iam_role.aplicacao.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([
    {
      name                   = "worker"
      image                  = local.imagem["worker"]
      essential              = true
      readonlyRootFilesystem = true
      environment = concat(local.ambiente_comum, [
        { name = "RabbitMq__Host", value = split(":", replace(aws_mq_broker.rabbitmq.instances[0].endpoints[0], "amqps://", ""))[0] },
        { name = "RabbitMq__Porta", value = "5671" },
        { name = "RabbitMq__UsarTls", value = "true" },
        { name = "RabbitMq__Usuario", value = "ingressa" },
        { name = "FilaVirtual__CompradoresSimultaneos", value = tostring(var.compradores_simultaneos) },
        { name = "Email__Host", value = "email-smtp.${var.regiao}.amazonaws.com" },
        { name = "Email__Porta", value = "587" },
        { name = "Email__UsarTls", value = "true" },
        { name = "Email__Remetente", value = "nao-responda@${var.dominio}" }
      ])
      secrets     = local.segredos
      mountPoints = [{ sourceVolume = "tmp", containerPath = "/tmp" }]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.apps["worker"].name
          awslogs-region        = var.regiao
          awslogs-stream-prefix = "worker"
        }
      }
    },
    local.coletor_otel
  ])

  volume {
    name = "tmp"
  }
}

resource "aws_ecs_task_definition" "web" {
  family                   = "${local.nome}-web"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = aws_iam_role.execucao.arn

  container_definitions = jsonencode([{
    name                   = "web"
    image                  = local.imagem["web"]
    essential              = true
    portMappings           = [{ containerPort = 8080, protocol = "tcp" }]
    environment            = [{ name = "API_UPSTREAM", value = "api:8080" }]
    readonlyRootFilesystem = false
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.apps["web"].name
        awslogs-region        = var.regiao
        awslogs-stream-prefix = "web"
      }
    }
  }])
}

# ---------- Migrations: tarefa avulsa executada pelo pipeline antes do deploy ----------

resource "aws_ecs_task_definition" "migracao" {
  family                   = "${local.nome}-migracao"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = aws_iam_role.execucao.arn
  task_role_arn            = aws_iam_role.aplicacao.arn

  container_definitions = jsonencode([{
    name      = "migracao"
    image     = local.imagem["api"]
    essential = true
    command   = ["--migrar-e-sair"]
    environment = [
      { name = "ASPNETCORE_ENVIRONMENT", value = "Production" }
    ]
    secrets = local.segredos_api
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.apps["api"].name
        awslogs-region        = var.regiao
        awslogs-stream-prefix = "migracao"
      }
    }
  }])
}

# ---------- Serviços ----------

locals {
  servicos = {
    api    = { definicao = aws_ecs_task_definition.api.arn, escala = var.api, porta = 8080 }
    worker = { definicao = aws_ecs_task_definition.worker.arn, escala = var.worker, porta = null }
    web    = { definicao = aws_ecs_task_definition.web.arn, escala = { cpu = 256, memoria = 512, minimo = 2, maximo = 4 }, porta = 8080 }
  }
}

resource "aws_ecs_service" "servicos" {
  for_each = local.servicos

  name            = each.key
  cluster         = aws_ecs_cluster.principal.id
  task_definition = each.value.definicao
  desired_count   = each.value.escala.minimo
  launch_type     = "FARGATE"

  # Implantação gradual; se a nova versão não ficar saudável, volta sozinha.
  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200
  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  network_configuration {
    subnets          = aws_subnet.privada[*].id
    security_groups  = [aws_security_group.apps.id]
    assign_public_ip = false
  }

  # A API se publica como "api:8080"; os demais serviços só consomem.
  service_connect_configuration {
    enabled = true

    dynamic "service" {
      for_each = each.key == "api" ? ["api"] : []
      content {
        port_name = "http"
        client_alias {
          dns_name = "api"
          port     = 8080
        }
      }
    }
  }

  dynamic "load_balancer" {
    for_each = each.value.porta == null ? [] : [each.key]
    content {
      target_group_arn = aws_lb_target_group.alvos[load_balancer.value].arn
      container_name   = load_balancer.value
      container_port   = each.value.porta
    }
  }

  lifecycle {
    # O autoscaling controla a quantidade depois da criação.
    ignore_changes = [desired_count]
  }

  depends_on = [aws_lb_listener.https]
}

# ---------- Escala automática ----------

resource "aws_appautoscaling_target" "servicos" {
  for_each = local.servicos

  service_namespace  = "ecs"
  resource_id        = "service/${aws_ecs_cluster.principal.name}/${aws_ecs_service.servicos[each.key].name}"
  scalable_dimension = "ecs:service:DesiredCount"
  min_capacity       = each.value.escala.minimo
  max_capacity       = each.value.escala.maximo
}

resource "aws_appautoscaling_policy" "cpu" {
  for_each = local.servicos

  name               = "${each.key}-cpu"
  policy_type        = "TargetTrackingScaling"
  service_namespace  = aws_appautoscaling_target.servicos[each.key].service_namespace
  resource_id        = aws_appautoscaling_target.servicos[each.key].resource_id
  scalable_dimension = aws_appautoscaling_target.servicos[each.key].scalable_dimension

  target_tracking_scaling_policy_configuration {
    target_value       = 60
    scale_in_cooldown  = 120
    scale_out_cooldown = 30

    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }
  }
}

# A API também escala pelo número de requisições por instância (reage antes da CPU num pico de vendas).
resource "aws_appautoscaling_policy" "requisicoes_api" {
  name               = "api-requisicoes"
  policy_type        = "TargetTrackingScaling"
  service_namespace  = aws_appautoscaling_target.servicos["api"].service_namespace
  resource_id        = aws_appautoscaling_target.servicos["api"].resource_id
  scalable_dimension = aws_appautoscaling_target.servicos["api"].scalable_dimension

  target_tracking_scaling_policy_configuration {
    target_value       = 800
    scale_in_cooldown  = 180
    scale_out_cooldown = 30

    predefined_metric_specification {
      predefined_metric_type = "ALBRequestCountPerTarget"
      resource_label         = "${aws_lb.principal.arn_suffix}/${aws_lb_target_group.alvos["api"].arn_suffix}"
    }
  }
}
