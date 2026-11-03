using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Configuration;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace Kiota.Builder.Tests;

public sealed partial class KiotaBuilderTests
{
    private const string RequiredNonNullable30Document = """
openapi: 3.0.4
info:
  title: Required non nullable
  version: 1.0.0
servers:
  - url: https://example.com
paths:
  /models:
    get:
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Model'
  /derived:
    get:
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Derived'
components:
  schemas:
    Status:
      type: string
      enum: [active, inactive]
    Other:
      type: object
      properties:
        id:
          type: string
    Model:
      type: object
      required: [reqString, reqNullableString, reqInt, reqNullableInt, reqEnum, reqObject, reqIntArray, reqStringArray, reqObjectArray, reqWrappedRef, reqNullableAllOfRef, reqNullableOneOfRef, reqWriteOnly, reqReadOnly]
      properties:
        optString:
          type: string
        optInt:
          type: integer
          format: int32
        reqString:
          type: string
        reqNullableString:
          type: string
          nullable: true
        reqInt:
          type: integer
          format: int32
        reqNullableInt:
          type: integer
          format: int32
          nullable: true
        reqEnum:
          $ref: '#/components/schemas/Status'
        reqObject:
          $ref: '#/components/schemas/Other'
        reqIntArray:
          type: array
          items:
            type: integer
            format: int32
        reqStringArray:
          type: array
          items:
            type: string
        reqObjectArray:
          type: array
          items:
            $ref: '#/components/schemas/Other'
        reqWrappedRef:
          description: a described reference, which can't be told apart from a nullable one
          allOf:
            - $ref: '#/components/schemas/Other'
        reqNullableAllOfRef:
          nullable: true
          allOf:
            - $ref: '#/components/schemas/Other'
        reqNullableOneOfRef:
          nullable: true
          oneOf:
            - $ref: '#/components/schemas/Other'
        reqWriteOnly:
          type: string
          writeOnly: true
        reqReadOnly:
          type: string
          readOnly: true
    Derived:
      allOf:
        - $ref: '#/components/schemas/Model'
        - type: object
          required: [derivedReq]
          properties:
            derivedReq:
              type: string
            derivedOpt:
              type: string
""";
    private const string RequiredNonNullable31Document = """
openapi: 3.1.1
info:
  title: Required non nullable
  version: 1.0.0
servers:
  - url: https://example.com
paths:
  /models:
    get:
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Model'
components:
  schemas:
    Other:
      type: object
      properties:
        id:
          type: string
    Model:
      type: object
      required: [reqString, reqNullableString, reqNullableInt, reqNullableAnyOfRef]
      properties:
        reqString:
          type: string
        reqNullableString:
          type: [string, 'null']
        reqNullableInt:
          type: [integer, 'null']
          format: int32
        reqNullableAnyOfRef:
          anyOf:
            - $ref: '#/components/schemas/Other'
            - type: 'null'
""";
    private async Task<CodeNamespace> GetRequiredNonNullableCodeModelAsync(string document, GenerationLanguage language)
    {
        var tempFilePath = Path.GetTempFileName();
        _tempFiles.Add(tempFilePath);
        await File.WriteAllTextAsync(tempFilePath, document, TestContext.Current.CancellationToken);
        var mockLogger = new Mock<ILogger<KiotaBuilder>>();
        var builder = new KiotaBuilder(mockLogger.Object, new GenerationConfiguration { ClientClassName = "ApiClient", OpenAPIFilePath = tempFilePath, Language = language }, _httpClient);
        await using var fs = new FileStream(tempFilePath, FileMode.Open);
        var openApiDocument = await builder.CreateOpenApiDocumentAsync(fs, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(openApiDocument);
        var node = builder.CreateUriSpace(openApiDocument);
        return builder.CreateSourceModel(node);
    }
    private static void AssertRequiredness(CodeClass model, string propertyName, bool isRequired, bool isNonNullable)
    {
        var property = model.FindChildByName<CodeProperty>(propertyName, false);
        Assert.NotNull(property);
        Assert.Equal(isRequired, property.IsRequired);
        Assert.Equal(isNonNullable, property.IsNonNullable);
        if (property.Type.CollectionKind == CodeTypeBase.CodeTypeCollectionKind.None)
            Assert.Equal(!isNonNullable, property.Type.IsNullable);
        else
            Assert.True(property.Type.IsNullable); // drives the nullability of the elements
    }
    [InlineData("optString", false, false)]
    [InlineData("optInt", false, false)]
    [InlineData("reqString", true, true)]
    [InlineData("reqNullableString", true, false)]
    [InlineData("reqInt", true, true)]
    [InlineData("reqNullableInt", true, false)]
    [InlineData("reqEnum", true, true)]
    [InlineData("reqObject", true, true)]
    [InlineData("reqIntArray", true, true)]
    [InlineData("reqStringArray", true, true)]
    [InlineData("reqObjectArray", true, true)]
    [InlineData("reqWrappedRef", true, false)]
    [InlineData("reqNullableAllOfRef", true, false)]
    [InlineData("reqNullableOneOfRef", true, false)]
    [InlineData("reqWriteOnly", true, false)]
    [InlineData("reqReadOnly", true, true)]
    [Theory]
    public async Task SetsRequiredNonNullablePropertiesForCSharpAsync(string propertyName, bool isRequired, bool isNonNullable)
    {
        var codeModel = await GetRequiredNonNullableCodeModelAsync(RequiredNonNullable30Document, GenerationLanguage.CSharp);
        var model = codeModel.FindChildByName<CodeClass>("Model");
        Assert.NotNull(model);
        AssertRequiredness(model, propertyName, isRequired, isNonNullable);
    }
    [InlineData("derivedReq", true, true)]
    [InlineData("derivedOpt", false, false)]
    [Theory]
    public async Task KeepsRequiredPropertiesOfMergedAllOfEntriesAsync(string propertyName, bool isRequired, bool isNonNullable)
    {
        var codeModel = await GetRequiredNonNullableCodeModelAsync(RequiredNonNullable30Document, GenerationLanguage.CSharp);
        var model = codeModel.FindChildByName<CodeClass>("Derived");
        Assert.NotNull(model);
        AssertRequiredness(model, propertyName, isRequired, isNonNullable);
    }
    [InlineData("reqString", true, true)]
    [InlineData("reqNullableString", true, false)]
    [InlineData("reqNullableInt", true, false)]
    [InlineData("reqNullableAnyOfRef", true, false)]
    [Theory]
    public async Task SetsRequiredNonNullablePropertiesForCSharpOpenApi31Async(string propertyName, bool isRequired, bool isNonNullable)
    {
        var codeModel = await GetRequiredNonNullableCodeModelAsync(RequiredNonNullable31Document, GenerationLanguage.CSharp);
        var model = codeModel.FindChildByName<CodeClass>("Model");
        Assert.NotNull(model);
        AssertRequiredness(model, propertyName, isRequired, isNonNullable);
    }
    [InlineData(GenerationLanguage.Java)]
    [InlineData(GenerationLanguage.TypeScript)]
    [InlineData(GenerationLanguage.Python)]
    [InlineData(GenerationLanguage.Go)]
    [Theory]
    public async Task DoesNotSetNonNullablePropertiesForOtherLanguagesAsync(GenerationLanguage language)
    {
        var codeModel = await GetRequiredNonNullableCodeModelAsync(RequiredNonNullable30Document, language);
        var model = codeModel.FindChildByName<CodeClass>("Model");
        Assert.NotNull(model);
        foreach (var property in model.Properties.Where(static x => x.IsOfKind(CodePropertyKind.Custom)))
        {
            Assert.False(property.IsNonNullable);
            Assert.True(property.Type.IsNullable);
        }
    }
}
