using System;
using System.IO;
using System.Linq;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Writers;

using Xunit;

namespace Kiota.Builder.Tests.Writers.CSharp;

public sealed class RequiredNonNullableWriterTests : IDisposable
{
    private const string DefaultPath = "./";
    private const string DefaultName = "name";
    private const string Fallback = "?? throw new InvalidOperationException(\"Could not deserialize the value of the required property";
    private readonly StringWriter tw;
    private readonly LanguageWriter writer;
    private readonly CodeNamespace root;
    private readonly CodeClass model;
    private readonly CodeEnum statusEnum;
    private readonly CodeClass otherClass;
    public RequiredNonNullableWriterTests()
    {
        writer = LanguageWriter.GetLanguageWriter(GenerationLanguage.CSharp, DefaultPath, DefaultName);
        tw = new StringWriter();
        writer.SetTextWriter(tw);
        root = CodeNamespace.InitRootNamespace().AddNamespace("ns");
        statusEnum = root.AddEnum(new CodeEnum { Name = "Status" }).First();
        statusEnum.AddOption(new CodeEnumOption { Name = "Active" });
        otherClass = root.AddClass(new CodeClass { Name = "Other", Kind = CodeClassKind.Model }).First();
        model = root.AddClass(new CodeClass { Name = "model", Kind = CodeClassKind.Model }).First();
        AddProperty("reqString", new CodeType { Name = "string", IsNullable = false }, true);
        AddProperty("reqInt", new CodeType { Name = "integer", IsNullable = false }, true);
        AddProperty("reqEnum", new CodeType { Name = "Status", TypeDefinition = statusEnum, IsNullable = false }, true);
        AddProperty("reqIntArray", new CodeType { Name = "integer", CollectionKind = CodeTypeBase.CodeTypeCollectionKind.Complex }, true);
        AddProperty("reqObject", new CodeType { Name = "Other", TypeDefinition = otherClass, IsNullable = false }, true);
        AddProperty("optString", new CodeType { Name = "string" }, false);
        AddProperty("optInt", new CodeType { Name = "integer" }, false);
    }
    public void Dispose()
    {
        tw?.Dispose();
        GC.SuppressFinalize(this);
    }
    private void AddProperty(string name, CodeType type, bool isNonNullable)
    {
        model.AddProperty(new CodeProperty
        {
            Name = name,
            Kind = CodePropertyKind.Custom,
            Type = type,
            IsRequired = isNonNullable,
            IsNonNullable = isNonNullable,
        });
    }
    private void AddBackingStore()
    {
        model.AddProperty(new CodeProperty
        {
            Name = "backingStore",
            Kind = CodePropertyKind.BackingStore,
            Type = new CodeType { Name = "IBackingStore", IsExternal = true, IsNullable = false },
        });
    }
    private string WriteProperty(string name)
    {
        writer.Write(model.FindChildByName<CodeProperty>(name, false)!);
        return tw.ToString();
    }
    private string WriteMethod(CodeMethodKind kind)
    {
        var method = model.AddMethod(new CodeMethod
        {
            Name = kind == CodeMethodKind.Serializer ? "Serialize" : "GetFieldDeserializers",
            Kind = kind,
            IsAsync = false,
            ReturnType = new CodeType { Name = kind == CodeMethodKind.Serializer ? "void" : "IDictionary<string, Action<IParseNode>>" },
        }).First();
        writer.Write(method);
        return tw.ToString();
    }
    private static string LineOf(string result, string marker) =>
        result.Split('\n').Single(x => x.Contains(marker, StringComparison.Ordinal));

