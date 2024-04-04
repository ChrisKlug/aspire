// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Sockets;
using Aspire.Hosting.Qdrant;
using Aspire.Hosting.Tests.Utils;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aspire.Hosting.Tests.Qdrant;

public class AddQdrantTests
{
    private const int QdrantPortHttp = 6334;
    private const int QdrantPortDashboard = 6333;

    [Fact]
    public async Task AddQdrantWithDefaultsAddsAnnotationMetadata()
    {
        var appBuilder = DistributedApplication.CreateBuilder();
        appBuilder.AddQdrant("my-qdrant");

        using var app = appBuilder.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var containerResource = Assert.Single(appModel.GetContainerResources());
        Assert.Equal("my-qdrant", containerResource.Name);

        var containerAnnotation = Assert.Single(containerResource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal(QdrantContainerImageTags.Tag, containerAnnotation.Tag);
        Assert.Equal(QdrantContainerImageTags.Image, containerAnnotation.Image);
        Assert.Null(containerAnnotation.Registry);

        var endpoint = containerResource.Annotations.OfType<EndpointAnnotation>()
            .FirstOrDefault(e => e.Name == "http");
        Assert.NotNull(endpoint);
        Assert.Equal(QdrantPortHttp, endpoint.TargetPort);
        Assert.False(endpoint.IsExternal);
        Assert.Equal("http", endpoint.Name);
        Assert.Null(endpoint.Port);
        Assert.Equal(ProtocolType.Tcp, endpoint.Protocol);
        Assert.Equal("http", endpoint.Transport);
        Assert.Equal("http", endpoint.UriScheme);

        var config = await EnvironmentVariableEvaluator.GetEnvironmentVariablesAsync(containerResource);

        Assert.Collection(config,
            env =>
            {
                Assert.Equal("QDRANT__SERVICE__API_KEY", env.Key);
                Assert.False(string.IsNullOrEmpty(env.Value));
            });
    }

    [Fact]
    public void AddQdrantWithDefaultsAndDashboardAddsAnnotationMetadata()
    {
        var appBuilder = DistributedApplication.CreateBuilder();
        appBuilder.AddQdrant("my-qdrant");

        using var app = appBuilder.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var containerResource = Assert.Single(appModel.GetContainerResources());
        Assert.Equal("my-qdrant", containerResource.Name);

        var containerAnnotation = Assert.Single(containerResource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal(QdrantContainerImageTags.Tag, containerAnnotation.Tag);
        Assert.Equal(QdrantContainerImageTags.Image, containerAnnotation.Image);
        Assert.Null(containerAnnotation.Registry);

        var endpoint = containerResource.Annotations.OfType<EndpointAnnotation>()
            .FirstOrDefault(e => e.Name == "rest");

        Assert.NotNull(endpoint);
        Assert.Equal(QdrantPortDashboard, endpoint.TargetPort);
        Assert.False(endpoint.IsExternal);
        Assert.Equal("rest", endpoint.Name);
        Assert.Null(endpoint.Port);
        Assert.Equal(ProtocolType.Tcp, endpoint.Protocol);
        Assert.Equal("http", endpoint.Transport);
        Assert.Equal("http", endpoint.UriScheme);
    }

    [Fact]
    public async Task AddQdrantAddsAnnotationMetadata()
    {
        var appBuilder = DistributedApplication.CreateBuilder();
        appBuilder.Configuration["Parameters:pass"] = "pass";

        var pass = appBuilder.AddParameter("pass");
        appBuilder.AddQdrant("my-qdrant", apiKey: pass);

        using var app = appBuilder.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var containerResource = Assert.Single(appModel.GetContainerResources());
        Assert.Equal("my-qdrant", containerResource.Name);

        var containerAnnotation = Assert.Single(containerResource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal(QdrantContainerImageTags.Tag, containerAnnotation.Tag);
        Assert.Equal(QdrantContainerImageTags.Image, containerAnnotation.Image);
        Assert.Null(containerAnnotation.Registry);

        var endpoint = containerResource.Annotations.OfType<EndpointAnnotation>()
            .FirstOrDefault(e => e.Name == "http");
        Assert.NotNull(endpoint);
        Assert.Equal(QdrantPortHttp, endpoint.TargetPort);
        Assert.False(endpoint.IsExternal);
        Assert.Equal("http", endpoint.Name);
        Assert.Null(endpoint.Port);
        Assert.Equal(ProtocolType.Tcp, endpoint.Protocol);
        Assert.Equal("http", endpoint.Transport);
        Assert.Equal("http", endpoint.UriScheme);

        var config = await EnvironmentVariableEvaluator.GetEnvironmentVariablesAsync(containerResource);

        Assert.Collection(config,
            env =>
            {
                Assert.Equal("QDRANT__SERVICE__API_KEY", env.Key);
                Assert.Equal("pass", env.Value);
            });
    }

    [Fact]
    public async Task QdrantCreatesConnectionString()
    {
        var appBuilder = DistributedApplication.CreateBuilder();

        appBuilder.Configuration["Parameters:pass"] = "pass";
        var pass = appBuilder.AddParameter("pass");

        var qdrant = appBuilder.AddQdrant("my-qdrant", pass)
                                 .WithEndpoint("http", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 6334));

        var connectionStringResource = qdrant.Resource as IResourceWithConnectionString;

        var connectionString = await connectionStringResource.GetConnectionStringAsync();
        Assert.Equal($"Endpoint=http://localhost:6334;Key=pass", connectionString);
    }

    [Fact]
    public async Task QdrantClientAppWithReferenceContainsConnectionStrings()
    {
        using var testProgram = CreateTestProgram();
        var appBuilder = DistributedApplication.CreateBuilder();

        appBuilder.Configuration["Parameters:pass"] = "pass";
        var pass = appBuilder.AddParameter("pass");

        var qdrant = appBuilder.AddQdrant("my-qdrant", pass)
            .WithEndpoint("http", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 6334))
            .WithEndpoint("rest", e => e.AllocatedEndpoint = new AllocatedEndpoint(e, "localhost", 6333));

        var projectA = appBuilder.AddProject<ProjectA>("projecta")
            .WithReference(qdrant);

        // Call environment variable callbacks.
        var config = await EnvironmentVariableEvaluator.GetEnvironmentVariablesAsync(projectA.Resource);

        var servicesKeysCount = config.Keys.Count(k => k.StartsWith("ConnectionStrings__"));
        Assert.Equal(2, servicesKeysCount);

        Assert.Contains(config, kvp => kvp.Key == "ConnectionStrings__my-qdrant" && kvp.Value == "Endpoint=http://localhost:6334;Key=pass");
        Assert.Contains(config, kvp => kvp.Key == "ConnectionStrings__my-qdrant_rest" && kvp.Value == "Endpoint=http://localhost:6333;Key=pass");
    }

    [Fact]
    public async Task VerifyManifest()
    {
        var appBuilder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions() { Args = new string[] { "--publisher", "manifest" } } );
        var qdrant = appBuilder.AddQdrant("qdrant");

        var serverManifest = await ManifestUtils.GetManifest(qdrant.Resource); // using this method does not get any ExecutionContext.IsPublishMode changes

        var expectedManifest = $$"""
            {
              "type": "container.v0",
              "connectionString": "Endpoint={qdrant.bindings.http.scheme}://{qdrant.bindings.http.host}:{qdrant.bindings.http.port};Key={qdrant-Key.value}",
              "image": "{{QdrantContainerImageTags.Image}}:{{QdrantContainerImageTags.Tag}}",
              "env": {
                "QDRANT__SERVICE__API_KEY": "{qdrant-Key.value}",
                "QDRANT__SERVICE__ENABLE_STATIC_CONTENT": "0"
              },
              "bindings": {
                "http": {
                  "scheme": "http",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 6334
                },
                "rest": {
                  "scheme": "http",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 6333
                }
              }
            }
            """;
        Assert.Equal(expectedManifest, serverManifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestWithParameters()
    {
        var appBuilder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions() { Args = new string[] { "--publisher", "manifest" } });

        var apiKeyParameter = appBuilder.AddParameter("QdrantApiKey");
        var qdrant = appBuilder.AddQdrant("qdrant", apiKeyParameter);

        var serverManifest = await ManifestUtils.GetManifest(qdrant.Resource); // using this method does not get any ExecutionContext.IsPublishMode changes

        var expectedManifest = $$"""
            {
              "type": "container.v0",
              "connectionString": "Endpoint={qdrant.bindings.http.scheme}://{qdrant.bindings.http.host}:{qdrant.bindings.http.port};Key={QdrantApiKey.value}",
              "image": "{{QdrantContainerImageTags.Image}}:{{QdrantContainerImageTags.Tag}}",
              "env": {
                "QDRANT__SERVICE__API_KEY": "{QdrantApiKey.value}",
                "QDRANT__SERVICE__ENABLE_STATIC_CONTENT": "0"
              },
              "bindings": {
                "http": {
                  "scheme": "http",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 6334
                },
                "rest": {
                  "scheme": "http",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 6333
                }
              }
            }
            """;
        Assert.Equal(expectedManifest, serverManifest.ToString());
    }

    private static TestProgram CreateTestProgram(string[]? args = null) => TestProgram.Create<AddQdrantTests>(args);

    private sealed class ProjectA : IProjectMetadata
    {
        public string ProjectPath => "projectA";

        public LaunchSettings LaunchSettings { get; } = new();
    }
}