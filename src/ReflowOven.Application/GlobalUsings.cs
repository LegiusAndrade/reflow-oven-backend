global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Logging;
// The domain's pt-BR LogLevel (INFO/Aviso/Erro) is the wire/DB contract; keep it as `LogLevel`
// over Microsoft.Extensions.Logging.LogLevel, which we only ever use via ILogger<T>.
global using LogLevel = ReflowOven.Domain.Enums.LogLevel;
global using ReflowOven.Domain.Abstractions;
global using ReflowOven.Domain.Common;
global using ReflowOven.Domain.Entities;
global using ReflowOven.Domain.Enums;
global using ReflowOven.Domain.Hardware;
global using ReflowOven.Domain.Platform;
global using ReflowOven.Application.Abstractions;
global using ReflowOven.Application.Common;
global using ReflowOven.Application.Dtos;
