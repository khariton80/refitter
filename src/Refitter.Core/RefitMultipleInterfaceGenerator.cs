using System.Text;

using NSwag;

namespace Refitter.Core;

internal class RefitMultipleInterfaceGenerator : RefitInterfaceGenerator
{
    private const string Separator = "    ";

    private readonly RefitGeneratorSettings settings;
    private readonly OpenApiDocument document;
    private readonly CustomCSharpClientGenerator generator;
    private readonly HashSet<string> knownIdentifiers = new();

    internal RefitMultipleInterfaceGenerator(
        RefitGeneratorSettings settings,
        OpenApiDocument document,
        CustomCSharpClientGenerator generator) 
        : base(settings, document, generator)
    {
        this.settings = settings;
        this.document = document;
        this.generator = generator;
        generator.BaseSettings.OperationNameGenerator = new OperationNameGenerator(document);
    }

    public override string GenerateCode()
    {
        var code = new StringBuilder();
        foreach (var kv in document.Paths)
        {
            foreach (var operations in kv.Value)
            {
                var operation = operations.Value;

                var returnTypeParameter = new[] {"200", "201", "203", "206"}
                    .Where(code => operation.Responses.ContainsKey(code))
                    .Select(code => generator.GetTypeName(operation.Responses[code].ActualResponse.Schema, true, null))
                    .FirstOrDefault();

                var returnType = GetReturnType(returnTypeParameter);

                var verb = operations.Key.CapitalizeFirstCharacter();

                GenerateInterfaceXmlDocComments(operation, code);
                code.AppendLine($$"""
                                  {{GenerateInterfaceDeclaration(GetInterfaceName(kv, verb, operation))}}
                                  {{Separator}}{
                                  """);

                var operationModel = generator.CreateOperationModel(operation);
                var parameters = ParameterExtractor.GetParameters(operationModel, operation, settings);
                var parametersString = string.Join(", ", parameters);

                GenerateMethodXmlDocComments(operation, code);
                GenerateForMultipartFormData(operationModel, code);
                GenerateAcceptHeaders(operations, operation, code);

                code.AppendLine($"{Separator}{Separator}[{verb}(\"{kv.Key}\")]")
                    .AppendLine($"{Separator}{Separator}{returnType} Execute({parametersString});")
                    .AppendLine($"{Separator}}}")
                    .AppendLine();
            }
        }

        return code.ToString();
    }

    private string GetInterfaceName(
        KeyValuePair<string, OpenApiPathItem> kv,
        string verb,
        OpenApiOperation operation)
    {
        var name = IdentifierUtils.Counted(
            knownIdentifiers,
            "I" + generator
                .BaseSettings
                .OperationNameGenerator
                .GetOperationName(document, kv.Key, verb, operation).CapitalizeFirstCharacter(),
            suffix: "Endpoint");
        knownIdentifiers.Add(name);
        return name;
    }

    private string GenerateInterfaceDeclaration(string name)
    {
        var modifier = settings.TypeAccessibility.ToString().ToLowerInvariant();
        return $"""
                {Separator}{GetGeneratedCodeAttribute()}
                {Separator}{modifier} interface {name}
                """;
    }
}