    [Fact]
    public void WritesNonNullableReferenceTypeWithDefaultInitializer()
    {
        var result = WriteProperty("reqString");
        Assert.Contains("#nullable enable", result);
        Assert.Contains("public string ReqString { get; set; } = default!;", result);
        Assert.Contains("#else", result);
        Assert.Contains("public string ReqString { get; set; }" + Environment.NewLine, result);
        Assert.DoesNotContain("string?", result);
    }
    [Fact]
    public void WritesNonNullableValueTypeWithoutNullableMarker()
    {
        var result = WriteProperty("reqInt");
        Assert.Contains("public int ReqInt { get; set; }", result);
        Assert.DoesNotContain("int?", result);
        Assert.DoesNotContain("#nullable", result);
        Assert.DoesNotContain("default!", result);
    }
    [Fact]
    public void WritesNonNullableEnumWithoutNullableMarker()
    {
        var result = WriteProperty("reqEnum");
        Assert.Contains("Status ReqEnum { get; set; }", result);
        Assert.DoesNotContain("Status?", result);
        Assert.DoesNotContain("#nullable", result);
    }
    [Fact]
    public void WritesNonNullableCollectionWithNullableElements()
    {
        var result = WriteProperty("reqIntArray");
        Assert.Contains("public List<int?> ReqIntArray { get; set; } = default!;", result);
        Assert.DoesNotContain("List<int?>?", result);
    }
    [Fact]
    public void KeepsOptionalPropertiesNullable()
    {
        var result = WriteProperty("optString");
        Assert.Contains("public string? OptString { get; set; }", result);
        Assert.DoesNotContain("default!", result);
    }
    [Fact]
    public void WritesNonNullableBackingStoreGetters()
    {
        AddBackingStore();
        var result = WriteProperty("reqString") + WriteProperty("reqInt");
        Assert.Contains("get { return BackingStore?.Get<string>(\"reqString\")!; }", result);
        Assert.Contains("get { return BackingStore?.Get<string>(\"reqString\"); }", result);
        Assert.Contains("get { return BackingStore?.Get<int>(\"reqInt\") ?? default; }", result);
        Assert.DoesNotContain("default!", result);
    }
    [Fact]
    public void WritesDeserializerFallbacksForNonNullableProperties()
    {
        var result = WriteMethod(CodeMethodKind.Deserializer);
        Assert.Contains("ReqString = n.GetStringValue() ?? throw new InvalidOperationException(\"Could not deserialize the value of the required property 'reqString' of 'Model'.\");", result);
        Assert.Contains("ReqInt = n.GetIntValue() " + Fallback, result);
        Assert.Contains("ReqEnum = n.GetEnumValue<global::ns.Status>() " + Fallback, result);
        Assert.Contains("ReqIntArray = n.GetCollectionOfPrimitiveValues<int?>()?.AsList() " + Fallback, result);
        Assert.DoesNotContain("throw", LineOf(result, "ReqObject ="));
        Assert.Contains("OptString = n.GetStringValue(); }", result);
        Assert.Contains("OptInt = n.GetIntValue(); }", result);
    }
    [Fact]
    public void WritesSerializerFromPropertiesWithoutBackingStore()
    {
        var result = WriteMethod(CodeMethodKind.Serializer);
        Assert.Contains("writer.WriteIntValue(\"reqInt\", ReqInt);", result);
        Assert.Contains("writer.WriteEnumValue<global::ns.Status>(\"reqEnum\", ReqEnum);", result);
        Assert.Contains("writer.WriteStringValue(\"reqString\", ReqString);", result);
    }
    [Fact]
    public void WritesSerializerFromBackingStoreForNonNullableValueTypes()
    {
        AddBackingStore();
        var result = WriteMethod(CodeMethodKind.Serializer);
        Assert.Contains("writer.WriteIntValue(\"reqInt\", BackingStore?.Get<int?>(\"reqInt\"));", result);
        Assert.Contains("writer.WriteEnumValue<global::ns.Status>(\"reqEnum\", BackingStore?.Get<global::ns.Status?>(\"reqEnum\"));", result);
        Assert.Contains("writer.WriteStringValue(\"reqString\", ReqString);", result);
        Assert.Contains("writer.WriteIntValue(\"optInt\", OptInt);", result);
    }
}
