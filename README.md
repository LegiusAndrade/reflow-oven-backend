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
A documentação **Swagger** fica em `/swagger`. Health-check em `/health`.

**Login de desenvolvimento:** qualquer usuário do seed (ex.: `Lucas Silva`, admin) com a senha
`reflow1234`, ou o login técnico oculto `calibracao` / `calibra`.

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

- `auth` (login/me/forgot-password) · `programs` (+ `favorite`) · `runs` (start/stop/status)
- `executions` · `errors` · `changes` · `system-log` · `fault-types` (Relatórios)
- `settings` · `calibration` (+ `wizard`) · `network/ping`
- `diagnostics` (overview/readings/self-test) · `maintenance` (overview/cleanup/factory-reset) · `device`

Tempo real (JWT via `?access_token=`):

- `/hubs/telemetry` — `SubscribeRun(runId)`; recebe `TraceSample`, `RunPhaseChanged`, `RunStatusChanged`, `RunCompleted`.
- `/hubs/diagnostics` — recebe `ReadingTick` a 1 Hz.

## 🧭 Convenções

Detalhes em [`CLAUDE.md`](./CLAUDE.md). Em resumo: identificadores em inglês; **literais de enum em pt-BR
são parte do contrato** (acentos importam); limites centralizados em `DomainConstants` (espelho de
`limits.ts`); **commits sempre em inglês** (sem trailer de autoria); desenvolvimento na branch `develop`.

## 🛡️ Licença

MIT.
