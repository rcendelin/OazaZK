# Oaza API — .NET 10 Azure Functions

Backend portálu Oáza Zadní Kopanina. Clean Architecture ve čtyřech projektech:

| Projekt | Obsah |
|---------|-------|
| `src/Oaza.Domain` | entity, enumy, konstanty (`TableNames`, `PartitionKeys`, `BlobContainerNames`), rozhraní repozitářů |
| `src/Oaza.Application` | use cases, DTO, FluentValidation validátory, mapování, `AppException` |
| `src/Oaza.Infrastructure` | Table/Blob Storage, JWT, Entra ID, e-mail přes ACS, cache importu |
| `src/Oaza.Functions` | HTTP endpointy (`Endpoints/`), timer (`Triggers/`), middleware, DI (`Program.cs`) |

## Spuštění

```bash
cd src/Oaza.Functions
cp local.settings.json.example local.settings.json
func start                       # http://localhost:7071/api
```

Vyžaduje běžící Azurite. Popis všech konfiguračních klíčů je v [../docs/LOKALNI-VYVOJ.md](../docs/LOKALNI-VYVOJ.md).

## Build a testy

```bash
dotnet build Oaza.sln
dotnet test Oaza.sln             # Oaza.Infrastructure.Tests potřebují Azurite, jinak se přeskočí
```

Používej `Oaza.sln`, ne zastaralý `Oaza.slnx`. Verze NuGet balíčků jsou pinované a `global.json` drží SDK na .NET 10.

## Přidání endpointu

1. DTO do `Oaza.Application/DTOs`, validátor do `Oaza.Application/Validators`.
2. Netriviální logiku dej do use case v `Oaza.Application/UseCases` a pokryj ji testem v `tests/Oaza.Application.Tests`.
3. Funkci přidej do odpovídajícího souboru v `Oaza.Functions/Endpoints` s atributem `[RequireRole(...)]`, nebo `[AllowAnonymous]`, pokud má být veřejná. Bez atributu na ni smí kterýkoli přihlášený uživatel.
4. Omezení na vlastní dům (`Member`) musí vynutit endpoint sám.
5. Aktualizuj [../docs/API.md](../docs/API.md) a typy ve `web/src/types/index.ts`.

## Dokumentace

- [Architektura](../docs/ARCHITEKTURA.md)
- [REST API](../docs/API.md)
- [Výpočty vyúčtování](../docs/VYUCTOVANI.md)
