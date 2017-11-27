﻿namespace Microsoft.RestApi.RestTransformer
{
    using System;
    using System.IO;
    using System.Linq;

    using Microsoft.DocAsCode.Build.RestApi.Swagger;
    using Microsoft.DocAsCode.DataContracts.RestApi;

    public class RestTransformer
    {
        public static readonly YamlDotNet.Serialization.Serializer YamlSerializer = new YamlDotNet.Serialization.Serializer();

        public static void Process(string filePath, SwaggerModel swaggerModel, RestApiRootItemViewModel viewModel, string folder)
        {
            if(viewModel.Metadata.TryGetValue("x-internal-split-type", out var fileType))
            {
                string currentFileType = (string)fileType;
                if (currentFileType == "OperationGroup")
                {
                    var groupInfo = RestOperationGroupTransformer.Transform(swaggerModel, viewModel, folder);
                    if (groupInfo != null)
                    {
                        using (var writer = new StreamWriter(filePath))
                        {
                            writer.WriteLine("### YamlMime:RESTOperationGroup");
                            YamlSerializer.Serialize(writer, groupInfo);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warining: the group has no members: {folder}");
                    }
                    
                    if (File.Exists(Path.ChangeExtension(filePath, ".json")))
                    {
                        File.Delete(Path.ChangeExtension(filePath, ".json"));
                    }
                    else
                    {
                        Console.WriteLine($"There is a duplicate operation group");
                    }
                }
                else if (currentFileType == "Operation")
                {
                    if (viewModel.Children?.Count == 1)
                    {
                        using (var writer = new StreamWriter(filePath))
                        {
                            writer.WriteLine("### YamlMime:RESTOperation");
                            YamlSerializer.Serialize(writer, RestOperationTransformer.Transform(swaggerModel, viewModel.Children.First()));
                        }
                        if (File.Exists(Path.ChangeExtension(filePath, ".json")))
                        {
                            File.Delete(Path.ChangeExtension(filePath, ".json"));
                        }
                        else
                        {
                            Console.WriteLine($"There is a duplicate operation");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Please make sure there is only 1 child here. the actual children number is : {viewModel.Children?.Count}");
                    }
                }
            }
        }
    }
}