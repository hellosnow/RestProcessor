﻿namespace Microsoft.RestApi.RestTransformer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Microsoft.DocAsCode.Build.RestApi.Swagger;
    using Microsoft.DocAsCode.DataContracts.RestApi;
    using Microsoft.RestApi.RestTransformer.Models;

    using Newtonsoft.Json.Linq;

    public class RestOperationTransformer
    {
        public static OperationEntity Transform(SwaggerModel swaggerModel, RestApiChildItemViewModel viewModel, string productUid)
        {
            var scheme = Utility.GetScheme(swaggerModel.Metadata);
            var hostWithParameters = Utility.GetHostWithParameters(swaggerModel.Host, swaggerModel.Metadata, viewModel.Metadata);
            var host = hostWithParameters.Item1;
            var hostParameters = hostWithParameters.Item2;
            var apiVersion = Utility.GetApiVersion(viewModel, swaggerModel.Info.Version);

            var allDefinitionObjects = GetAllDefinitionObjects(swaggerModel);
            var allDefinitions = GetAllDefinitions(allDefinitionObjects);

            var bodyDefinitionObject = new DefinitionObject();
            var parametersDefinitions = new List<Definition>();
            var allSimpleParameters = TransformAllSimpleParameters(hostParameters, viewModel, ref bodyDefinitionObject, ref parametersDefinitions);

            var responseDefinitionObjects = new List<DefinitionObject>();
            var responses = TransformResponses(viewModel, allDefinitions, ref responseDefinitionObjects);

            var basePath = swaggerModel.BasePath;
            var paths = TransformPaths(viewModel, scheme, host, basePath, apiVersion, allSimpleParameters);
            var serviceId = swaggerModel.Metadata.GetValueFromMetaData<string>("x-internal-service-id");
            var serviceName = swaggerModel.Metadata.GetValueFromMetaData<string>("x-internal-service-name");
            var groupName = swaggerModel.Metadata.GetValueFromMetaData<string>("x-internal-toc-name");
            var operationName = swaggerModel.Metadata.GetValueFromMetaData<string>("x-internal-operation-name");

            return new OperationEntity
            {
                Id = Utility.TrimUId($"{Utility.GetHostWithBasePathUId(swaggerModel.Host, productUid, basePath)}.{serviceId}.{groupName}.{operationName}")?.ToLower(),
                Name = operationName,
                Service = serviceName,
                Summary = Utility.GetSummary(viewModel.Summary, viewModel.Description),
                ApiVersion = apiVersion,
                GroupName = groupName,
                IsDeprecated = swaggerModel.Metadata.GetValueFromMetaData<bool>("deprecated"),
                IsPreview = swaggerModel.Metadata.GetValueFromMetaData<bool>("x-ms-preview"),
                Responses = responses,
                Parameters = Helper.SortParameters(paths, allSimpleParameters.Where(p => p.ParameterEntityType == ParameterEntityType.Query || p.ParameterEntityType == ParameterEntityType.Path).ToList()),
                RequestHeaders = allSimpleParameters.Where(p => p.ParameterEntityType == ParameterEntityType.Header).ToList(),
                RequestBodies = TransformRequestBodies(allDefinitions, allSimpleParameters.Where(p => p.ParameterEntityType == ParameterEntityType.Body).ToList(), bodyDefinitionObject),
                Paths = Helper.HandlePathsDefaultValues(paths, apiVersion, allSimpleParameters.Where(p => p.ParameterEntityType == ParameterEntityType.Query || p.ParameterEntityType == ParameterEntityType.Path).ToList()),
                Produces = viewModel.Metadata.GetArrayFromMetaData<string>("produces"),
                Consumes = viewModel.Metadata.GetArrayFromMetaData<string>("consumes"),
                Examples = TransformExamples(viewModel, paths, allSimpleParameters, bodyDefinitionObject),
                Definitions = TransformDefinitions(allDefinitions, parametersDefinitions, bodyDefinitionObject, responseDefinitionObjects),
                Securities = TransformSecurities(swaggerModel)
            };
        }

        #region Parameters

        private static IList<ParameterEntity> TransformAllSimpleParameters(List<ParameterEntity> hostParameters, RestApiChildItemViewModel viewModel, ref DefinitionObject definitionObject, ref List<Definition> definitions)
        {
            var parameters = hostParameters == null ? new List<ParameterEntity>() : new List<ParameterEntity>(hostParameters);
            if (viewModel?.Parameters != null)
            {
                foreach (var parameter in viewModel.Parameters)
                {
                    var inType = parameter.Metadata.GetValueFromMetaData<string>("in");
                    if (inType != null && Enum.TryParse<ParameterEntityType>(inType, true, out var parameterEntityType))
                    {
                        var isRequired = parameter.Metadata.GetValueFromMetaData<bool>("required");
                        if (parameter.Metadata.TryGetValue("x-ms-required", out var msRequired))
                        {
                            isRequired = (bool)msRequired;
                        }
                        var types = new List<BaseParameterTypeEntity>();
                       
                        if (parameter.Metadata.TryGetValue("type", out var type))
                        {
                            var enumValues = parameter.Metadata.GetArrayFromMetaData<string>("enum");
                            var enumNode = parameter.Metadata.GetDictionaryFromMetaData<Dictionary<string, object>>("x-ms-enum");
                            if (enumValues != null && enumNode != null)
                            {
                                var enumObjects = new List<EnumValue>();
                                foreach (var v in enumValues)
                                {
                                    enumObjects.Add(new EnumValue { Value = v });
                                }

                                var definition = new Definition
                                {
                                    DefinitionObjectType = DefinitionObjectType.Enum,
                                    Description = parameter.Description,
                                    EnumValues = enumObjects
                                };

                                if (enumNode.TryGetValue("name", out var enumName))
                                {
                                    types.Add(new BaseParameterTypeEntity
                                    {
                                        Id = (string)enumName
                                    });
                                    definition.Name = (string)enumName;
                                    definition.Type = (string)enumName;
                                }

                                if (enumNode.TryGetValue("values", out var enumValue))
                                {
                                    var values = (JArray)enumValue;
                                    if (values != null)
                                    {
                                        foreach (var v in values)
                                        {
                                            var keyValueEnum = v.ToObject<Dictionary<string, object>>();
                                            var enumV = keyValueEnum.GetValueFromMetaData<string>("value");
                                            var enumObject = enumObjects.FirstOrDefault(q => q.Value == enumV);
                                            if (enumV != null && enumObject != null)
                                            {
                                                enumObject.Description = keyValueEnum.GetValueFromMetaData<string>("description");
                                                var name = keyValueEnum.GetValueFromMetaData<string>("name");
                                                enumObject.Name = string.IsNullOrEmpty(name) ? enumV : name;
                                            }
                                        }
                                    }
                                }
                                definitions.Add(definition);
                            }
                            else
                            {
                                types.Add(new BaseParameterTypeEntity
                                {
                                    Id = (string)type
                                });
                            }

                            var parameterEntity = new ParameterEntity
                            {
                                Name = parameter.Name,
                                Description = parameter.Description,
                                IsRequired = isRequired,
                                Pattern = parameter.Metadata.GetValueFromMetaData<string>("pattern"),
                                Format = parameter.Metadata.GetValueFromMetaData<string>("format"),
                                In = inType,
                                ParameterEntityType = parameterEntityType,
                                Types = types,
                                EnumValues = enumValues
                            };
                            parameters.Add(parameterEntity);
                        }
                        else if (parameter.Metadata.TryGetValue("schema", out var schema))
                        {
                            definitionObject = ResolveSchema((JObject)schema);
                            definitionObject.Name = parameter.Name;
                        }
                    }
                }
            }
            return parameters;
        }
        
        #endregion

        #region Paths

        private static string FormatPathQueryStrings(string initParameters, IEnumerable<ParameterEntity> queryParameters)
        {
            var queries = new List<string>();
            if (!string.IsNullOrEmpty(initParameters))
            {
                var initStrings = initParameters.Split('&').Select(p =>
                {
                    if (!queryParameters.Any(q => q.Name == p?.Split('=')[0]))
                    {
                        return p;
                    }
                    return null;
                });
                queries.AddRange(initStrings.Where(s => !string.IsNullOrEmpty(s)));
            }
            var queryStrings = queryParameters.Select(p =>
            {
                return $"{p.Name}={{{p.Name}}}";
            });
            queries.AddRange(queryStrings);
            return string.Join("&", queries);
        }

        private static IList<PathEntity> TransformPaths(RestApiChildItemViewModel viewModel, string scheme, string host, string basePath, string apiVersion, IList<ParameterEntity> parameters)
        {
            var pathEntities = new List<PathEntity>();

            var paths = viewModel.Path?.Split('?');
            var requiredQueryStrings = parameters.Where(p => p.IsRequired && p.In == "query");
            var requiredPath = paths[0];

            if (requiredQueryStrings.Any())
            {
                requiredPath = requiredPath + "?" + FormatPathQueryStrings(paths.Count() > 1 ? paths[1] : null, requiredQueryStrings);
            }

            pathEntities.Add(new PathEntity
            {
                Content = $"{viewModel.OperationName.ToUpper()} {Utility.ResolveScheme(scheme)}{Utility.GetHostWithBasePath(host, basePath)}{requiredPath}",
                IsOptional = false
            });

            var allQueryStrings = parameters.Where(p => p.In == "query");
            var optionPath = paths[0];
            if (!allQueryStrings.All(p => p.IsRequired))
            {
                optionPath = optionPath + "?" + FormatPathQueryStrings(paths.Count() > 1 ? paths[1] : null, allQueryStrings);

                pathEntities.Add(new PathEntity
                {
                    Content = $"{viewModel.OperationName.ToUpper()} {Utility.ResolveScheme(scheme)}{Utility.GetHostWithBasePath(host, basePath)}{optionPath}",
                    IsOptional = true
                });
            }
            return pathEntities;
        }

        #endregion

        #region Requestbody

        public static IList<RequestBody> TransformRequestBodies(IList<Definition> allDefinitions, List<ParameterEntity> bodyParameters, DefinitionObject bodyDefinitionObject)
        {
            var bodies = new List<RequestBody>();

            if (!string.IsNullOrEmpty(bodyDefinitionObject?.Type))
            {
                var polymorphicDefinitions = GetPolymorphicDefinitions(allDefinitions, bodyDefinitionObject.Type);
                if (polymorphicDefinitions == null  || polymorphicDefinitions.Count == 0)
                {
                    var selfDefinition = GetSelfDefinition(allDefinitions, bodyDefinitionObject.Type);
                    if (selfDefinition != null)
                    {
                        var parameterEntities = GetDefinitionParameters(allDefinitions, selfDefinition, true);
                        bodyParameters.AddRange(parameterEntities);
                    }
                    else
                    {
                        bodyParameters.Add(new ParameterEntity
                        {
                            Name = bodyDefinitionObject.Name,
                            Description = bodyDefinitionObject.Description,
                            IsRequired = bodyDefinitionObject.IsRequired,
                            IsReadOnly = bodyDefinitionObject.IsReadOnly,
                            In = "body",
                            ParameterEntityType = ParameterEntityType.Body,
                            Pattern = bodyDefinitionObject.Pattern,
                            Format = bodyDefinitionObject.Format,
                            Types = new List<BaseParameterTypeEntity> { new BaseParameterTypeEntity { Id = bodyDefinitionObject.Type } }
                        });
                    }
                    
                    if (bodyParameters.Count > 0)
                    {
                        bodies.Add(new RequestBody
                        {
                            RequestBodyParameters = bodyParameters
                        });
                    }
                }
                else
                {
                    foreach (var polymorphicDefinition in polymorphicDefinitions)
                    {
                        var fullBodyParameters = GetDefinitionParameters(allDefinitions, polymorphicDefinition, true);
                        foreach (var bodyParameter in bodyParameters)
                        {
                            fullBodyParameters.Add(Utility.Clone(bodyParameter));
                        }
                        if (fullBodyParameters.Count > 0)
                        {
                            bodies.Add(new RequestBody
                            {
                                Name = polymorphicDefinition?.Type,
                                Description = polymorphicDefinition?.Description,
                                RequestBodyParameters = fullBodyParameters
                            });
                        }
                    }
                }
            }

            return bodies.Count > 0 ? bodies : null;
        }

        #endregion RequestBody

        #region Responses

        private static IList<ResponseEntity> TransformResponses(RestApiChildItemViewModel child, IList<Definition> definitions, ref List<DefinitionObject> definitionObjects)
        {
            var responses = new List<ResponseEntity>();
            foreach (var response in child.Responses)
            {
                var typesName = new List<BaseParameterTypeEntity>();
                string typesTitle = null;
                var schema = response.Metadata.GetDictionaryFromMetaData<Dictionary<string, object>>("schema");
                if (schema != null)
                {
                    var definitionObject = ResolveSchema(response.Metadata.GetValueFromMetaData<JObject>("schema"));
                    definitionObjects.Add(definitionObject);
                    if (string.IsNullOrEmpty(definitionObject.DiscriminatorKey) && !string.IsNullOrEmpty(definitionObject.Type))
                    {
                        typesName.Add(new BaseParameterTypeEntity
                        {
                            IsArray = definitionObject.DefinitionObjectType == DefinitionObjectType.Array,
                            Id = definitionObject.Type
                        });
                    }
                    else if (!string.IsNullOrEmpty(definitionObject.DiscriminatorKey) && !string.IsNullOrEmpty(definitionObject.Type))
                    {
                        var polymorphicDefinitions = GetPolymorphicDefinitions(definitions, definitionObject.Type);
                        typesTitle = definitionObject.Type;
                        if (polymorphicDefinitions?.Count > 0)
                        {
                            foreach (var polymorphicDefinition in polymorphicDefinitions)
                            {
                                typesName.Add(new BaseParameterTypeEntity
                                {
                                    IsArray = definitionObject.DefinitionObjectType == DefinitionObjectType.Array,
                                    Id = polymorphicDefinition.Type
                                });
                            }
                        }
                    }
                }

                var headerList = new List<ResponseHeader>();
                var headers = response.Metadata.GetDictionaryFromMetaData<Dictionary<string, object>>("headers");
                if (headers != null)
                {
                    foreach(var header in headers)
                    {
                        var headerValue = ((JObject)header.Value).ToObject<Dictionary<string, object>>();
                        headerList.Add(new ResponseHeader
                        {
                            Name = header.Key,
                            Value = headerValue?.GetValueFromMetaData<string>("type")
                        });
                    }
                }

                responses.Add(new ResponseEntity
                {
                    Name = Utility.GetStatusCodeString(response.HttpStatusCode),
                    Description = response.Description,
                    Types = typesName.Count == 0 ? null : typesName,
                    TypesTitle = typesTitle,
                    ResponseHeaders = headerList.Count == 0 ? null : headerList
                });
            }

            return responses;
        }

        #endregion

        #region Example

        private static string GetExampleRequestUri(IList<PathEntity> paths, Dictionary<string, object> msExampleParameters, IList<ParameterEntity> parameters)
        {
            var pathContent = Helper.GetOptionalFullPath(paths);
            if (msExampleParameters != null)
            {
                foreach (var parameter in msExampleParameters)
                {
                    if (pathContent != null && pathContent.Contains($"{{{parameter.Key}}}"))
                    {
                        pathContent = pathContent?.Replace($"{{{parameter.Key}}}", Convert.ToString(parameter.Value));
                    }
                }
            }
            if (parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    if (!parameter.IsRequired && pathContent != null && pathContent.Contains($"{{{parameter.Name}}}"))
                    {
                        pathContent = pathContent?.Replace($"/{{{parameter.Name}}}", string.Empty);
                    }
                }
            }
            if (pathContent.Contains('?'))
            {
                var contents = pathContent.Split('?');
                if (contents[1].Contains('&'))
                {
                    var queries = contents[1].Split('&');
                    contents[1] = string.Join("&", queries.Where(q => !q.Contains("={")));
                }
                pathContent = string.Join("?", contents);
            }

            return pathContent;
        }

        private static IList<ExampleRequestHeaderEntity> GetExampleRequestHeader(Dictionary<string, object> msExampleParameters, IList<ParameterEntity> headerParameters)
        {
            var exampleRequestHeaders = new List<ExampleRequestHeaderEntity>();

            foreach (var headerParameter in headerParameters)
            {
                if (msExampleParameters != null)
                {
                    foreach (var msExampleParameter in msExampleParameters)
                    {
                        if (msExampleParameter.Key == headerParameter.Name)
                        {
                            exampleRequestHeaders.Add(new ExampleRequestHeaderEntity
                            {
                                Name = msExampleParameter.Key,
                                Value = (string)msExampleParameter.Value
                            });
                        }
                    }
                }
            }

            return exampleRequestHeaders.Count > 0 ? exampleRequestHeaders : null;
        }

        private static string GetExampleRequestBody(Dictionary<string, object> msExampleParameters, IList<ParameterEntity> bodyParameters, DefinitionObject bodyDefinitionObject)
        {
            if (msExampleParameters != null)
            {
                foreach (var msExampleParameter in msExampleParameters)
                {
                    if (msExampleParameter.Key == bodyDefinitionObject.Name)
                    {
                        return Utility.FormatJsonString(msExampleParameter.Value);
                    }
                }
            }

            foreach (var bodyParameter in bodyParameters)
            {
                if (msExampleParameters != null)
                {
                    foreach (var msExampleParameter in msExampleParameters)
                    {
                        if (msExampleParameter.Key == bodyParameter.Name || msExampleParameter.Key == "parameters")
                        {
                            return Utility.FormatJsonString(msExampleParameter.Value);
                        }
                    }
                }
            }

            return null;
        }

        private static List<ExampleResponseEntity> GetExampleResponses(Dictionary<string, object> msExampleResponses)
        {
            var exampleResponses = new List<ExampleResponseEntity>();
            if (msExampleResponses != null)
            {
                foreach (var msExampleResponse in msExampleResponses)
                {
                    var msExampleResponseValue = ((JObject)msExampleResponse.Value).ToObject<Dictionary<string, object>>();
                    string body = null;
                    if (msExampleResponseValue.TryGetValue("body", out var msBody) && msBody != null)
                    {
                        body = Utility.FormatJsonString(msBody);
                    }
                    else if (msExampleResponseValue.TryGetValue("value", out var msValue) && msValue != null)
                    {
                        body = Utility.FormatJsonString(msExampleResponseValue);
                    }

                    var headers = new List<ExampleResponseHeaderEntity>();
                    if (msExampleResponseValue.TryGetValue("headers", out var msHeader) && msHeader != null)
                    {
                        var msHeaderDict = ((JObject)msHeader).ToObject<Dictionary<string, string>>();
                        foreach (var header in msHeaderDict)
                        {
                            headers.Add(new ExampleResponseHeaderEntity
                            {
                                Name = header.Key,
                                Value = header.Value
                            });
                        }
                    }

                    var exampleResponse = new ExampleResponseEntity
                    {
                        StatusCode = msExampleResponse.Key,
                        Body = string.IsNullOrEmpty(body) ? null : body,
                        Headers = headers.Count > 0 ? headers : null
                    };
                    exampleResponses.Add(exampleResponse);
                }
            }

            return exampleResponses;
        }

        private static IList<ExampleEntity> TransformExamples(RestApiChildItemViewModel viewModel, IList<PathEntity> paths, IList<ParameterEntity> parameters, DefinitionObject bodyDefinitionObject)
        {
            var examples = new List<ExampleEntity>();
            var msExamples = viewModel.Metadata.GetDictionaryFromMetaData<Dictionary<string, object>>("x-ms-examples");
            if (msExamples != null)
            {
                foreach (var msExample in msExamples)
                {
                    var msExampleValue = ((JObject)msExample.Value).ToObject<Dictionary<string, object>>();
                    var msExampleParameters = msExampleValue.GetDictionaryFromMetaData<Dictionary<string, object>>("parameters");
                    var msExampleResponses = msExampleValue.GetDictionaryFromMetaData<Dictionary<string, object>>("responses");

                    var example = new ExampleEntity
                    {
                        Name = msExample.Key,
                        ExampleRequest = new ExampleRequestEntity
                        {
                            RequestUri = GetExampleRequestUri(paths, msExampleParameters, parameters.Where(p => p.In == "path").ToList()),
                            Headers = GetExampleRequestHeader(msExampleParameters, parameters.Where(p => p.In == "header").ToList()),
                            RequestBody = GetExampleRequestBody(msExampleParameters, parameters.Where(p => p.In == "body").ToList(), bodyDefinitionObject),
                        },
                        ExampleResponses = GetExampleResponses(msExampleResponses)
                    };
                    examples.Add(example);
                }
            }
            return examples;
        }

        #endregion

        #region Definitions

        private static IList<DefinitionObject> GetAllDefinitionObjects(SwaggerModel swaggerModel)
        {
            var allDefinitionObjects = new List<DefinitionObject>();
            if (swaggerModel.Definitions != null)
            {
                var definitions = ((JObject)swaggerModel.Definitions).ToObject<Dictionary<string, JObject>>();
                foreach (var definition in definitions)
                {
                    var definitionObject = ResolveSchema(definition.Value);
                    definitionObject.Name = definition.Key;
                    definitionObject.Type = definition.Key;
                    var flattenDefinitionObjects = FlattenDefinitionObject(definitionObject);
                    foreach (var flattenDefinitionObject in flattenDefinitionObjects)
                    {
                        if (!string.IsNullOrEmpty(flattenDefinitionObject.Type)
                            && string.IsNullOrEmpty(flattenDefinitionObject.DiscriminatorKey)
                            && (flattenDefinitionObject.DefinitionObjectType == DefinitionObjectType.Object
                            || flattenDefinitionObject.DefinitionObjectType == DefinitionObjectType.Enum
                            || (flattenDefinitionObject.DefinitionObjectType == DefinitionObjectType.Array && flattenDefinitionObject.PropertyItems?.Count > 0)
                            || (flattenDefinitionObject.DefinitionObjectType == DefinitionObjectType.Array && flattenDefinitionObject.AllOfs?.Count > 0))
                            && !allDefinitionObjects.Any(d => d.Type == flattenDefinitionObject.Type))
                        {
                            allDefinitionObjects.Add(flattenDefinitionObject);
                        }
                    }
                    var indexOfDefinitionObject = allDefinitionObjects.FindIndex(d => d.Type == definitionObject.Type);
                    if (indexOfDefinitionObject > -1)
                    {
                        allDefinitionObjects[indexOfDefinitionObject] = definitionObject;
                    }
                }
            }

            return allDefinitionObjects;
        }

        private static IList<Definition> GetAllDefinitions(IList<DefinitionObject> definitionObjects)
        {
            var definitions = new List<Definition>();
            foreach (var definitionObject in definitionObjects)
            {
                var definition = new Definition
                {
                    Name = definitionObject.Type,
                    Title = definitionObject.Title,
                    SubTitle = definitionObject.SubTitle,
                    Description = definitionObject.Description,
                    SubDescription = definitionObject.SubDescription,
                    Type = definitionObject.Type,
                    DefinitionObjectType = definitionObject.DefinitionObjectType,
                    DefinitionProperties = GetDefinitionProperties(definitionObject),
                    DiscriminatorValue = definitionObject.DiscriminatorValue,
                    EnumValues = definitionObject.EnumValues,
                    AllOfTypes = definitionObject.AllOfs?.Select(p => p.Type).ToList()
                };
                definitions.Add(definition);
            }

            return definitions;
        }

        private static IList<DefinitionProperty> GetDefinitionProperties(DefinitionObject definitionObject)
        {
            var definitionProperties = new List<DefinitionProperty>();

            foreach (var property in definitionObject.PropertyItems)
            {
                if (!definitionProperties.Any(p => p.Name == property.Name))
                {
                    var definitionProperty = new DefinitionProperty
                    {
                        Name = property.Name,
                        Title = property.Title,
                        SubTitle = property.SubTitle,
                        Description = property.Description,
                        SubDescription = property.SubDescription,
                        Type = property.Type,
                        AdditionalType = property.AdditionalType,
                        IsReadOnly = property.IsReadOnly,
                        IsRequired = property.IsRequired,
                        IsFlatten = property.IsFlatten,
                        DiscriminatorKey = property.DiscriminatorKey,
                        DiscriminatorValue = property.DiscriminatorValue,
                        DefinitionObjectType = property.DefinitionObjectType,
                        Pattern = property.Pattern,
                        Format = property.Format,
                        EnumValues = property.EnumValues
                    };
                    definitionProperties.Add(definitionProperty);
                }
            }

            foreach (var allOf in definitionObject.AllOfs)
            {
                var tmpProperties = GetDefinitionProperties(allOf);
                foreach (var tmpProperty in tmpProperties)
                {
                    if (!definitionProperties.Any(d => d.Name == tmpProperty.Name))
                    {
                        definitionProperties.Add(tmpProperty);
                    }
                }
            }

            return definitionProperties;
        }

        private static Definition GetSelfDefinition(IList<Definition> allDefinitions, string selfType)
        {
            return allDefinitions?.FirstOrDefault(d => d.Type == selfType);
        }

        private static IList<Definition> GetPolymorphicDefinitions(IList<Definition> allDefinitions, string baseType)
        {
            return allDefinitions?.Where(d => d.AllOfTypes != null && d.AllOfTypes.Any(t => t == baseType)).ToList();
        }

        private static IList<ParameterEntity> GetDefinitionParameters(IList<Definition> allDefinitions, Definition definition, bool filterReadOnly = true)
        {
            var parameters = new List<ParameterEntity>();

            foreach (var property in definition.DefinitionProperties)
            {
                if (!filterReadOnly || (filterReadOnly == true && property.IsReadOnly == false))
                {
                    string typesTitle = null;
                    var types = new List<BaseParameterTypeEntity>();

                    var parameterTypeEntity = new BaseParameterTypeEntity
                    {
                        Id = property.Type,
                    };

                    if (property.Name == property.DiscriminatorKey && !string.IsNullOrEmpty(property.DiscriminatorValue))
                    {
                        typesTitle = "string";
                        types.Add(new BaseParameterTypeEntity
                        {
                            Id = property.DiscriminatorValue
                        });
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(property.AdditionalType))
                        {
                            parameterTypeEntity.IsDictionary = true;
                            parameterTypeEntity.Id = "object";
                            parameterTypeEntity.AdditionalTypes = new List<IdentifiableEntity>
                            {
                                new IdentifiableEntity{ Id = "string" },
                                new IdentifiableEntity{ Id = property.AdditionalType }
                            };
                            types.Add(parameterTypeEntity);
                        }
                        else if (property.DefinitionObjectType == DefinitionObjectType.Enum && string.IsNullOrEmpty(property.Type))
                        {
                            typesTitle = "enum";
                            foreach (var enumValue in property.EnumValues)
                            {
                                types.Add(new BaseParameterTypeEntity
                                {
                                    Id = enumValue.Value
                                });
                            }
                        }
                        else if (property.DefinitionObjectType != DefinitionObjectType.Simple)
                        {
                            var polymorphicDefinitions = GetPolymorphicDefinitions(allDefinitions, property.Type);
                            if (polymorphicDefinitions?.Count > 0)
                            {
                                typesTitle = property.Type + (property.DefinitionObjectType == DefinitionObjectType.Array ? "[]" : string.Empty);
                                foreach (var polymorphicDefinition in polymorphicDefinitions)
                                {
                                    types.Add(new BaseParameterTypeEntity
                                    {
                                        Id = polymorphicDefinition.Type,
                                        IsArray = property.DefinitionObjectType == DefinitionObjectType.Array
                                    });
                                }
                            }
                            else
                            {
                                types.Add(new BaseParameterTypeEntity
                                {
                                    Id = property.Type,
                                    IsArray = property.DefinitionObjectType == DefinitionObjectType.Array
                                });
                            }
                        }
                        else
                        {
                            types.Add(parameterTypeEntity);
                        }
                    }

                    parameters.Add(new ParameterEntity
                    {
                        Name = property.Name,
                        Description = Utility.GetDefinitionPropertyDescription(property),
                        IsRequired = property.IsRequired,
                        IsReadOnly = property.IsReadOnly,
                        Types = types,
                        TypesTitle = string.IsNullOrEmpty(typesTitle) ? null : typesTitle,
                        In = "body",
                        ParameterEntityType = ParameterEntityType.Body,
                        Pattern = property.Pattern,
                        Format = property.Format
                    });
                }
            }

            return parameters;
        }

        private static IList<DefinitionObject> FlattenDefinitionObject(DefinitionObject definitionObject)
        {
            var definitionObjects = new List<DefinitionObject>() { definitionObject };
            foreach (var item in definitionObject.PropertyItems)
            {
                if (definitionObjects.Any(d => d.Type == item.Type))
                {
                    continue;
                }
                if (item.DefinitionObjectType != DefinitionObjectType.Simple)
                {
                    definitionObjects.Add(item);

                    var tmpDefinitions = FlattenDefinitionObject(item);
                    foreach (var tmpDefinition in tmpDefinitions)
                    {
                        if (!definitionObjects.Any(d => d.Type == tmpDefinition.Type))
                        {
                            definitionObjects.Add(tmpDefinition);
                        }
                    }
                }
            }
            foreach (var allOf in definitionObject.AllOfs)
            {
                var tmpDefinitions = FlattenDefinitionObject(allOf);
                foreach (var tmpDefinition in tmpDefinitions)
                {
                    if (!definitionObjects.Any(d => d.Type == tmpDefinition.Type))
                    {
                        definitionObjects.Add(tmpDefinition);
                    }
                }
            }
            return definitionObjects;
        }

        private static IList<Definition> ResolveAllDefinitions(IList<Definition> allDefinitions, DefinitionObject bodyDefinitionObject, IList<DefinitionObject> responseDefinitionObjects)
        {
            var definitionObjects = new List<DefinitionObject>();
            responseDefinitionObjects.Add(bodyDefinitionObject);

            foreach (var responseDefinitionObject in responseDefinitionObjects)
            {
                var flattenDefinitionObjects = FlattenDefinitionObject(bodyDefinitionObject);
                foreach (var flattenDefinitionObject in flattenDefinitionObjects)
                {
                    if (!string.IsNullOrEmpty(flattenDefinitionObject.Type)
                    && string.IsNullOrEmpty(flattenDefinitionObject.DiscriminatorKey)
                    && (flattenDefinitionObject.DefinitionObjectType == DefinitionObjectType.Object
                    || flattenDefinitionObject.DefinitionObjectType == DefinitionObjectType.Enum
                    || (flattenDefinitionObject.DefinitionObjectType == DefinitionObjectType.Array && flattenDefinitionObject.PropertyItems?.Count > 0)
                    || (flattenDefinitionObject.DefinitionObjectType == DefinitionObjectType.Array && flattenDefinitionObject.AllOfs?.Count > 0))
                    && !definitionObjects.Any(d => d.Type == flattenDefinitionObject.Type))
                    {
                        definitionObjects.Add(flattenDefinitionObject);
                    }
                }
            }

            var resolvedDefinitions = GetAllDefinitions(definitionObjects);
            foreach (var resolvedDefinition in resolvedDefinitions)
            {
                if (!allDefinitions.Any(d => d.Type == resolvedDefinition.Type))
                {
                    allDefinitions.Add(resolvedDefinition);
                }
            }
            return allDefinitions;
        }

        private static IList<DefinitionEntity> TransformDefinitions(IList<Definition> allDefinitions, List<Definition> parametersDefinitions, DefinitionObject bodyDefinitionObject, IList<DefinitionObject> responseDefinitionObjects)
        {
            allDefinitions = ResolveAllDefinitions(allDefinitions, bodyDefinitionObject, responseDefinitionObjects);
            var definitions = new List<DefinitionEntity>();
            var typesDictionary = new Dictionary<string, bool>();
            var typesQueue = new Queue<string>();
            if (!string.IsNullOrEmpty(bodyDefinitionObject.Type))
            {
                var polymorphicDefinitions = GetPolymorphicDefinitions(allDefinitions, bodyDefinitionObject.Type);
                if (polymorphicDefinitions?.Count > 0)
                {
                    foreach (var polymorphicDefinition in polymorphicDefinitions)
                    {
                        if (!string.IsNullOrEmpty(polymorphicDefinition.Type))
                        {
                            typesQueue.Enqueue(polymorphicDefinition.Type);
                        }
                    }
                }
            }
            var bodyProperties = GetDefinitionProperties(bodyDefinitionObject);
            foreach(var parametersDefinition in parametersDefinitions)
            {
                allDefinitions.Add(parametersDefinition);
                typesQueue.Enqueue(parametersDefinition.Type);
            }
            foreach (var bodyProperty in bodyProperties)
            {
                if (!string.IsNullOrEmpty(bodyProperty.Type)
                    && (bodyProperty.DefinitionObjectType == DefinitionObjectType.Object || bodyProperty.DefinitionObjectType == DefinitionObjectType.Array)
                    && !typesQueue.Any(t => t == bodyProperty.Type))
                {
                    typesQueue.Enqueue(bodyProperty.Type);
                }
            }
            foreach (var responseDefinitionObject in responseDefinitionObjects)
            {
                if (!string.IsNullOrEmpty(responseDefinitionObject.Type)
                    && (responseDefinitionObject.DefinitionObjectType == DefinitionObjectType.Object || responseDefinitionObject.DefinitionObjectType == DefinitionObjectType.Array)
                    && !typesQueue.Any(t => t == responseDefinitionObject.Type))
                {
                    typesQueue.Enqueue(responseDefinitionObject.Type);
                }
            }

            while (typesQueue.Count > 0)
            {
                var type = typesQueue.Dequeue();
                if (typesDictionary.TryGetValue(type, out var typeValue))
                {
                    continue;
                }
                typesDictionary[type] = true;
                var polymorphicDefinitions = GetPolymorphicDefinitions(allDefinitions, type);
                if (polymorphicDefinitions?.Count > 0)
                {
                    foreach (var polymorphicDefinition in polymorphicDefinitions)
                    {
                        if (!string.IsNullOrEmpty(polymorphicDefinition.Type))
                        {
                            typesQueue.Enqueue(polymorphicDefinition.Type);
                        }
                    }
                }
                else
                {
                    var selfDefinition = GetSelfDefinition(allDefinitions, type);
                    if (selfDefinition != null)
                    {
                        if (selfDefinition.DefinitionObjectType == DefinitionObjectType.Enum)
                        {
                            definitions.Add(new DefinitionEntity
                            {
                                Name = selfDefinition.Name,
                                Description = Utility.GetDefinitionDescription(selfDefinition),
                                Kind = "enum",
                                ParameterItems = selfDefinition.EnumValues.Select(p => new DefinitionParameterEntity
                                {
                                    Name = p.Value,
                                    Types = new List<BaseParameterTypeEntity>
                                    {
                                        new BaseParameterTypeEntity
                                        {
                                            Id = "string"
                                        }
                                    },
                                    Description = p.Description
                                }).ToList()
                            });
                        }
                        else if (selfDefinition.DefinitionObjectType != DefinitionObjectType.Simple)
                        {
                            var parameters = GetDefinitionParameters(allDefinitions, selfDefinition, false).ToList();
                            definitions.Add(new DefinitionEntity
                            {
                                Name = selfDefinition.Name,
                                Description = Utility.GetDefinitionDescription(selfDefinition),
                                Kind = "object",
                                ParameterItems = parameters?.Select(p => new DefinitionParameterEntity
                                {
                                    Id = p.Id,
                                    Name = p.Name,
                                    Description = p.Description,
                                    IsReadOnly = p.IsReadOnly,
                                    Types = p.Types,
                                    TypesTitle = p.TypesTitle,
                                    Pattern = p.Pattern,
                                    Format = p.Format
                                }).ToList()
                            });

                            foreach (var definitionProperty in selfDefinition.DefinitionProperties)
                            {
                                if (!string.IsNullOrEmpty(definitionProperty.Type))
                                {
                                    typesQueue.Enqueue(definitionProperty.Type);
                                }
                            }
                        }
                    }
                }
            }

            return definitions;
        }

        #endregion

        #region Security

        private static IList<SecurityEntity> GetAllSecurityEntities(SwaggerModel swaggerModel)
        {
            var allSecurities = new List<SecurityEntity>();
            var securityDefinitionsModel = swaggerModel.Metadata.GetDictionaryFromMetaData<Dictionary<string, JObject>>("securityDefinitions");
            if (securityDefinitionsModel != null)
            {
                foreach (var definition in securityDefinitionsModel)
                {
                    var securityEntity = new SecurityEntity
                    {
                        Key = definition.Key
                    };
                    var definitionValue = definition.Value.ToObject<Dictionary<string, object>>();
                    if(definitionValue != null)
                    {
                        securityEntity.Name = definitionValue.GetValueFromMetaData<string>("name");
                        securityEntity.Type = definitionValue.GetValueFromMetaData<string>("type");
                        securityEntity.Description = definitionValue.GetValueFromMetaData<string>("description");
                        securityEntity.In = definitionValue.GetValueFromMetaData<string>("in");
                        securityEntity.Flow = definitionValue.GetValueFromMetaData<string>("flow");
                        securityEntity.AuthorizationUrl = definitionValue.GetValueFromMetaData<string>("authorizationUrl");
                        securityEntity.TokenUrl = definitionValue.GetValueFromMetaData<string>("tokenUrl");
                        var scopes = definitionValue.GetDictionaryFromMetaData<Dictionary<string, string>>("scopes");
                        if (scopes != null)
                        {
                            securityEntity.Scopes = new List<SecurityScopeEntity>();
                            foreach(var scope in scopes)
                            {
                                securityEntity.Scopes.Add(new SecurityScopeEntity
                                {
                                    Name = scope.Key,
                                    Description = scope.Value
                                });
                            }
                        }
                    }
                    allSecurities.Add(securityEntity);
                }
            }
            return allSecurities;
        }

        private static IList<SecurityEntity> TransformSecurities(SwaggerModel swaggerModel)
        {
            var securities = new List<SecurityEntity>();
            var securitiesModel = swaggerModel.Metadata.GetArrayFromMetaData<JObject>("security");
            if (securitiesModel != null)
            {
                var allSecurities = GetAllSecurityEntities(swaggerModel);
                foreach (var securityModel in securitiesModel)
                {
                    var security = securityModel.ToObject<Dictionary<string, object>>().FirstOrDefault();
                    var foundSecurity = allSecurities.FirstOrDefault(s => s.Key == security.Key);
                    var scopes = ((JArray)security.Value).ToObject<string[]>();
                    var securityEntity = new SecurityEntity
                    {
                        Name = foundSecurity?.Name,
                        Type = foundSecurity?.Type,
                        Description = foundSecurity?.Description,
                        In = foundSecurity?.In,
                        Flow = foundSecurity?.Flow,
                        AuthorizationUrl = foundSecurity?.AuthorizationUrl,
                        TokenUrl = foundSecurity?.TokenUrl,
                        Scopes = scopes == null ? null : foundSecurity.Scopes?.Where(s => scopes.Any(sc => s.Name == sc)).ToList()
                    };
                    securities.Add(securityEntity);
                }
            }

            return securities;
        }

        #endregion

        #region Parse JObject to DefinitionObject

        private static void ResolveObject(string key, JObject nodeObject, DefinitionObject definitionObject, string[] requiredFields = null, string discriminatorKey = null, string discriminatorValue = null)
        {
            if (nodeObject.Type == JTokenType.Object)
            {
                var nodeObjectDict = nodeObject.ToObject<Dictionary<string, object>>();
                var refName = nodeObjectDict.GetValueFromMetaData<string>("x-internal-ref-name");
                if (string.IsNullOrEmpty(refName))
                {
                    refName = nodeObjectDict.GetValueFromMetaData<string>("x-internal-loop-ref-name");
                }
                var currentType = nodeObjectDict.GetValueFromMetaData<string>("type");
                definitionObject.Name = key ?? refName;
                definitionObject.Type = refName;
                definitionObject.Description = nodeObjectDict.GetValueFromMetaData<string>("description");
                definitionObject.Title = nodeObjectDict.GetValueFromMetaData<string>("title");
               
                definitionObject.IsReadOnly = nodeObjectDict.GetValueFromMetaData<bool>("readOnly");
                definitionObject.IsFlatten = definitionObject.IsFlatten ? true : nodeObjectDict.GetValueFromMetaData<bool>("x-ms-client-flatten");

                if (requiredFields != null && requiredFields.Any(v => v == definitionObject.Name))
                {
                    definitionObject.IsRequired = true;
                }
                var requiredProperties = nodeObjectDict.GetArrayFromMetaData<string>("required");

                definitionObject.DiscriminatorKey = nodeObjectDict.GetValueFromMetaData<string>("discriminator");
                definitionObject.DiscriminatorValue = nodeObjectDict.GetValueFromMetaData<string>("x-ms-discriminator-value");
                var discriminatorPropertyKey = definitionObject.DiscriminatorKey;
                var discriminatorPropertyValue = string.IsNullOrEmpty(definitionObject.DiscriminatorValue) ? discriminatorValue : definitionObject.DiscriminatorValue;

                var allOf = nodeObjectDict.GetArrayFromMetaData<JObject>("allOf");
                if (allOf != null && allOf.Count() > 0)
                {
                    definitionObject.DefinitionObjectType = DefinitionObjectType.Object;
                    foreach (var oneAllOf in allOf)
                    {
                        var childDefinitionObject = new DefinitionObject();
                        childDefinitionObject.IsFlatten = definitionObject.IsFlatten;
                        ResolveObject(string.Empty, oneAllOf, childDefinitionObject, requiredProperties, discriminatorPropertyKey, discriminatorPropertyValue);
                        definitionObject.AllOfs.Add(childDefinitionObject);
                    }
                }

                if (nodeObjectDict.GetValueFromMetaData<JObject>("properties") != null)
                {
                    definitionObject.DefinitionObjectType = DefinitionObjectType.Object;
                    var propertiesNode = nodeObjectDict.GetValueFromMetaData<JObject>("properties"); 

                    var properties = propertiesNode.ToObject<Dictionary<string, object>>();
                    foreach (var property in properties)
                    {
                        var childDefinitionObject = new DefinitionObject();
                        ResolveObject(property.Key, (JObject)property.Value, childDefinitionObject, requiredProperties, discriminatorPropertyKey, discriminatorPropertyValue);
                        definitionObject.PropertyItems.Add(childDefinitionObject);
                    }
                }
                else if (nodeObjectDict.GetValueFromMetaData<JObject>("additionalProperties") != null)
                {
                    definitionObject.DefinitionObjectType = DefinitionObjectType.Object;
                    var additionalPropertiesNode = nodeObjectDict.GetValueFromMetaData<JObject>("additionalProperties");
                    var additionalProperties = additionalPropertiesNode.ToObject<Dictionary<string, object>>();
                    var additionalType = additionalProperties.GetValueFromMetaData<string>("type");
                    var additionalPropertyProperties = additionalProperties.GetDictionaryFromMetaData<Dictionary<string, object>>("properties");

                    if (additionalType == "object" && additionalPropertyProperties != null)
                    {
                        var childDefinitionObject = new DefinitionObject();
                        definitionObject.AdditionalType = additionalProperties.GetValueFromMetaData<string>("x-internal-ref-name");
                        ResolveObject(string.Empty, additionalPropertiesNode, childDefinitionObject, requiredProperties, discriminatorPropertyKey, discriminatorPropertyValue);
                        definitionObject.PropertyItems.Add(childDefinitionObject);
                    }
                    else
                    {
                        definitionObject.DefinitionObjectType = DefinitionObjectType.Simple;
                        definitionObject.AdditionalType = additionalType;
                    }
                }
                else if (nodeObjectDict.GetValueFromMetaData<JObject>("items") != null)
                {
                    definitionObject.DefinitionObjectType = DefinitionObjectType.Array;
                    var itemsDefine = nodeObjectDict.GetValueFromMetaData<JObject>("items");
                    if (itemsDefine.TryGetValue("x-internal-ref-name", out var type))
                    {
                        definitionObject.Type = (string)type;
                    }
                    else if (itemsDefine.TryGetValue("x-internal-loop-ref-name", out var loopType))
                    {
                        definitionObject.Type = (string)loopType;
                    }

                    if (itemsDefine.TryGetValue("description", out var subDescription))
                    {
                        definitionObject.SubDescription = (string)subDescription;
                    }
                    if (itemsDefine.TryGetValue("title", out var title))
                    {
                        definitionObject.SubTitle = (string)title;
                    }

                    if (itemsDefine.TryGetValue("x-ms-discriminator-value", out var innerDiscriminatorValue))
                    {
                        definitionObject.DiscriminatorValue = (string)innerDiscriminatorValue;
                        discriminatorPropertyValue = (string)innerDiscriminatorValue;
                    }
                    if (itemsDefine.TryGetValue("discriminator", out var innerDiscriminatorKey))
                    {
                        definitionObject.DiscriminatorKey = (string)innerDiscriminatorKey;
                        discriminatorPropertyKey = (string)innerDiscriminatorKey;
                    }

                    if (itemsDefine.TryGetValue("allOf", out var allOfsNode))
                    {
                        foreach (var oneAllOf in allOfsNode.ToObject<JArray>())
                        {
                            var childDefinitionObject = new DefinitionObject();
                            ResolveObject(string.Empty, (JObject)oneAllOf, childDefinitionObject, requiredProperties, discriminatorPropertyKey, discriminatorPropertyValue);
                            definitionObject.AllOfs.Add(childDefinitionObject);
                        }
                    }
                    if (itemsDefine.TryGetValue("properties", out var propertiesNode))
                    {
                        var properties = propertiesNode.ToObject<Dictionary<string, object>>();
                        foreach (var property in properties)
                        {
                            var childDefinitionObject = new DefinitionObject();

                            ResolveObject(property.Key, (JObject)property.Value, childDefinitionObject, requiredProperties, discriminatorPropertyKey, discriminatorPropertyValue);
                            definitionObject.PropertyItems.Add(childDefinitionObject);
                        }
                    }
                    else if (itemsDefine.TryGetValue("type", out var itemType))
                    {
                        definitionObject.Type = (string)itemType;
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(discriminatorKey) && string.Equals(discriminatorKey, definitionObject.Name))
                    {
                        definitionObject.DiscriminatorKey = discriminatorKey;
                        definitionObject.DiscriminatorValue = discriminatorValue;
                    }

                    var enumValues = nodeObjectDict.GetArrayFromMetaData<string>("enum");
                    if (enumValues != null)
                    {
                        definitionObject.DefinitionObjectType = DefinitionObjectType.Enum;
                        var enumObjects = new List<EnumValue>();
                        foreach (var v in enumValues)
                        {
                            enumObjects.Add(new EnumValue { Value = v });
                        }

                        var enumNode = nodeObjectDict.GetDictionaryFromMetaData<Dictionary<string, object>>("x-ms-enum");
                        if (enumNode != null && enumNode.TryGetValue("name", out var enumName))
                        {
                            definitionObject.Type = (string)enumName;
                        }
                        if (enumNode != null && enumNode.TryGetValue("values", out var enumValue))
                        {
                            var values = (JArray)enumValue;
                            if (values != null)
                            {
                                foreach (var v in values)
                                {
                                    var keyValueEnum = v.ToObject<Dictionary<string, object>>();
                                    var enumV = keyValueEnum.GetValueFromMetaData<string>("value");
                                    var enumObject = enumObjects.FirstOrDefault(q => q.Value == enumV);
                                    if (enumV != null && enumObject != null)
                                    {
                                        enumObject.Description = keyValueEnum.GetValueFromMetaData<string>("description");
                                        var name = keyValueEnum.GetValueFromMetaData<string>("name");
                                        enumObject.Name = string.IsNullOrEmpty(name) ? enumV : name;
                                    }
                                }
                            }
                        }

                        definitionObject.EnumValues = enumObjects;
                    }
                    else if (definitionObject.AllOfs.Count == 0 && definitionObject.PropertyItems.Count == 0)
                    {
                        definitionObject.DefinitionObjectType = DefinitionObjectType.Simple;
                        definitionObject.Type = currentType;
                    }
                }

                if (definitionObject.DefinitionObjectType == DefinitionObjectType.Object || definitionObject.DefinitionObjectType == DefinitionObjectType.Array)
                {
                    if (string.IsNullOrEmpty(definitionObject.Type))
                    {
                        definitionObject.Type = definitionObject.Name.FirstLetterToUpper();
                    }
                }
            }
        }

        private static void ResolveDefinitionClientFlatten(DefinitionObject definitionObject, DefinitionObject parentDefinitionObject = null)
        {
            foreach (var allOf in definitionObject.AllOfs)
            {
                ResolveDefinitionClientFlatten(allOf, definitionObject);
            }

            foreach (var propertyItem in definitionObject.PropertyItems)
            {
                ResolveDefinitionClientFlatten(propertyItem, definitionObject);
                if (propertyItem.IsFlatten)
                {
                    var items = new List<DefinitionObject>();
                    foreach (var item in definitionObject.PropertyItems)
                    {
                        if (!item.IsFlatten || (item.DefinitionObjectType == DefinitionObjectType.Simple && !string.IsNullOrEmpty(item.AdditionalType)))
                        {
                            items.Add(item);
                        }
                    }
                    foreach (var item in propertyItem.PropertyItems)
                    {
                        // todo: should flatten the property as "properties.property"
                        item.Name = propertyItem.Name + "." + item.Name;
                        items.Add(item);
                    }
                    foreach (var allOf in propertyItem.AllOfs)
                    {
                        foreach(var item in allOf.PropertyItems)
                        {
                            item.Name = propertyItem.Name + "." + item.Name;
                        }
                        definitionObject.AllOfs.Add(allOf);
                    }
                     definitionObject.PropertyItems = items;
                    propertyItem.PropertyItems = new List<DefinitionObject>();
                }
            }
        }

        private static DefinitionObject ResolveSchema(JObject nodeObject)
        {
            DefinitionObject definitionObject = new DefinitionObject();
            ResolveObject(string.Empty, nodeObject, definitionObject);
            ResolveDefinitionClientFlatten(definitionObject);
            return definitionObject;
        }

        #endregion
    }
}
