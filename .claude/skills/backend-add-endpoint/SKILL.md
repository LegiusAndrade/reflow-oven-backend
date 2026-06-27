---
name: backend-add-endpoint
description: Procedimento para adicionar um endpoint REST no reflow-oven-backend seguindo a Clean Architecture — DTO em Application, validação e lógica no service, controller fino, autorização e contrato (enums pt-BR / ISO 8601). Use ao criar uma nova rota da API do reflow-oven-backend.
---

# Adicionar um endpoint REST no reflow-oven-backend

Clean Architecture: a dependência aponta pra dentro (Api → Infrastructure → Application → Domain). **Controller não toca EF.**

## Passos
1. **DTO (`Application/Dtos`).** Defina request e response como DTOs tipados (nunca exponha entity). Timestamps como ISO 8601 (ou null). Enum no payload → use o tipo com `[JsonStringEnumMemberName]` (literal pt-BR — contrato com a UI).
2. **Service (`Application`).** Adicione o método no service apropriado (Auth/User/Program/Settings/...). **Valide** entrada (tamanhos/ranges/obrigatórios) contra `DomainConstants` **antes** de persistir. Acesso a dados via `IAppDbContext`; `async`/`await`. Lance `AppException` para erro de domínio (o middleware traduz em 400/409).
3. **Controller (`Api`).** Endpoint **fino** que só chama o service e devolve o DTO. Autorização: leitura = autenticado (default); escrita = `[Authorize(Policy="AdminOnly")]`; calibração = `CalibrationOnly`. Siga REST e nomes consistentes.
4. **Audit/log (se mutação).** Mutações de program/config passam por `AuditService` (`ChangeLogEntry` + `OperationLog`). Logue no seam do service.
5. **Contrato com o frontend.** Se a UI vai consumir, alinhe com `../reflow-oven-front` (`api.ts` + schema Zod). Não invente forma divergente sem motivo.
6. **Validar.** `dotnet build ReflowOven.slnx`; `dotnet test`; confira no Scalar (`/scalar`).

## Lembrar
- `DomainConstants` espelha `limits.ts` (lock-step).
- Nunca `object`/`dynamic` no wire; nunca senha/hash em output DTO.
- Token só via `JwtTokenService`. Commits em inglês, sem trailer de autoria; nunca commite sem o usuário pedir.
