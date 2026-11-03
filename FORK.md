# About this fork

This is a personal fork of [microsoft/kiota](https://github.com/microsoft/kiota), used only to generate **C#** clients. It isn't meant to be upstreamed. It's rebased onto upstream stable release tags (currently `v1.38.0`).

## What's different: `required` + non-nullable properties (microsoft/kiota#3911)

Upstream Kiota generates every C# model property as nullable. This fork respects `required` and nullability from the OpenAPI description for **C# model properties**:

| `required` | nullable in the description | Generated |
| :-: | :-: | --- |
| no  | no  | `T?` |
| no  | yes | `T?` |
| yes | yes | `T?` |
| yes | no  | **`T`** |

A property counts as nullable when the description has any of these:
- OAS 3.0 `nullable: true` with a `type`
- OAS 3.1 `type: [..., "null"]`
- an `anyOf`/`oneOf` with a `{ type: "null" }` entry

### Generated code

```csharp
public int Quantity { get; set; }                       // value types and enums: no "?"
public string Id { get; set; } = default!;              // reference types and collections: no "?", initialized to default!
public List<int?> Quantities { get; set; } = default!;  // collections keep their element type
```

Deserializers throw an `InvalidOperationException` when a required non-nullable value is **present but unusable**: an unknown enum member, a malformed GUID or date, or a JSON value of the wrong kind:

```csharp
{ "status", n => { Status = n.GetEnumValue<Status>() ?? throw new InvalidOperationException("Could not deserialize the value of the required property 'status' of 'Order'."); } },
```

### Limits

- **Missing properties and explicit `null` are not detected.** The stock JSON runtime skips `null` values and absent properties before the generated code runs. Such properties keep their default value: `0`, `false`, `Guid.Empty`, the first enum member, or `null` for reference types despite the non-nullable type. The types are accurate as long as the server follows its description. They don't validate responses.
- **Unknown enum members fail the whole response** when the property is required and non-nullable, the same as NSwag. Optional enum properties and enum arrays keep upstream behaviour: an unknown member becomes `null`.
- **Typeless single-entry wrappers around a reference stay nullable.** OpenAPI.NET discards `nullable: true` on schemas without a `type`. So `{ nullable: true, allOf: [ { $ref } ] }` (Swashbuckle) and `{ nullable: true, oneOf: [ { $ref } ] }` (NSwag) can't be told apart from a reference that only adds a `description`. Any typeless wrapper around a single `$ref` is treated as nullable.
- **`writeOnly` properties stay nullable**, because the service never sends them back.
- **Element nullability of collections is unchanged** (`List<int?>`, `List<Status?>`). The stock runtime only reads primitive collections with nullable type arguments.
- **A derived schema that re-lists a base property in `required`** doesn't change the base class declaration.
- **Without the backing store, required value types are always serialized**, including `0`/`false`, so they can't be left out of a PATCH that reuses the schema. **With `--backing-store`**, `Serialize` reads these properties from the store, so only values that were set or changed are written.

Other languages generate exactly the same code as upstream.

## Building and installing

```bash
dotnet pack src/kiota/kiota.csproj -c Release -o ./artifacts -p:VersionSuffix=fork.1
dotnet tool install -g Asimmon.OpenApi.Kiota --add-source ./artifacts --version 1.38.0-fork.1
```

The package is `Asimmon.OpenApi.Kiota` and its command is `kiota-asimmon`, so it installs side by side with the official `kiota` tool. To install a rebuilt package, bump the suffix (`fork.2`, ...) and use `dotnet tool update -g` with the same arguments.

The update check is disabled in `src/kiota/appsettings.json`, as it would otherwise suggest the official upstream release.

## Syncing with upstream

```bash
git fetch upstream --tags
git rebase --onto vX.Y.Z v1.38.0 csharp-required-non-nullable
```

The fork's changes:
- `CodeProperty.IsRequired` / `IsNonNullable`
- `OpenApiSchemaExtensions.IsExplicitlyNullable` and the `Required` merge in the allOf merge helpers
- the C#-only gate in `KiotaBuilder.SetPropertyRequiredness`
- `CSharpRefiner.MakeEnumPropertiesNullable`
- the C# `CodePropertyWriter` / `CodeMethodWriter`
- the package ID, tool command name and update check of `src/kiota`

The tests live in their own files (`KiotaBuilderTests.RequiredNonNullable.cs`, `Writers/CSharp/RequiredNonNullableWriterTests.cs`).
