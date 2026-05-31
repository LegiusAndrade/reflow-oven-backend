# Guia do Projeto — Backend do Forno de Refusão

Este guia é para quem **não conhece .NET ainda** e quer entender, arquivo por arquivo, o que existe
no projeto, por que existe, e como **rodar/debugar no VSCode**. Leitura recomendada de cima para baixo.

## Índice

1. [O que o projeto faz](#1-o-que-o-projeto-faz)
2. [Conceitos que você precisa conhecer](#2-conceitos-que-você-precisa-conhecer)
3. [Arquivos da raiz (configuração) — incluindo o "compose"](#3-arquivos-da-raiz)
4. [As 4 camadas, pasta por pasta, arquivo por arquivo](#4-as-4-camadas-arquivo-por-arquivo)
5. [Como tudo se conecta (fluxos de ponta a ponta)](#5-fluxos-de-ponta-a-ponta)
6. [Como debugar no VSCode](#6-como-debugar-no-vscode)
7. [Comandos do dia a dia](#7-comandos-do-dia-a-dia)
8. [Glossário](#8-glossário)
9. [Solução de problemas (os perrengues que passamos)](#9-solução-de-problemas)

---

## 1. O que o projeto faz

É o **backend** (servidor) do forno de refusão. O frontend (a tela _touchscreen_ em Next.js) hoje
guarda tudo no navegador (`localStorage`, que é "de mentira"). Este backend substitui isso por um
**servidor real** que:

- Guarda os dados num banco **PostgreSQL** (programas/perfis, execuções, falhas, usuários, configurações…).
- Expõe uma **API REST** (endpoints HTTP em `/api/...`) para o frontend ler e gravar.
- Transmite a **telemetria ao vivo** durante uma queima via **SignalR** (WebSocket).
- Conversa (ou vai conversar) com a placa de potência **STM32 por RS422** — hoje há um **simulador**.

Quando você rodou `dotnet run`, no log apareceu:

- `Applying migration 'Initial'` → ele **criou as tabelas** no banco.
- Vários `INSERT INTO ...` → o **seed** populou dados iniciais (50 programas, 8 falhas, etc.).
- `Now listening on: http://localhost:5248` → a API **está no ar**. Abra `http://localhost:5248/scalar`.
- A linha `fail ... __EFMigrationsHistory` é **normal** num banco vazio (o EF tenta ler a tabela de
  controle de migrations, ela ainda não existe, então ele cria). Não é um erro.

---

## 2. Conceitos que você precisa conhecer

| Conceito | O que é, em uma frase |
| --- | --- |
| **.NET 10 / C#** | A plataforma e a linguagem. Você compila `.cs` em DLLs e roda com `dotnet`. |
| **ASP.NET Core** | O framework web do .NET (cria a API HTTP e o WebSocket/SignalR). |
| **EF Core** (Entity Framework) | Um **ORM**: você escreve classes C# e ele gera o SQL e fala com o banco por você. |
| **DbContext** | A "sessão" do EF com o banco. Tem um `DbSet<T>` por tabela; `SaveChanges` grava. |
| **Migration** | Um arquivo gerado que descreve **como criar/alterar as tabelas**. Versiona o schema. |
| **Seed** | Dados iniciais inseridos no primeiro start (usuários, programas, catálogo de falhas…). |
| **DI** (Injeção de Dependência) | Você pede um serviço no construtor e o framework te entrega pronto. Evita `new` espalhado. |
| **DTO** | "Data Transfer Object": a classe que vira **JSON** na API (o contrato com o frontend). |
| **JWT** | Um token assinado que o usuário manda no header `Authorization` para provar quem é. |
| **SignalR** | Camada do ASP.NET para enviar dados em tempo real (WebSocket) — usamos para a telemetria. |
| **Middleware** | "Filtros" que o request atravessa (ex.: tratar erros, autenticar) antes de chegar ao controller. |
| **PostgreSQL / Npgsql** | O banco de dados. `Npgsql` é o driver que o EF usa para falar com ele. |

### Clean Architecture (por que **4 projetos**?)

O código está separado em 4 projetos, e as **dependências só apontam para dentro**:

```
Api  ──►  Infrastructure  ──►  Application  ──►  Domain
(web)      (banco/hardware)     (regras/uso)      (modelo puro)
```

- **Domain** não depende de ninguém → é o "coração" (entidades e regras), testável e estável.
- **Application** depende só do Domain → orquestra os casos de uso (serviços) e define **interfaces**
  (ex.: "preciso de um banco", "preciso de uma placa") sem saber **como** são implementadas.
- **Infrastructure** implementa essas interfaces (EF Core, JWT, placa simulada…).
- **Api** é a "casca" fina: recebe HTTP, chama os serviços, devolve JSON.

Benefício: trocar PostgreSQL por outro banco, ou o simulador pela placa real, mexe **só na Infrastructure** —
o Domain e a Application não mudam.

---

## 3. Arquivos da raiz

| Arquivo | Para que serve |
| --- | --- |
| `ReflowOven.slnx` | A **solução**: lista quais projetos fazem parte. Formato novo (XML) do .NET. É o que você abre. |
| `global.json` | **Fixa a versão do SDK** .NET (10.0.x) para todos terem o mesmo build. |
| `Directory.Build.props` | Configurações **compartilhadas por todos os projetos** (alvo `net10.0`, `Nullable`, etc.) — evita repetir em cada `.csproj`. |
| `docker-compose.yml` | Receita para subir o **PostgreSQL em container** (veja abaixo). |
| `*.csproj` (um por projeto) | Define as **dependências** do projeto: pacotes NuGet (`PackageReference`) e referências a outros projetos. É o "package.json" do .NET. |
| `.gitignore` / `.editorconfig` | O que o git ignora (ex.: `bin/`, `obj/`) e o estilo de formatação. |
| `appsettings.json` | Configuração da API: **connection string** do banco, chave do **JWT**, CORS, modo do hardware. |
| `appsettings.Development.json` | Sobrescreve a config quando o ambiente é Development (logs mais verbosos, etc.). |
| `Properties/launchSettings.json` | Define as **portas** locais (HTTP `5248`, HTTPS `7255`) e variáveis ao rodar/debug. |

### O `docker-compose.yml` (o "compose") — o que tem e pra que serve

`docker compose` é uma ferramenta que **sobe serviços em containers** a partir de uma receita YAML.
Aqui ele sobe **um PostgreSQL** já configurado, para você **não precisar instalar o banco na mão**:

```yaml
services:
  postgres:
    image: postgres:18.4          # qual imagem/versão do PostgreSQL baixar
    environment:                  # ao subir, cria automaticamente:
      POSTGRES_DB: reflowoven      #   - o banco "reflowoven"
      POSTGRES_USER: reflow        #   - o usuário "reflow"
      POSTGRES_PASSWORD: reflow    #   - com senha "reflow"
    ports:
      - "5432:5432"               # expõe a porta 5432 do container na sua máquina
    volumes:
      - reflowoven-pgdata:/var/lib/postgresql/data   # guarda os dados em disco (não some ao parar)
    healthcheck: ...              # o docker verifica se o banco já está pronto
```

> **No seu caso** você não usou o container: você já tinha um PostgreSQL instalado na máquina e criou ali
> o usuário `reflow` + banco `reflowoven` (com `sudo -u postgres psql`). Os dois caminhos funcionam — a
> única coisa que importa é a **connection string** em `appsettings.json` apontar para esse banco
> (`Host=localhost;Port=5432;Database=reflowoven;Username=reflow;Password=reflow`). Se um dia quiser o
> container, pare o Postgres local antes (a porta 5432 só pode ser usada por um deles).

---

## 4. As 4 camadas, arquivo por arquivo

### 🟦 `src/ReflowOven.Domain` — o modelo puro (sem dependências)

| Arquivo | O que faz / por quê |
| --- | --- |
| `Entities/User.cs` | Usuário (login, papel Admin/Regular, status), contadores de atividade e token de reset de senha. |
| `Entities/ReflowProgram.cs` | O **perfil térmico** (programa): pontos temperatura×tempo, segmentos, favorito, soft-delete. Chama-se `ReflowProgram` para não colidir com a classe `Program` do .NET. |
| `Entities/ExecutionReport.cs` | O registro de uma execução concluída (curva programada × real, eventos, pico de temp/corrente). |
| `Entities/ErrorLogEntry.cs` | Uma falha registrada + o catálogo de tipos de falha (`FaultType`) + o "snapshot" multi-sinal. |
| `Entities/ChangeLogEntry.cs` | Auditoria: o que mudou (programa/config), quem mudou, o "diff". |
| `Entities/SystemLogEntry.cs` | Linha do log do sistema (Diagnóstico → Manutenção). |
| `Entities/Settings.cs` | As Configurações (PID, forno, processo, tensão, rede) + notificações + séries do gráfico. É um **singleton** (sempre `Id = 1`). |
| `Entities/Calibration.cs` | Calibração da placa (offsets/ganho/PWM). Singleton. |
| `Entities/Device.cs` | Info do dispositivo (versões, disco, SO) e as placas (Potência/Controle). |
| `Enums/Enums.cs` | Todos os enums. **Importante:** os literais em pt-BR (`Concluído`, `Crítico`, `Parábola positiva`…) são **parte do contrato** com o frontend; o atributo `[JsonStringEnumMemberName]` fixa o texto exato. |
| `Common/DomainConstants.cs` | Todos os **limites** (tamanho de nome, faixas de temperatura…). É o espelho do `limits.ts` do frontend. |
| `Hardware/IPowerBoard.cs` + `HardwareTypes.cs` | A **interface** da placa (ler sensores, iniciar/parar, calibrar…) e os tipos de dados dela. Não diz **como** — só o "contrato". |
| `Platform/ISystemController.cs` | A **interface do sistema operacional** do OrangePi (métricas CPU/mem/disco, rede/Wi-Fi/IP, relógio/NTP, atualização OTA, reboot/shutdown, ping do servidor central). É a "placa" do SO — só o contrato. |
| `Entities/Notification.cs` | Uma **notificação do feed** (o sininho da TopBar / tela Notificações): `info`/`error`/`update`. Diferente das preferências fixas de `NotificationSetting`. |
| `Abstractions/IClock.cs` | Abstração do relógio (facilita testes). |
| `Abstractions/ITelemetrySink.cs` | Para onde a telemetria é "empurrada" (implementado na API com SignalR). |
| `GlobalUsings.cs` | `using` globais (evita repetir imports em todo arquivo). |

### 🟩 `src/ReflowOven.Application` — casos de uso e contrato

| Arquivo | O que faz / por quê |
| --- | --- |
| `Abstractions/IAppDbContext.cs` | O "contrato" do banco que os serviços usam (lista de `DbSet`). Implementado na Infrastructure. |
| `Abstractions/Identity.cs` | Interfaces de `ICurrentUser` (quem está logado), `IPasswordHasher`, `IJwtTokenService`, `IEmailSender`. |
| `Abstractions/IRunManager.cs` | Contrato de quem controla a execução (start/stop/status/tick). |
| `Abstractions/ITechnicianCredentials.cs` | Contrato do login técnico oculto (`calibracao`). |
| `Dtos/*.cs` | As classes que viram **JSON** (o contrato com o frontend): `AuthDtos`, `ProgramDtos`, `RunDtos`, `SettingsDtos`, `ReportDtos`, `DiagnosticsDtos`, `MaintenanceDtos`, `DeviceDtos`, `CalibrationDtos`, `UserDtos`. |
| `Services/AuthService.cs` | Login (com as mensagens pt-BR exatas), recuperação de senha. |
| `Services/UserService.cs` | CRUD de usuários (+ validações de nome/e-mail únicos). |
| `Services/ProgramService.cs` | Listar (busca/filtro/ordenação/paginação), criar/editar/apagar programas, favoritar. |
| `Services/SettingsService.cs` | Ler/gravar Configurações, gerar o "diff" de auditoria, mandar pra placa. |
| `Services/CalibrationService.cs` | Ler/gravar calibração e o **ajuste (fit)** do assistente de saída. |
| `Services/ReportService.cs` | Relatórios (Execuções/Erros/Alterações/Log) — só leitura, paginado. |
| `Services/DiagnosticsService.cs` | Estatísticas/rankings, leitura dos sensores, autotestes, ping de rede. |
| `Services/MaintenanceService.cs` | Tamanho **real** do banco (`pg_database_size`), **limpeza** por categoria e **reset de fábrica**. |
| `Services/DeviceService.cs` | Monta a tela de Informação (disco ao vivo + dados das placas). |
| `Services/SystemService.cs` | Embrulha o `ISystemController` (métricas/rede/Wi-Fi/relógio/atualização/energia) + o tamanho real do banco. É o que o `/api/system/*` usa. |
| `Services/NotificationService.cs` | O **feed de notificações**: listar, contador de não-lidas, marcar lida/todas, e `RaiseAsync` (usado pela execução e pelo monitor de sistema). |
| `Services/AuditService.cs` | Grava o histórico de alterações e incrementa os contadores por usuário. |
| `Common/ProfileBuilder.cs` | Porta fiel do `toProfile`/`tempAt` do frontend (transforma segmentos na curva e interpola na execução). |
| `Common/Defaults.cs` | **Fonte única** dos dados de seed (50 programas, 8 falhas, 11 notificações, configs padrão). |
| `Common/Validation.cs` | Validações de nome/e-mail (espelham as regras do frontend). |
| `Common/PagedResult.cs` | Resultado paginado genérico (`{ items, total, page, pageSize }`). |
| `Common/AppException.cs` | Erros de negócio com status HTTP (404/409/400…) que o middleware traduz. |
| `DependencyInjection.cs` | `AddApplication()` — registra todos os serviços acima no contêiner de DI. |

### 🟧 `src/ReflowOven.Infrastructure` — a implementação concreta

| Arquivo | O que faz / por quê |
| --- | --- |
| `Persistence/ReflowDbContext.cs` | O **DbContext** do EF: define todas as tabelas e como mapear cada entidade (jsonb para curvas, enums como texto, check constraints dos singletons, filtro de soft-delete). |
| `Persistence/ReflowDbContextFactory.cs` | Deixa o `dotnet ef` criar migrations **sem subir a aplicação nem o banco**. |
| `Persistence/DbSeeder.cs` | Popula os dados iniciais no primeiro start (idempotente). |
| `Persistence/Conversions/EnumWire.cs` + `PtBrEnumConverter.cs` | Convertem enums ↔ o texto pt-BR exato, no banco e no JSON, lendo o mesmo atributo. |
| `Persistence/Migrations/*` | A migration `Initial` (gerada) e o "snapshot" do modelo. **Não edite à mão**; gere com `dotnet ef`. |
| `Hardware/SimulatedPowerBoard.cs` | A **placa simulada** (padrão): interpola o perfil e gera leituras plausíveis, sem hardware. |
| `Hardware/Rs422PowerBoard.cs` | A placa **real** (esqueleto): aqui entraria o protocolo RS422 do STM32. |
| `Hardware/HardwareOptions.cs` | Escolhe Simulated/Rs422 via `appsettings` (`Hardware:Mode`). |
| `Platform/SimulatedSystemController.cs` | O **SO simulado** (padrão): dados plausíveis e mutações no-op, pra rodar a API num PC sem o OrangePi. |
| `Platform/LinuxSystemController.cs` | O **SO real**: chama `nmcli`/`timedatectl`/`systemctl` e lê `/proc`+`/sys`. Escolhido com `System:Mode=Linux`. Comandos que alteram o sistema exigem **privilégios** (sudoers/polkit). |
| `Platform/ProcessRunner.cs` + `SystemOptions.cs` | Roda comandos do SO (sem shell, captura stdout/stderr) e a config da seção `System`. |
| `BackgroundServices/SystemMonitorService.cs` | Em segundo plano: vigia o **servidor central** e a **atualização (OTA)**; quando muda, gera uma notificação no feed. Inofensivo no modo simulado. |
| `Auth/BcryptPasswordHasher.cs` | Faz o hash/verificação de senha com BCrypt. |
| `Auth/JwtTokenService.cs` + `JwtOptions.cs` | Gera o token JWT (claims: id, nome, papel, calibração). |
| `Auth/TechnicianCredentials.cs` | Valida o login técnico oculto contra a config. |
| `Run/RunManager.cs` | **O cérebro da execução**: guarda a execução ativa, lê a placa a cada 1s, calcula o setpoint, empurra a telemetria e, ao terminar, salva o `ExecutionReport`. |
| `BackgroundServices/RunControlLoopService.cs` | Um serviço que roda em segundo plano e "tica" o `RunManager` a 1 Hz (e publica leituras quando ocioso). |
| `Email/StubEmailSender.cs` | "Envia" o e-mail de reset escrevendo no log (troque por SMTP real). |
| `Time/SystemClock.cs` | Implementação real de `IClock`. |
| `DependencyInjection.cs` | `AddInfrastructure()` — registra banco, JWT, placa, run manager e o loop. |

### 🟥 `src/ReflowOven.Api` — a casca web

| Arquivo | O que faz / por quê |
| --- | --- |
| `Program.cs` | O **ponto de entrada**: liga tudo (banco, DI, JWT+policies, SignalR, CORS, **Serilog**, **Scalar/OpenAPI**), **aplica a migration + seed** no start e mapeia controllers/hubs. |
| `Controllers/*.cs` | Um controller por área: `Auth`, `Users`, `Programs`, `Runs`, `Report*` (Execuções/Erros/Alterações/Log/FaultTypes), `Settings`, `Calibration`, `Diagnostics`, `Maintenance`, `Device`, `System` (`/api/system/*` — leituras autenticadas, **mutações `AdminOnly`**: rede/Wi-Fi/relógio/NTP/atualização/reboot/shutdown) e `Notifications` (`/api/notifications/*` — feed do sininho). Cada método é um endpoint HTTP. |
| `Realtime/RunTelemetryHub.cs` | Hub SignalR `/hubs/telemetry`: o cliente entra no grupo de uma execução e recebe os pontos ao vivo. |
| `Realtime/DiagnosticsHub.cs` | Hub `/hubs/diagnostics`: leituras de sensor a 1 Hz na tela de Diagnóstico. |
| `Realtime/SignalRTelemetrySink.cs` | Implementa `ITelemetrySink` enviando os dados pelos hubs. |
| `Auth/CurrentUser.cs` | Lê quem está logado a partir dos claims do JWT. |
| `Auth/AuthPolicies.cs` | Nomes das políticas (`AdminOnly`, `CalibrationOnly`). |
| `Middleware/ExceptionMiddleware.cs` | Captura erros e responde em formato padrão (ProblemDetails) com o status certo. |
| `GlobalUsings.cs` | `using` globais da API. |

### 🧪 `tests/ReflowOven.Tests`

| Arquivo | O que valida |
| --- | --- |
| `ProfileBuilderTests.cs` | A matemática do perfil (Linear/Fixo/Parábola, interpolação). |
| `PasswordHasherTests.cs` | Hash/verificação de senha. |
| `DefaultsTests.cs` | Os dados de seed (50 programas, 8 falhas, 11 notificações). |
| `SimulatedPowerBoardTests.cs` | O simulador (leituras dentro dos limites; segue o perfil durante a execução). |

---

## 5. Fluxos de ponta a ponta

- **Login:** `POST /api/auth/login` → `AuthService` confere a senha (BCrypt) → `JwtTokenService` gera o
  token → o frontend guarda e manda no header `Authorization: Bearer <token>` nas próximas chamadas.
- **Listar programas:** `GET /api/programs` → `ProgramService` consulta o banco (via `IAppDbContext`),
  aplica filtro/ordenação/paginação e devolve `ProgramDto[]` em JSON.
- **Executar uma queima:** `POST /api/runs/start` → `RunManager` carrega o perfil + limites, manda a placa
  iniciar e cria a execução ativa. O `RunControlLoopService` "tica" a cada 1s: lê a placa, calcula o
  `alvo`, empurra um `TraceSample` pelo `RunTelemetryHub` (o frontend desenha o gráfico ao vivo). Ao
  terminar, salva o `ExecutionReport` e emite `RunCompleted`.
- **Start da aplicação:** `Program.cs` chama `Database.MigrateAsync()` (cria/atualiza tabelas) e
  `DbSeeder.SeedAsync()` (popula dados) — foi exatamente isso que apareceu no seu log.

---

## 6. Como debugar no VSCode

### Pré-requisitos (uma vez só)

1. **Extensão**: instale o **C# Dev Kit** (a Microsoft). Ao abrir a pasta, o VSCode vai sugerir
   (configurado em `.vscode/extensions.json`).
2. **Deixe o `dotnet` no PATH permanentemente** (o SDK está em `~/.dotnet`, fora do PATH global). Edite
   o `~/.bashrc` e adicione no final:
   ```bash
   export DOTNET_ROOT="$HOME/.dotnet"
   export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
   ```
   Depois feche e reabra o terminal (e o VSCode, abrindo-o pelo terminal: `code .`). Sem isso, o VSCode
   pode não achar o `dotnet`. (Os arquivos `.vscode/*.json` já usam o caminho absoluto de `~/.dotnet`
   como reforço.)
3. **Suba o banco** antes (Postgres local rodando, ou `docker compose up -d`).

### Debugar

1. Abra a pasta do projeto no VSCode (`code .` a partir do terminal).
2. Vá na aba **Run and Debug** (ícone de "play" com inseto), escolha **"Debug API (.NET 10)"** e tecle **F5**.
   - Isso roda a tarefa `build`, sobe a API com o debugger anexado e abre o navegador na URL que aparecer.
3. Coloque um **breakpoint** clicando na margem esquerda de uma linha (ex.: dentro de
   `AuthController.Login` ou `ProgramService.ListAsync`). Faça a chamada pelo **Scalar**
   (`http://localhost:5248/scalar`) e o VSCode vai **parar** na linha — você inspeciona variáveis,
   usa F10 (passo a passo) / F11 (entra no método) / F5 (continua).
4. Para testar endpoints protegidos no Scalar: faça `POST /api/auth/login` (usuário `lucas.silva`,
   senha `reflow1234`), copie o `token`, abra o painel **Authentication** (Bearer) e cole. Agora as chamadas vão autenticadas.
5. Para depurar os **testes**: abra um arquivo de teste e use os ícones "Run/Debug Test" acima de cada
   `[Fact]` (com o C# Dev Kit), ou rode a tarefa **test**.

> **O debugger "parou" numa exceção de validação (ex.: `ValidationAppException`, `ConflictException`)?**
> Isso é **normal** e **não é um bug**: são exceções de controle de fluxo, capturadas pelo
> `ExceptionMiddleware`, que devolve um `400`/`409` limpo (ProblemDetails) ao front e registra um
> `WRN` no console. O VSCode só está te avisando da exceção no momento em que ela é lançada
> ("first-chance"). Para não parar mais nelas: na aba **Run and Debug**, seção **BREAKPOINTS**,
> **desmarque** "All Exceptions" / "User-Unhandled Exceptions". Aperte F5 que segue normal.

> Dica: a configuração **"Attach to process (.NET)"** serve para anexar o debugger a uma API que já
> está rodando (escolha o processo `ReflowOven.Api`).

---

## 7. Comandos do dia a dia

```bash
# (deixe isto no ~/.bashrc; aqui para referência)
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

dotnet build ReflowOven.slnx                      # compilar tudo
dotnet run --project src/ReflowOven.Api            # rodar a API (migra + seed)
dotnet test                                        # rodar os testes

# Banco / migrations
dotnet ef migrations add <Nome> -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api -o Persistence/Migrations
dotnet ef database update      -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api
dotnet ef migrations remove    -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api   # desfaz a última (não aplicada)
```

---

## 8. Glossário

- **Endpoint** — uma URL+método (ex.: `GET /api/programs`) que a API atende.
- **Controller** — classe que agrupa endpoints relacionados.
- **Service** — classe com a lógica de negócio, chamada pelos controllers.
- **Entidade** — classe que vira uma **tabela** no banco.
- **DTO** — classe que vira **JSON** na resposta/requisição (o contrato).
- **Migration** — script versionado que cria/altera tabelas.
- **Seed** — dados iniciais inseridos automaticamente.
- **DI / contêiner** — o mecanismo que cria e entrega os serviços (registrado em `AddApplication`/`AddInfrastructure`).
- **Claim** — um dado dentro do JWT (id, nome, papel).
- **Policy** — regra de autorização (ex.: `AdminOnly`).
- **Hub** — o "controller" do SignalR (tempo real).
- **jsonb** — tipo do PostgreSQL que guarda JSON; usamos para curvas/snapshots (lidos por inteiro).
- **Singleton (no banco)** — tabela com uma única linha (`Settings`, `Calibration`, `DeviceInfo`).

---

## 9. Solução de problemas

Estes são **exatamente** os tropeços que tivemos ao subir o projeto pela primeira vez — guarde para a próxima.

### `dotnet: command not found` (ou o VSCode não acha o .NET)
O SDK foi instalado em `~/.dotnet`, que **não está no PATH** por padrão. Adicione ao final do `~/.bashrc`:
```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
```
Feche e reabra o terminal e confirme com `dotnet --version` (deve mostrar `10.0.x`). Abra o VSCode pelo
terminal (`code .`) para herdar esse PATH.

### `docker compose up -d` → `permission denied ... /var/run/docker.sock`
Duas coisas acontecem aqui:
- Seu usuário **não está no grupo `docker`** → `sudo usermod -aG docker $USER && newgrp docker` (ou reinicie a sessão).
- Mesmo resolvendo isso, **se você já tem um PostgreSQL local na porta 5432**, o container não consegue
  usar a mesma porta. Então: ou pare o local antes (`sudo systemctl stop postgresql`) e use o Docker,
  **ou** simplesmente use o Postgres local (próximo item) — foi o caminho que seguimos.

### `28P01: password authentication failed for user "reflow"`
Esse erro significa que **existe um PostgreSQL rodando**, mas o usuário `reflow` (ou o banco `reflowoven`)
ainda não foi criado. Crie-os usando o superusuário do Postgres:
```bash
sudo -u postgres psql <<'SQL'
CREATE ROLE reflow WITH LOGIN PASSWORD 'reflow';
CREATE DATABASE reflowoven OWNER reflow;
GRANT ALL PRIVILEGES ON DATABASE reflowoven TO reflow;
SQL
```
Se aparecer _"role already exists"_, troque a 1ª linha por `ALTER ROLE reflow WITH LOGIN PASSWORD 'reflow';`.
Rode `dotnet run` de novo — a aplicação cria as tabelas e popula o seed sozinha. Usuário/senha/banco
devem bater com a _connection string_ em `appsettings.json`.

### Logs que ASSUSTAM mas são NORMAIS no primeiro start
- `fail: ... Failed executing DbCommand ... SELECT ... FROM "__EFMigrationsHistory"` — o EF tenta ler a
  tabela que controla as migrations; num banco vazio ela ainda não existe, então ele **cria e segue**. Esperado.
- `warn: ... No XML encryptor configured. Key ... may be persisted ... unencrypted` — aviso do
  ASP.NET DataProtection; **benigno em desenvolvimento**.

### `Connection refused` / `No connection could be made`
Não há banco rodando na 5432. Suba o Postgres local ou `docker compose up -d` **antes** do `dotnet run`.

### Aviso do EF sobre `FavoriteProgram` / `ReflowProgram` (já corrigido)
Numa primeira versão o EF avisava sobre o filtro global de soft-delete dos programas e o relacionamento
de favoritos. Já corrigimos adicionando um filtro de consulta correspondente em `FavoriteProgram` no
`ReflowDbContext`. Se você ainda vir esse aviso, atualize o código (`git pull`).

---

Dúvida em algum arquivo específico? Abra ele e procure o comentário `///` no topo da classe — quase tudo
tem uma frase explicando o porquê. Veja também o [`CLAUDE.md`](../CLAUDE.md) (mais técnico) e o
[`README.md`](../README.md).
