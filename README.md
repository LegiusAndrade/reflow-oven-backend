# Forno de Refusão — Backend

![Status](https://img.shields.io/badge/Status-Em%20Desenvolvimento-yellow)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![EF Core](https://img.shields.io/badge/EF%20Core-10-512BD4)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-336791)

API e serviço de controle para um **forno de refusão (reflow oven)** usado na soldagem de componentes
**SMD**. É o par do frontend Next.js em [`../reflow-oven-front`](../reflow-oven-front): substitui a
persistência _mock_ em `localStorage` por um backend real, executa o processo, transmite a telemetria ao
vivo e armazena perfis, execuções, falhas e o histórico de alterações.

Roda em um **Raspberry Pi / Orange Pi** e se comunica por **RS422** com a placa de potência baseada em
**STM32** (abstraída — um simulador acompanha o projeto). A interface servida é em **pt-BR**.

> O frontend já está **integrado** a esta API: ele consome os endpoints REST e os hubs SignalR
> (sem mais `localStorage` _mock_). Veja a seção "Integração com o backend" em
> [`../reflow-oven-front`](../reflow-oven-front).

## 🧰 Stack

| Camada       | Tecnologia                                                        |
| ------------ | ----------------------------------------------------------------- |
| Plataforma   | **.NET 10** · **ASP.NET Core** (Web API + SignalR)                |
| Persistência | **Entity Framework Core 10** · **PostgreSQL** (Npgsql)            |
| Auth         | **JWT** (Bearer) · papéis Admin/Regular · hashing **BCrypt**      |
| Tempo real   | **SignalR** — telemetria da execução e leituras de diagnóstico    |
| Arquitetura  | **Clean Architecture** (Domain · Application · Infrastructure · Api) |

## 🔌 Pré-requisitos

- **.NET 10 SDK** (instalado em `~/.dotnet`; veja [`CLAUDE.md`](./CLAUDE.md) para o `PATH`).
- **PostgreSQL** — via `docker compose` (incluso) ou uma instância local.

## 🚀 Como rodar

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

docker compose up -d                        # sobe o PostgreSQL
dotnet run --project src/ReflowOven.Api      # aplica migrations, faz o seed e sobe a API
```

A API sobe em `https://localhost:5001` / `http://localhost:5000` (veja `Properties/launchSettings.json`).
A referência de API interativa (**Scalar**) fica em `/scalar` (doc OpenAPI em `/openapi/v1.json`).
Health-check em `/health`. Todos os eventos (login/logout, alterações, execuções, erros) e cada
requisição HTTP são logados no **console** via **Serilog**.

**Login de desenvolvimento:** qualquer usuário do seed (ex.: `lucas.silva`, admin) com a senha
`reflow1234`, ou o login técnico oculto `calibracao` / `calibra`.

## 🛠️ Solução de problemas (perrengues comuns)

Tropeços que enfrentamos ao subir o projeto pela primeira vez:

| Sintoma | Causa | Solução |
| --- | --- | --- |
| `dotnet: command not found` (ou o VSCode não acha o .NET) | O SDK está em `~/.dotnet`, fora do `PATH` global. | Adicione ao `~/.bashrc`: `export DOTNET_ROOT="$HOME/.dotnet"` e `export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"`; reabra o terminal. |
| `docker ... permission denied ... /var/run/docker.sock` | Usuário fora do grupo `docker` (e/ou a porta 5432 já está em uso por um Postgres local). | Use o Postgres local (bloco abaixo) **ou** `sudo usermod -aG docker $USER && newgrp docker` (e pare o Postgres local antes, pois a 5432 não pode ser usada pelos dois). |
| `28P01: password authentication failed for user "reflow"` | O papel/banco `reflow`/`reflowoven` ainda não existem. | Crie-os (bloco abaixo). |
| `Connection refused` / `No connection could be made` | Não há PostgreSQL rodando na 5432. | Suba o banco: `docker compose up -d` ou inicie o Postgres local. |
| Log `fail ... __EFMigrationsHistory` no 1º start | **Normal**: a tabela de controle não existe num banco vazio; o EF a cria em seguida. | Nada a fazer. |
| `warn ... No XML encryptor configured` | Aviso benigno do DataProtection em desenvolvimento. | Ignorar em dev; configurar em produção. |

**Criar o papel e o banco no PostgreSQL local** (alternativa ao Docker — foi o que usamos):

```bash
sudo -u postgres psql <<'SQL'
CREATE ROLE reflow WITH LOGIN PASSWORD 'reflow';
CREATE DATABASE reflowoven OWNER reflow;
GRANT ALL PRIVILEGES ON DATABASE reflowoven TO reflow;
SQL
```

> Se o papel já existir, troque a 1ª linha por `ALTER ROLE reflow WITH LOGIN PASSWORD 'reflow';`. Se o
> banco já existir, pule o `CREATE DATABASE`. Usuário/senha/banco devem bater com a _connection string_
> em `appsettings.json`. Detalhes em [`docs/GUIA-DO-PROJETO.md`](./docs/GUIA-DO-PROJETO.md#9-solução-de-problemas).

## 📜 Comandos

| Comando                                                | Descrição                              |
| ------------------------------------------------------ | -------------------------------------- |
| `dotnet build ReflowOven.slnx`                         | Compila a solução (formato `.slnx`)    |
| `dotnet run --project src/ReflowOven.Api`              | Sobe a API (migra + seed no startup)   |
| `dotnet test`                                          | Executa os testes                      |
| `dotnet ef migrations add <Nome> -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api -o Persistence/Migrations` | Nova migration |

## 📁 Estrutura

```
src/
├── ReflowOven.Domain/          # Entidades, enums, constantes (espelho de limits.ts), abstrações de hardware
├── ReflowOven.Application/     # DTOs (contrato JSON), serviços, ProfileBuilder, Defaults (seed)
├── ReflowOven.Infrastructure/  # DbContext + mapeamentos, migrations, seeder, placa (sim/RS422), JWT, run loop
└── ReflowOven.Api/             # Program.cs, controllers, hubs SignalR, middleware
tests/
└── ReflowOven.Tests/           # Testes (ProfileBuilder, hasher, Defaults, simulador)
```

## 🔭 Visão geral da API

REST sob `/api` (autenticado por padrão; `AdminOnly` para escritas; `CalibrationOnly` para a calibração):

- `auth` (login/me/forgot-password/change-password) · `me/preferences` (tema + séries por usuário) · `programs` (+ `favorite`) · `runs` (start/stop/status)
- `executions` (detalhe inclui o trace multi-sinal) · `errors` · `changes` · `system-log` · `fault-types` — Relatórios com filtro server-side (`?status/action/severity/level`)
- `settings` · `calibration` (+ `wizard`) · `network/ping` (com `port` opcional → TCP)
- `diagnostics` (overview/readings/self-test) · `maintenance` (overview/cleanup/factory-reset) · `device`
- `system` (status/metrics/network/wifi/interfaces/time/ntp/update/connectivity/reboot/shutdown — OS do OrangePi) · `notifications` (feed do sininho)

Tempo real (JWT via `?access_token=`):

- `/hubs/telemetry` — `SubscribeRun(runId)`; recebe `TraceSample`, `RunPhaseChanged`, `RunStatusChanged`, `RunCompleted`.
- `/hubs/diagnostics` — recebe `ReadingTick` a 1 Hz.

## 🧭 Convenções

Detalhes em [`CLAUDE.md`](./CLAUDE.md). Em resumo: identificadores em inglês; **literais de enum em pt-BR
são parte do contrato** (acentos importam); limites centralizados em `DomainConstants` (espelho de
`limits.ts`); **commits sempre em inglês** (sem trailer de autoria); desenvolvimento na branch `develop`.

## 🛡️ Licença

MIT.
