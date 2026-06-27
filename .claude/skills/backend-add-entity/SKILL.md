---
name: backend-add-entity
description: Procedimento para adicionar uma entidade EF Core + migration no reflow-oven-backend — mapping em OnModelCreating, DbSet em ReflowDbContext e IAppDbContext, dotnet ef migrations add, jsonb para dados largos e singletons Id=1. Use ao criar/alterar o modelo de persistência do reflow-oven-backend.
---

# Adicionar uma entidade EF Core (+ migration) no reflow-oven-backend

## Passos
1. **Entidade (`Domain`).** Crie a classe em `ReflowOven.Domain` (nullable enabled). Enums com `[JsonStringEnumMemberName]` (literal pt-BR) — persistem como texto via `PtBrEnumConverter`.
2. **`DbSet` (dois lugares!).** Adicione o `DbSet<T>` em **`ReflowDbContext`** (Infrastructure) **e** na interface **`IAppDbContext`** (Application) — os services usam a interface; se faltar, eles não enxergam a entidade.
3. **Mapping (`OnModelCreating`).** Configure inline em `ReflowDbContext.OnModelCreating`. Aplique `PtBrEnumConverter` nas colunas de enum não-JSON. Dados largos / lidos por inteiro (curvas, snapshots) como **`jsonb`** via `OwnsMany(...).ToJson()`. Singletons (Settings/Calibration/DeviceInfo) com check constraint `Id = 1`. Soft-delete (como `Program`): `IsDeleted` + global query filter.
4. **Migration.** `dotnet ef migrations add <Nome> -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api -o Persistence/Migrations` (a design-time factory dispensa host/DB rodando). A migration **aplica no startup** (`Database.MigrateAsync()` + `DbSeeder`) — normalmente você só roda o `migrations add`.
5. **Seed (se precisar).** Dados de fábrica vêm de `Defaults` (uma fonte só — usada por `DbSeeder` e `MaintenanceService.FactoryResetAsync`).
6. **Validar.** `dotnet build`; `dotnet test` (os testes de DbContext usam EF **InMemory**).

## Lembrar
- `DbSet` nos **dois** (`ReflowDbContext` + `IAppDbContext`).
- O modelo emite DDL Postgres-specific (ex. CHECK regex `~`) — por isso os testes usam InMemory, não SQLite.
- `DomainConstants` espelha `limits.ts`. Commits em inglês, sem trailer; branch `develop`; nunca migre/commite sem o usuário pedir.
