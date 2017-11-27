﻿namespace Microsoft.RestApi.RestTransformer
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Microsoft.DocAsCode.Build.RestApi;
    using Microsoft.DocAsCode.Build.RestApi.Swagger;
    using Microsoft.DocAsCode.DataContracts.RestApi;
    using Microsoft.RestApi.RestTransformer.Models;

    using Newtonsoft.Json.Linq;

    public static class RestOperationGroupTransformer
    {
        public static OperationGroupEntity Transform(SwaggerModel swaggerModel, RestApiRootItemViewModel viewModel, string folder)
        {
            var serviceName = swaggerModel.Metadata.GetValueFromMetaData<string>("x-internal-service-name");
            var groupName = swaggerModel.Metadata.GetValueFromMetaData<string>("x-internal-toc-name");
            var apiVersion = swaggerModel.Info.Version;
            
            var members = swaggerModel.Metadata.GetArrayFromMetaData<JObject>("x-internal-split-members");
            if(members != null && members.Count() > 0)
            {
                var operations = new List<Operation>();
                var operationPaths = Directory.GetFiles(Path.Combine(folder, Utility.TrimWhiteSpace(groupName)), "*.json");
                foreach(var operationPath in operationPaths)
                {
                    var childSwaggerModel = SwaggerJsonParser.Parse(operationPath);
                    var childViewModel = SwaggerModelConverter.FromSwaggerModel(childSwaggerModel);

                    var model = childViewModel.Children.FirstOrDefault();
                    var operationName = childSwaggerModel.Metadata.GetValueFromMetaData<string>("x-internal-operation-name");
                    var operation = new Operation
                    {
                        Id = Utility.TrimWhiteSpace($"{swaggerModel.Host}.{serviceName}.{groupName}.{operationName}")?.ToLower(),
                        Summary = Utility.GetSummary(model?.Summary, model?.Description)
                    };
                    operations.Add(operation);
                }
                return new OperationGroupEntity
                {
                    Id = Utility.TrimWhiteSpace($"{swaggerModel.Host}.{serviceName}.{groupName}")?.ToLower(),
                    ApiVersion = apiVersion,
                    Name = groupName,
                    Operations = operations,
                    Service = serviceName,
                    Summary = Utility.GetSummary(viewModel.Summary, viewModel.Description)
                };
            }
            return null;
        }
    }
}